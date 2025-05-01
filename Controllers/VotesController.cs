// Controllers/VotesController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackOverStadyApi.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace StackOverStadyApi.Controllers
{
    [Route("api")] // Общий префикс для голосования
    [ApiController]
    [Authorize] // Все действия требуют авторизации
    public class VotesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public VotesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public class VoteInputModel
        {
            [Required]
            public string VoteType { get; set; } // Ожидаем Up или Down
        }

        // POST /api/questions/{questionId}/vote
        [HttpPost("questions/{questionId}/vote")]
        public async Task<IActionResult> VoteForQuestion(int questionId, [FromBody] VoteInputModel model)
        {
            if (!Enum.TryParse<VoteType>(model.VoteType, ignoreCase: true, out var parsedVoteType))
            {
                return BadRequest($"Invalid vote type: {model.VoteType}");
            }

            return await ProcessVote(questionId, null, parsedVoteType);
        }

        // POST /api/answers/{answerId}/vote
        [HttpPost("answers/{answerId}/vote")]
        public async Task<IActionResult> VoteForAnswer(int answerId, [FromBody] VoteInputModel model)
        {
            if (!Enum.TryParse<VoteType>(model.VoteType, ignoreCase: true, out var parsedVoteType))
            {
                return BadRequest($"Invalid vote type: {model.VoteType}");
            }

            return await ProcessVote(null, answerId, parsedVoteType);
        }


        // --- Приватный метод для обработки голоса ---
        private async Task<IActionResult> ProcessVote(int? questionId, int? answerId, VoteType voteType)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var userId))
            {
                return Unauthorized("Не удалось определить пользователя.");
            }

            // Находим существующий голос пользователя за этот пост (если есть)
            var existingVote = await _context.Votes
                .FirstOrDefaultAsync(v => v.UserId == userId && v.QuestionId == questionId && v.AnswerId == answerId);

            int ratingChange = 0;
            string action = "voted";

            using var transaction = await _context.Database.BeginTransactionAsync(); // Используем транзакцию

            try
            {
                if (existingVote != null)
                {
                    // Голос уже существует
                    if (existingVote.VoteType == voteType)
                    {
                        // Пользователь отменяет свой голос (нажал ту же кнопку)
                        _context.Votes.Remove(existingVote);
                        ratingChange = -(int)voteType; // Изменение рейтинга противоположно типу голоса
                        action = "removed vote";
                        Console.WriteLine($"[Vote] User {userId} removed {voteType} vote for Q:{questionId}/A:{answerId}");
                    }
                    else
                    {
                        // Пользователь меняет свой голос (нажал другую кнопку)
                        ratingChange = (int)voteType - (int)existingVote.VoteType; // Разница между новым и старым
                        existingVote.VoteType = voteType; // Обновляем тип голоса
                        existingVote.VotedAt = DateTime.UtcNow;
                        _context.Votes.Update(existingVote);
                        action = "changed vote";
                        Console.WriteLine($"[Vote] User {userId} changed vote to {voteType} for Q:{questionId}/A:{answerId}");
                    }
                }
                else
                {
                    // Новый голос
                    var newVote = new Vote
                    {
                        UserId = userId,
                        QuestionId = questionId,
                        AnswerId = answerId,
                        VoteType = voteType,
                        VotedAt = DateTime.UtcNow
                    };
                    _context.Votes.Add(newVote);
                    ratingChange = (int)voteType; // Изменение рейтинга равно типу голоса
                    action = "added vote";
                    Console.WriteLine($"[Vote] User {userId} added {voteType} vote for Q:{questionId}/A:{answerId}");
                }

                // Обновляем рейтинг поста
                if (ratingChange != 0)
                {
                    if (questionId.HasValue)
                    {
                        // Обновляем рейтинг вопроса атомарно
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE \"Questions\" SET \"Rating\" = \"Rating\" + {ratingChange} WHERE \"Id\" = {questionId}");
                        Console.WriteLine($"[Vote] Question {questionId} rating changed by {ratingChange}");
                    }
                    else if (answerId.HasValue)
                    {
                        // Обновляем рейтинг ответа атомарно
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE \"Answers\" SET \"Rating\" = \"Rating\" + {ratingChange} WHERE \"Id\" = {answerId}");
                        Console.WriteLine($"[Vote] Answer {answerId} rating changed by {ratingChange}");
                    }
                }

                await _context.SaveChangesAsync(); // Сохраняем изменения голосов
                await transaction.CommitAsync(); // Фиксируем транзакцию

                // Получаем новый рейтинг для возврата клиенту
                int newRating = 0;
                if (questionId.HasValue)
                {
                    newRating = await _context.Questions.Where(q => q.Id == questionId.Value).Select(q => q.Rating).FirstOrDefaultAsync();
                }
                else if (answerId.HasValue)
                {
                    newRating = await _context.Answers.Where(a => a.Id == answerId.Value).Select(a => a.Rating).FirstOrDefaultAsync();
                }

                return Ok(new { action, newRating }); // Возвращаем результат и новый рейтинг
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[ERROR Vote] Failed to process vote for Q:{questionId}/A:{answerId}. User: {userId}. Error: {ex.Message}");
                return StatusCode(500, "Ошибка при обработке голоса.");
            }
        }
    }
}