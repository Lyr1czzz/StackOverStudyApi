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
                        // Загружаем сущность контента с отслеживанием состояния
                        int? authorId = null;
                        Question questionEntity = null;
                        Answer answerEntity = null;

                        if (questionId.HasValue)
                        {
                            questionEntity = await _context.Questions
                                .FirstOrDefaultAsync(q => q.Id == questionId.Value);
                            if (questionEntity == null) return NotFound("Вопрос не найден.");
                            authorId = questionEntity.AuthorId;
                        }
                        else if (answerId.HasValue)
                        {
                            answerEntity = await _context.Answers
                                .FirstOrDefaultAsync(a => a.Id == answerId.Value);
                            if (answerEntity == null) return NotFound("Ответ не найден.");
                            authorId = answerEntity.AuthorId;
                        }
                        else return BadRequest("Должен быть указан вопрос или ответ.");

                        // Проверка на голосование за свой пост
                        Vote? existingVote = await _context.Votes
                            .FirstOrDefaultAsync(v => v.UserId == userId &&
                                                    v.QuestionId == questionId &&
                                                    v.AnswerId == answerId);

                        if (existingVote == null && authorId == userId)
                        {
                            return BadRequest(new { message = "Вы не можете голосовать за свой собственный пост." });
                        }

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

                        if (ratingChange != 0)
                        {
                            // Обновляем рейтинг контента
                            if (questionEntity != null)
                            {
                                questionEntity.Rating += ratingChange;
                            }
                            else if (answerEntity != null)
                            {
                                answerEntity.Rating += ratingChange;
                            }

                            // Обновляем рейтинг автора
                            var author = await _context.Users.FindAsync(authorId);
                            if (author == null)
                            {
                                await transaction.RollbackAsync();
                                return NotFound("Автор не найден.");
                            }
                            author.Rating += ratingChange;
                        }

                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        // Получаем обновленный рейтинг контента
                        int newRating = 0;
                        if (questionEntity != null)
                        {
                            newRating = questionEntity.Rating;
                        }
                        else if (answerEntity != null)
                        {
                            newRating = answerEntity.Rating;
                        }

                        return Ok(new { action, newRating });
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        Console.WriteLine($"Error in ProcessVote: {ex}");
                        return StatusCode(500, new
                        {
                            message = "Внутренняя ошибка сервера при обработке голоса",
                            details = ex.Message
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL ERROR] User: {userId}, QID: {questionId}, AID: {answerId}, VoteType: {voteType}. Error: {ex}");
                return StatusCode(500, new
                {
                    message = "Внутренняя ошибка сервера при обработке голоса",
                    details = ex.Message
                });
            }
        }
    }
}