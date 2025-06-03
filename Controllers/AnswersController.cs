// src/Controllers/AnswersController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackOverStadyApi.Models;
using System.Security.Claims;
using System.Threading.Tasks;

namespace StackOverStadyApi.Controllers
{
    [Route("api/[controller]")] // Префикс /api/answers
    [ApiController]
    public class AnswersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AnswersController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpDelete("{answerId}")]
        [Authorize(Policy = "RequireModeratorRole")]
        public async Task<IActionResult> DeleteAnswer(int answerId)
        {
            var answer = await _context.Answers.FindAsync(answerId);

            if (answer == null)
            {
                return NotFound(new { message = "Ответ не найден." });
            }
            // Аналогично, EF Core должен удалить связанные комментарии и голоса,
            // если настроено каскадное удаление в ApplicationDbContext.
            // У тебя это настроено для Vote и Comment по отношению к AnswerId.
            _context.Answers.Remove(answer);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST /api/answers/{answerId}/accept
        [HttpPost("{answerId}/accept")]
        [Authorize] // Только авторизованные пользователи
        public async Task<IActionResult> AcceptAnswer(int answerId)
        {
            // 1. Получаем ID текущего пользователя
            var currentUserIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdString, out var currentUserId))
            {
                return Unauthorized(new { message = "Не удалось определить ID пользователя." });
            }

            // 2. Находим ответ, который пытаются принять, вместе с его вопросом
            var answerToAccept = await _context.Answers
                .Include(a => a.Question) // Нам нужен Question, чтобы проверить автора вопроса
                .FirstOrDefaultAsync(a => a.Id == answerId);

            if (answerToAccept == null)
            {
                return NotFound(new { message = "Ответ не найден." });
            }

            if (answerToAccept.Question == null)
            {
                // Эта ситуация не должна возникать при корректных данных, но лучше проверить
                Console.WriteLine($"[ERROR AcceptAnswer]: Answer ID {answerId} has no associated Question in the database.");
                return StatusCode(500, new { message = "Ошибка данных: у ответа отсутствует связанный вопрос." });
            }

            // 3. Проверяем, является ли текущий пользователь автором вопроса
            if (answerToAccept.Question.AuthorId != currentUserId)
            {
                return Forbid("Вы не являетесь автором вопроса и не можете принять этот ответ.");
            }

            // 4. Если ответ уже принят, просто возвращаем ОК
            // (или можно реализовать логику отмены принятия ответа)
            if (answerToAccept.IsAccepted)
            {
                // Если хотим разрешить отмену принятия
                
                answerToAccept.IsAccepted = false;
                await _context.SaveChangesAsync();
                return Ok(new {
                    message = "Принятие ответа отменено.",
                    acceptedAnswerId = (int?)null, // Указываем, что принятого ответа больше нет
                    questionId = answerToAccept.QuestionId
                });
            }

            // 5. Логика принятия ответа (с транзакцией для атомарности)
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Находим все другие ответы на этот вопрос, которые УЖЕ могли быть приняты
                var questionId = answerToAccept.QuestionId;
                var otherAcceptedAnswers = await _context.Answers
                    .Where(a => a.QuestionId == questionId && a.Id != answerToAccept.Id && a.IsAccepted)
                    .ToListAsync();

                // Снимаем флаг IsAccepted у других ранее принятых ответов (если такие были)
                // Это гарантирует, что только один ответ на вопрос может быть принят одновременно.
                foreach (var otherAnswer in otherAcceptedAnswers)
                {
                    otherAnswer.IsAccepted = false;
                }

                // Устанавливаем IsAccepted = true для текущего ответа
                answerToAccept.IsAccepted = true;

                await _context.SaveChangesAsync(); // Сохраняем все изменения
                await transaction.CommitAsync();   // Фиксируем транзакцию

                return Ok(new
                {
                    message = "Ответ успешно принят.",
                    acceptedAnswerId = answerToAccept.Id, // Возвращаем ID принятого ответа
                    questionId = answerToAccept.QuestionId // И ID вопроса для удобства клиента
                });
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[DB ERROR AcceptAnswer]: {ex.ToString()}");
                return StatusCode(500, new { message = "Ошибка базы данных при принятии ответа." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[ERROR AcceptAnswer]: {ex.ToString()}");
                return StatusCode(500, new { message = ex.Data.Values.ToString() });
            }
        }
    }
}