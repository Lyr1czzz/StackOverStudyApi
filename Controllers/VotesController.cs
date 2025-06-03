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
                // Эта ошибка вернет 401, а не 500, если проблема здесь.
                return Unauthorized("Не удалось определить пользователя.");
            }

            // --- ВОЗМОЖНАЯ ТОЧКА ОШИБКИ 1: Взаимодействие с БД для existingVote ---
            Vote? existingVote = null; // Объявляем заранее
            try
            {
                existingVote = await _context.Votes
                    .FirstOrDefaultAsync(v => v.UserId == userId && v.QuestionId == questionId && v.AnswerId == answerId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR VotesController.ProcessVote] Error fetching existing vote: {ex.ToString()}");
                // Если здесь Npgsql.PostgresException, то проблема с подключением/запросом к таблице Votes
                return StatusCode(500, "Ошибка при проверке существующего голоса.");
            }

            int ratingChange = 0;
            string action = "voted";

            // --- ВОЗМОЖНАЯ ТОЧКА ОШИБКИ 2: Начало транзакции ---
            // Хотя маловероятно, если другие операции с БД работают
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
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
                        // _context.Votes.Update(existingVote); // Update не обязателен для отслеживаемых сущностей
                    }
                }
                else
                {
                    // --- ВОЗМОЖНАЯ ТОЧКА ОШИBКИ 3: Проверка, не голосует ли автор за свой пост ---
                    // Если эта логика есть, и она падает, может быть 500.
                    // Например, если questionId или answerId null, а мы пытаемся найти пост для проверки автора
                    if (questionId.HasValue)
                    {
                        var question = await _context.Questions.FindAsync(questionId.Value);
                        if (question != null && question.AuthorId == userId)
                        {
                            await transaction.RollbackAsync(); // Откатываем транзакцию перед выходом
                            return BadRequest(new { message = "Вы не можете голосовать за свой собственный вопрос." });
                        }
                    }
                    else if (answerId.HasValue)
                    {
                        var answer = await _context.Answers.FindAsync(answerId.Value);
                        if (answer != null && answer.AuthorId == userId)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { message = "Вы не можете голосовать за свой собственный ответ." });
                        }
                    }
                    // --- КОНЕЦ ПРОВЕРКИ ---

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

                // --- ВОЗМОЖНАЯ ТОЧКА ОШИБКИ 4: SaveChanges для таблицы Votes ---
                await _context.SaveChangesAsync(); // Сохраняем изменения голосов

                // --- ВОЗМОЖНАЯ ТОЧКА ОШИБКИ 5: ExecuteSqlInterpolatedAsync ---
                if (ratingChange != 0)
                {
                    if (questionId.HasValue)
                    {
                        // Убедись, что имя таблицы "Questions" и колонок "Rating", "Id" ТОЧНО совпадает с БД на Render
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE \"Questions\" SET \"Rating\" = \"Rating\" + {ratingChange} WHERE \"Id\" = {questionId.Value}");
                    }
                    else if (answerId.HasValue)
                    {
                        // Убедись, что имя таблицы "Answers" и колонок "Rating", "Id" ТОЧНО совпадает
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE \"Answers\" SET \"Rating\" = \"Rating\" + {ratingChange} WHERE \"Id\" = {answerId.Value}");
                    }
                }

                await transaction.CommitAsync();

                int newRating = 0;
                // --- ВОЗМОЖНАЯ ТОЧКА ОШИБКИ 6: Получение нового рейтинга ---
                if (questionId.HasValue)
                {
                    newRating = await _context.Questions.Where(q => q.Id == questionId.Value).Select(q => q.Rating).FirstOrDefaultAsync();
                }
                else if (answerId.HasValue)
                {
                    newRating = await _context.Answers.Where(a => a.Id == answerId.Value).Select(a => a.Rating).FirstOrDefaultAsync();
                }

                return Ok(new { action, newRating });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // Это должно логироваться на Render!
                Console.WriteLine($"[CRITICAL ERROR VotesController.ProcessVote] User: {userId}, QID: {questionId}, AID: {answerId}, VoteType: {voteType}. Error: {ex.ToString()}");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера при обработке голоса.", details = ex.Message }); // Возвращаем детали ошибки для отладки
            }
        }
    }
}