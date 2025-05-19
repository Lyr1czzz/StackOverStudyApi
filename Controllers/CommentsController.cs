// Controllers/CommentsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackOverStadyApi.Models;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using AutoMapper; // Добавляем AutoMapper
using AutoMapper.QueryableExtensions; // Для ProjectTo

namespace StackOverStadyApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CommentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper; // Добавляем IMapper

        // --- DTO для Комментариев ---
        public class CommentDto
        {
            public int Id { get; set; }
            public string Text { get; set; }
            public DateTime CreatedAt { get; set; }
            public UserInfoDto User { get; set; } // Используем UserInfoDto
        }

        public class CommentCreateDto
        {
            [Required(ErrorMessage = "Текст комментария обязателен")]
            [MaxLength(500, ErrorMessage = "Максимальная длина комментария 500 символов")]
            public string Text { get; set; }
        }

        // DTO для UserInfo (можно вынести в общее место)
        public class UserInfoDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string PictureUrl { get; set; }
        }
        // ---------------------------

        // Добавляем маппинг в конструктор
        public CommentsController(ApplicationDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper; // Инициализируем IMapper
        }

        [HttpDelete("{commentId}")] // Это даст /api/Comments/{commentId} для DELETE запросов
        public async Task<IActionResult> DeleteComment(int commentId)
        {
            var comment = await _context.Comments.FindAsync(commentId);

            if (comment == null)
            {
                return NotFound(new { message = "Комментарий не найден." }); // Это правильный ответ 404, если комментарий ДЕЙСТВИТЕЛЬНО не найден
            }

             var currentUserIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(currentUserIdString, out var currentUserId);
            bool isAuthor = comment.UserId == currentUserId;
            bool canModerate = User.IsInRole(UserRole.Moderator.ToString()) || User.IsInRole(UserRole.Admin.ToString());
            if (!isAuthor && !canModerate)
            {
                return Forbid();
            }


            try
            {
                _context.Comments.Remove(comment);
                await _context.SaveChangesAsync();
                return NoContent(); // Успешное удаление
            }
            catch (DbUpdateException ex)
            {
                // Логирование ошибки
                Console.WriteLine($"[DB ERROR DeleteComment ID: {commentId}]: {ex.ToString()}");
                return StatusCode(500, new { message = "Ошибка базы данных при удалении комментария." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR DeleteComment ID: {commentId}]: {ex.ToString()}");
                return StatusCode(500, new { message = "Внутренняя ошибка сервера при удалении комментария." });
            }
        }

        // GET /api/questions/{questionId}/comments
        [HttpGet("/api/questions/{questionId}/comments")]
        public async Task<ActionResult<IEnumerable<CommentDto>>> GetQuestionComments(int questionId)
        {
            Console.WriteLine($"[GetComments] Request for Question ID: {questionId}");
            var comments = await _context.Comments
                .Where(c => c.QuestionId == questionId)
                .OrderBy(c => c.CreatedAt)
                .AsNoTracking()
                 // Проецируем в DTO с помощью AutoMapper
                 .ProjectTo<CommentDto>(_mapper.ConfigurationProvider)
                .ToListAsync();
            return Ok(comments);
        }

        // GET /api/answers/{answerId}/comments
        [HttpGet("/api/answers/{answerId}/comments")]
        public async Task<ActionResult<IEnumerable<CommentDto>>> GetAnswerComments(int answerId)
        {
            Console.WriteLine($"[GetComments] Request for Answer ID: {answerId}");
            var comments = await _context.Comments
                .Where(c => c.AnswerId == answerId)
                .OrderBy(c => c.CreatedAt)
                .AsNoTracking()
                 .ProjectTo<CommentDto>(_mapper.ConfigurationProvider)
                .ToListAsync();
            return Ok(comments);
        }

        // POST /api/questions/{questionId}/comments
        [HttpPost("/api/questions/{questionId}/comments")]
        [Authorize] // Требует авторизации
        public async Task<ActionResult<CommentDto>> PostCommentToQuestion(int questionId, [FromBody] CommentCreateDto commentDto)
        {
            return await AddComment(questionId, null, commentDto);
        }

        // POST /api/answers/{answerId}/comments
        [HttpPost("/api/answers/{answerId}/comments")]
        [Authorize] // Требует авторизации
        public async Task<ActionResult<CommentDto>> PostCommentToAnswer(int answerId, [FromBody] CommentCreateDto commentDto)
        {
            return await AddComment(null, answerId, commentDto);
        }


        // --- Приватный метод добавления комментария ---
        private async Task<ActionResult<CommentDto>> AddComment(int? questionId, int? answerId, CommentCreateDto commentDto)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var userId))
            {
                return Unauthorized("Не удалось определить пользователя.");
            }
            Console.WriteLine($"[AddComment] User ID: {userId}, QID: {questionId}, AID: {answerId}");

            // Проверяем существование цели (вопроса или ответа)
            bool targetExists = questionId.HasValue
                ? await _context.Questions.AnyAsync(q => q.Id == questionId.Value)
                : await _context.Answers.AnyAsync(a => a.Id == answerId.Value);

            if (!targetExists)
            {
                Console.WriteLine($"[AddComment] Target not found (QID: {questionId}, AID: {answerId})");
                return NotFound("Объект для комментирования не найден.");
            }

            // Проверка существования юзера (опционально)
            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
            if (!userExists) return NotFound("Пользователь не найден.");


            try
            {
                var comment = new Comment
                {
                    Text = commentDto.Text,
                    UserId = userId,
                    QuestionId = questionId,
                    AnswerId = answerId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Comments.Add(comment);
                await _context.SaveChangesAsync();
                Console.WriteLine($"[AddComment] Comment {comment.Id} created successfully.");

                // Загружаем автора для маппинга
                await _context.Entry(comment).Reference(c => c.User).LoadAsync();

                // Маппим в DTO для ответа
                var resultDto = _mapper.Map<CommentDto>(comment);

                return Ok(resultDto); // Возвращаем 200 OK с созданным комментарием
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR AddComment] Failed to add comment. Error: {ex.Message}");
                return StatusCode(500, "Ошибка при добавлении комментария.");
            }
        }
    }

    // --- Добавьте этот маппинг в ваш MappingProfile.cs ---
    // CreateMap<User, CommentsController.UserInfoDto>(); // Если еще не было
    // CreateMap<Comment, CommentsController.CommentDto>()
    //      .ForMember(dest => dest.User, opt => opt.MapFrom(src => src.User));
}