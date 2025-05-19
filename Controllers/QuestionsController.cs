using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using StackOverStadyApi.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace StackOverStadyApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuestionsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;

        public QuestionsController(ApplicationDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }



        // DTO для отображения информации об авторе (уже есть)
        public class UserInfoDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string PictureUrl { get; set; }
            // Можно добавить рейтинг пользователя, если нужно в карточке
            // public int Rating { get; set; }
        }

        // DTO для отображения ответа
        public class AnswerDto
        {
            public int Id { get; set; }
            public string Content { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool IsAccepted { get; set; }
            public int Rating { get; set; }
            public UserInfoDto Author { get; set; } // Автор ответа
            public int QuestionId { get; set; } // ID вопроса, к которому он относится
        }

        // DTO для отображения вопроса в СПИСКЕ
        // Добавляем количество ответов для отображения на карточке
        public class QuestionDto // Этот DTO уже был, добавляем поле
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string Content { get; set; } // 🔥 Убедитесь, что это поле есть
            public DateTime CreatedAt { get; set; }
            public UserInfoDto Author { get; set; }
            public List<TagDto> Tags { get; set; } = new();
            public int AnswerCount { get; set; }
            public int Rating { get; set; }
            public bool HasAcceptedAnswer { get; set; }
        }

        // DTO для ДЕТАЛЬНОГО отображения вопроса (включает ответы)
        public class QuestionDetailDto // <<< Новый DTO
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string Content { get; set; } // Полный контент
            public DateTime CreatedAt { get; set; }
            public UserInfoDto Author { get; set; }
            public List<TagDto> Tags { get; set; } = new();
            public int Rating { get; set; }
            public List<AnswerDto> Answers { get; set; } = new(); // <<< Список ответов
        }

        // DTO для СОЗДАНИЯ ответа
        public class AnswerCreateDto // <<< Новый DTO
        {
            [Required(ErrorMessage = "Текст ответа не может быть пустым")]
            [MinLength(10, ErrorMessage = "Ответ должен содержать не менее 10 символов")]
            public string Content { get; set; }
        }

        public class QuestionCreateDto
        {
            // This remains List<string> as the frontend will send the names of selected tags
            [Required]
            [MinLength(1, ErrorMessage = "Хотя бы один тег должен быть указан.")]
            public List<string> Tags { get; set; } = new List<string>();

            [Required(ErrorMessage = "Заголовок не может быть пустым.")]
            [StringLength(200, MinimumLength = 5, ErrorMessage = "Заголовок должен содержать от 5 до 200 символов.")]
            public string Title { get; set; }

            [Required(ErrorMessage = "Содержимое вопроса не может быть пустым.")]
            [MinLength(20, ErrorMessage = "Содержимое вопроса должно содержать не менее 20 символов.")]
            public string Content { get; set; }
        }

        public class VoteRequestDto // Этот DTO может быть общим или дублироваться
        {
            [Required]
            public string VoteType { get; set; } // "Up" or "Down"
        }

        [HttpDelete("{questionId}")]
        [Authorize(Policy = "RequireModeratorRole")]
        public async Task<IActionResult> DeleteQuestion(int questionId)
        {
            var question = await _context.Questions.FindAsync(questionId); // Проще найти по ID

            if (question == null)
            {
                return NotFound(new { message = "Вопрос не найден." });
            }

            // EF Core должен удалить связанные Answers, если в ApplicationDbContext
            // для связи Question -> Answers настроено OnDelete(DeleteBehavior.Cascade).
            // У тебя это настроено:
            // .OnDelete(DeleteBehavior.Cascade); // Удалять ответы при удалении вопроса
            // То же самое касается комментариев и голосов, если они каскадно связаны с ответами/вопросом.

            _context.Questions.Remove(question);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // GET /api/Questions - Получение списка вопросов
        [HttpGet]
        public async Task<ActionResult<PaginatedResult<QuestionDto>>> GetAllQuestions(
            [FromQuery] string sort = "newest",
            [FromQuery] string? tags = null,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var query = _context.Questions.AsNoTracking();

                if (!string.IsNullOrEmpty(tags))
                {
                    var tagList = tags
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim().ToLower())
                        .ToList();
                    if (tagList.Any())
                    {
                        query = query.Where(q => q.Tags.Any(t => tagList.Contains(t.Name.ToLower())));
                    }
                }

                if (!string.IsNullOrEmpty(search))
                {
                    string searchTermLower = search.ToLower(); // Преобразуем в нижний регистр
                    query = query.Where(q => (q.Title != null && q.Title.ToLower().Contains(searchTermLower)) ||
                                             (q.Content != null && q.Content.ToLower().Contains(searchTermLower)));
                    // Добавил проверки на null для Title и Content на всякий случай, хотя они должны быть Required
                }

                IOrderedQueryable<Question> orderedQuery;
                switch (sort)
                {
                    case "votes":
                        orderedQuery = query.OrderByDescending(q => q.Rating).ThenByDescending(q => q.CreatedAt);
                        break;
                    case "active":
                        orderedQuery = query.OrderByDescending(q => q.Answers.Any() ? q.Answers.Max(a => a.CreatedAt) : q.CreatedAt)
                                            .ThenByDescending(q => q.CreatedAt);
                        break;
                    case "newest":
                    default:
                        orderedQuery = query.OrderByDescending(q => q.CreatedAt);
                        break;
                }

                var totalCount = await orderedQuery.CountAsync(); // Считаем от уже отфильтрованного и готового к сортировке запроса

                var questionIds = await orderedQuery
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(q => q.Id)
                    .ToListAsync();

                if (!questionIds.Any())
                {
                    return Ok(new PaginatedResult<QuestionDto>
                    {
                        Items = new List<QuestionDto>(),
                        TotalCount = totalCount
                    });
                }

                // Загружаем вопросы, включая необходимые связанные данные для маппинга
                var questionsQuery = _context.Questions
                    .AsNoTracking()
                    .Include(q => q.Author)
                    .Include(q => q.Tags)
                    .Include(q => q.Answers) // Необходимо для AnswerCount и HasAcceptedAnswer в маппере
                    .Where(q => questionIds.Contains(q.Id));

                List<Question> questions;
                // ПОВТОРНО ПРИМЕНЯЕМ СОРТИРОВКУ
                switch (sort)
                {
                    case "votes":
                        questions = await questionsQuery.OrderByDescending(q => q.Rating).ThenByDescending(q => q.CreatedAt).ToListAsync();
                        break;
                    case "active":
                        questions = await questionsQuery
                                            .OrderByDescending(q => q.Answers.Any() ? q.Answers.Max(a => a.CreatedAt) : q.CreatedAt)
                                            .ThenByDescending(q => q.CreatedAt)
                                            .ToListAsync();
                        break;
                    case "newest":
                    default:
                        questions = await questionsQuery.OrderByDescending(q => q.CreatedAt).ToListAsync();
                        break;
                }

                // Альтернативный способ сохранить порядок из questionIds, если он критичен (менее производительно для БД)
                // var tempQuestions = await questionsQuery.ToListAsync();
                // questions = tempQuestions.OrderBy(q => questionIds.IndexOf(q.Id)).ToList();


                var questionDtos = _mapper.Map<List<QuestionDto>>(questions);

                return Ok(new PaginatedResult<QuestionDto>
                {
                    Items = questionDtos,
                    TotalCount = totalCount
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR GetAllQuestions]: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[INNER EXCEPTION GetAllQuestions]: {ex.InnerException.Message}");
                    Console.WriteLine($"[INNER EXCEPTION STACKTRACE]: {ex.InnerException.StackTrace}");
                }
                Console.WriteLine($"[FULL STACK GetAllQuestions]: {ex.ToString()}");
                // В продакшене лучше не возвращать ex.Message напрямую клиенту
                return StatusCode(500, new { message = "Произошла ошибка на сервере при обработке запроса.", details = ex.Message });
            }
        }

        // Вспомогательный класс для пагинации
        public class PaginatedResult<T>
        {
            public List<T> Items { get; set; }
            public int TotalCount { get; set; }
        }

        // GET /api/Questions/{id} - Получение одного вопроса с ответами
        [HttpGet("{id}")]
        public async Task<ActionResult<QuestionDetailDto>> GetQuestionById(int id) // <<< Возвращаем QuestionDetailDto
        {
            Console.WriteLine($"[DEBUG GetQuestionById] Request for ID: {id}");
            var question = await _context.Questions
                .AsNoTracking()
                .Include(q => q.Author) // Автор вопроса
                .Include(q => q.Tags)   // Теги вопроса
                .Include(q => q.Answers) // <<< Ответы на вопрос
                    .ThenInclude(a => a.Author) // <<< Авторы ответов
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                Console.WriteLine($"[DEBUG GetQuestionById] Question with ID {id} not found.");
                return NotFound(new { message = "Вопрос не найден" });
            }

            // Используем AutoMapper для преобразования в QuestionDetailDto
            var questionDetailDto = _mapper.Map<QuestionDetailDto>(question);

            // Опционально: сортировка ответов (например, по рейтингу или дате)
            questionDetailDto.Answers = questionDetailDto.Answers.OrderByDescending(a => a.Rating).ThenBy(a => a.CreatedAt).ToList();

            return Ok(questionDetailDto);
        }


        // POST /api/Questions - Создание вопроса (остается как было, использует QuestionCreateDto)
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<QuestionDto>> CreateQuestion([FromBody] QuestionCreateDto questionDto)
        {
            // ... (проверка пользователя и DTO)
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var userId))
                return Unauthorized("User ID not found in token.");

            // ... (проверка существования пользователя)

            try
            {
                var questionTags = new List<Tag>();
                if (questionDto.Tags != null && questionDto.Tags.Any())
                {
                    // Используем HashSet для быстрой проверки уникальности добавляемых имен
                    var distinctTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var tagNameFromUser in questionDto.Tags)
                    {
                        if (string.IsNullOrWhiteSpace(tagNameFromUser) || tagNameFromUser.Length > 30) // Добавим проверку длины
                            continue;

                        var normalizedTagName = tagNameFromUser.Trim().ToLower();

                        // Пропускаем, если тег с таким именем уже был добавлен в этой сессии
                        if (!distinctTagNames.Add(normalizedTagName))
                            continue;

                        var existingTag = await _context.Tags
                            .FirstOrDefaultAsync(t => t.Name == normalizedTagName); // Сравнение уже нормализованных имен

                        if (existingTag != null)
                        {
                            questionTags.Add(existingTag);
                        }
                        else
                        {
                            var newTag = new Tag { Name = normalizedTagName }; // Сохраняем в нижнем регистре
                            _context.Tags.Add(newTag); // EF отследит для добавления
                            questionTags.Add(newTag);  // Добавляем новый тег в список для вопроса
                        }
                    }
                }

                if (!questionTags.Any()) // Проверка, что хотя бы один тег есть после обработки
                {
                    // Можно вернуть ошибку, если теги обязательны и все введенные были некорректны
                    // ModelState.AddModelError("Tags", "Необходимо указать хотя бы один корректный тег.");
                    // return BadRequest(ModelState);
                }


                var question = new Question
                {
                    Title = questionDto.Title,
                    Content = questionDto.Content,
                    AuthorId = userId,
                    Tags = questionTags, // Связываем существующие и только что созданные теги
                    CreatedAt = DateTime.UtcNow,
                    Rating = 0
                };

                _context.Questions.Add(question);
                await _context.SaveChangesAsync(); // Здесь новые теги будут сохранены в БД вместе с вопросом

                // Перезагрузка вопроса с полными данными для DTO
                var createdQuestion = await _context.Questions
                    .Include(q => q.Author)
                    .Include(q => q.Tags)
                    .Include(q => q.Answers)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(q => q.Id == question.Id);

                if (createdQuestion == null)
                { // Маловероятно, но для полноты
                    return NotFound("Созданный вопрос не найден после сохранения.");
                }

                var questionDtoResult = _mapper.Map<QuestionDto>(createdQuestion);
                return CreatedAtAction(nameof(GetQuestionById), new { id = question.Id }, questionDtoResult);
            }
            // ... (обработка ошибок) ...
            catch (DbUpdateException dbEx)
            {
                Console.Error.WriteLine($"[ERROR CreateQuestion DB]: {dbEx.InnerException?.Message ?? dbEx.Message}");
                return StatusCode(500, new { message = "Ошибка базы данных при создании вопроса." });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR CreateQuestion Other]: {ex.Message}");
                return StatusCode(500, new { message = "Произошла непредвиденная ошибка." });
            }
        }

        // POST /api/Questions/{questionId}/answers - Добавление ответа к вопросу <<< НОВЫЙ МЕТОД >>>
        [HttpPost("{questionId}/answers")]
        [Authorize] // Требует авторизации
        public async Task<ActionResult<AnswerDto>> PostAnswerToQuestion(int questionId, [FromBody] AnswerCreateDto answerDto)
        {
            Console.WriteLine($"[DEBUG PostAnswer] Request for Question ID: {questionId}");
            // 1. Получить ID пользователя из токена
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var userId))
            {
                Console.WriteLine("[ERROR PostAnswer] Invalid User ID format in token.");
                return Unauthorized("Не удалось определить ID пользователя.");
            }
            Console.WriteLine($"[DEBUG PostAnswer] User ID from token: {userId}");

            // 2. Проверить, существует ли вопрос
            var questionExists = await _context.Questions.AnyAsync(q => q.Id == questionId);
            if (!questionExists)
            {
                Console.WriteLine($"[ERROR PostAnswer] Question with ID {questionId} not found.");
                return NotFound(new { message = "Вопрос, на который вы пытаетесь ответить, не найден." });
            }

            // 3. Проверить существование пользователя (опционально, но полезно)
            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
            {
                Console.WriteLine($"[ERROR PostAnswer] User with ID {userId} from token not found in DB.");
                return NotFound($"Пользователь с ID {userId} не найден.");
            }

            try
            {
                // 4. Создать объект ответа
                var answer = new Answer
                {
                    Content = answerDto.Content,
                    QuestionId = questionId, // Связываем с вопросом
                    AuthorId = userId,      // Связываем с автором
                    CreatedAt = DateTime.UtcNow,
                    Rating = 0,
                    IsAccepted = false
                };

                // 5. Сохранить ответ в БД
                _context.Answers.Add(answer);
                await _context.SaveChangesAsync();
                Console.WriteLine($"[DEBUG PostAnswer] Answer created with ID: {answer.Id} for Question ID: {questionId}");

                // 6. Загрузить автора для маппинга (если мапперу это нужно)
                await _context.Entry(answer).Reference(a => a.Author).LoadAsync();

                // 7. Смапить в DTO для ответа
                var answerDtoResult = _mapper.Map<AnswerDto>(answer);

                // 8. Вернуть 201 Created (или 200 OK) с созданным ответом
                // Если есть GET для ответа: return CreatedAtAction(nameof(GetAnswerById), new { id = answer.Id }, answerDtoResult);
                return Ok(answerDtoResult); // Проще вернуть 200 OK с данными ответа
            }
            catch (DbUpdateException dbEx) { /*...*/ return StatusCode(500, "Ошибка БД при добавлении ответа."); }
            catch (Exception ex) { /*...*/ return StatusCode(500, "Ошибка сервера при добавлении ответа."); }
        }

    } // Конец контроллера

    
} // Конец пространства имен

// --- Добавьте/обновите эти DTO ---

