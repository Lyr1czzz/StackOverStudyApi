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
        // Controllers/VotesController.cs
        private async Task<IActionResult> ProcessVote(int? questionId, int? answerId, VoteType voteType)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var userId))
            {
                return Unauthorized("Не удалось определить пользователя.");
            }

            var executionStrategy = _context.Database.CreateExecutionStrategy();

            try
            {
                return await executionStrategy.ExecuteAsync<IActionResult>(async () =>
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        // Перезагружаем данные внутри блока для корректной работы повторов
                        Vote? existingVote = await _context.Votes
                            .FirstOrDefaultAsync(v => v.UserId == userId &&
                                                    v.QuestionId == questionId &&
                                                    v.AnswerId == answerId);

                        int ratingChange = 0;
                        string action = "voted";

                        if (existingVote != null)
                        {
                            if (existingVote.VoteType == voteType)
                            {
                                _context.Votes.Remove(existingVote);
                                ratingChange = -(int)voteType;
                                action = "removed vote";
                            }
                            else
                            {
                                ratingChange = (int)voteType - (int)existingVote.VoteType;
                                existingVote.VoteType = voteType;
                                existingVote.VotedAt = DateTime.UtcNow;
                            }
                        }
                        else
                        {
                            // Проверка на голосование за свой пост
                            if (questionId.HasValue)
                            {
                                var question = await _context.Questions
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(q => q.Id == questionId.Value);

                                if (question != null && question.AuthorId == userId)
                                {
                                    return BadRequest(new { message = "Вы не можете голосовать за свой собственный вопрос." });
                                }
                            }
                            else if (answerId.HasValue)
                            {
                                var answer = await _context.Answers
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(a => a.Id == answerId.Value);

                                if (answer != null && answer.AuthorId == userId)
                                {
                                    return BadRequest(new { message = "Вы не можете голосовать за свой собственный ответ." });
                                }
                            }

                            var newVote = new Vote
                            {
                                UserId = userId,
                                QuestionId = questionId,
                                AnswerId = answerId,
                                VoteType = voteType,
                                VotedAt = DateTime.UtcNow
                            };
                            _context.Votes.Add(newVote);
                            ratingChange = (int)voteType;
                            action = "added vote";
                        }

                        await _context.SaveChangesAsync();

                        if (ratingChange != 0)
                        {
                            if (questionId.HasValue)
                            {
                                await _context.Database.ExecuteSqlInterpolatedAsync(
                                    $"UPDATE \"Questions\" SET \"Rating\" = \"Rating\" + {ratingChange} WHERE \"Id\" = {questionId.Value}");
                            }
                            else if (answerId.HasValue)
                            {
                                await _context.Database.ExecuteSqlInterpolatedAsync(
                                    $"UPDATE \"Answers\" SET \"Rating\" = \"Rating\" + {ratingChange} WHERE \"Id\" = {answerId.Value}");
                            }
                        }

                        await transaction.CommitAsync();

                        // Получаем обновленный рейтинг
                        int newRating = 0;
                        if (questionId.HasValue)
                        {
                            newRating = await _context.Questions
                                .AsNoTracking()
                                .Where(q => q.Id == questionId.Value)
                                .Select(q => q.Rating)
                                .FirstOrDefaultAsync();
                        }
                        else if (answerId.HasValue)
                        {
                            newRating = await _context.Answers
                                .AsNoTracking()
                                .Where(a => a.Id == answerId.Value)
                                .Select(a => a.Rating)
                                .FirstOrDefaultAsync();
                        }

                        return Ok(new { action, newRating });
                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL ERROR VotesController.ProcessVote] User: {userId}, QID: {questionId}, AID: {answerId}, VoteType: {voteType}. Error: {ex}");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера при обработке голоса", details = ex.Message });
            }
        }
    }
}