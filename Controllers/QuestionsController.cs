// QuestionsController.cs
// ... (using, конструктор, DTO определения выше) ...

using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        // DTO для отображения тега (уже есть)
        public class TagDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
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
            // Можно добавить краткое содержание Content или убрать его из списка
            // public string ContentSnippet { get; set; }
            public DateTime CreatedAt { get; set; }
            public UserInfoDto Author { get; set; }
            public List<TagDto> Tags { get; set; } = new();
            public int AnswerCount { get; set; } // <<< Количество ответов
            public int Rating { get; set; } // <<< Рейтинг вопроса (если есть)
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
            public object Tags { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
        }

        // GET /api/Questions - Получение списка вопросов
        [HttpGet]
        public async Task<ActionResult<IEnumerable<QuestionDto>>> GetAllQuestions()
        {
            try
            {
                Console.WriteLine("[DEBUG GetAllQuestions] Request received.");
                var questions = await _context.Questions
                    .AsNoTracking()
                    .Include(q => q.Author) // Включаем автора
                    .Include(q => q.Tags)   // Включаем теги
                    .Include(q => q.Answers) // <<< Включаем ответы, чтобы посчитать их количество
                    .OrderByDescending(q => q.CreatedAt)
                    .ToListAsync();

                // Используем AutoMapper для преобразования в QuestionDto (который теперь включает AnswerCount)
                var questionDtos = _mapper.Map<List<QuestionDto>>(questions);
                Console.WriteLine($"[DEBUG GetAllQuestions] Returning {questionDtos.Count} questions.");
                return Ok(questionDtos);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR GetAllQuestions]: {ex.Message}");
                return StatusCode(500, "Ошибка сервера при получении вопросов.");
            }
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
            // ... (код остается прежним, получает userId из токена, создает Question) ...
            Console.WriteLine("[DEBUG CreateQuestion] Request received.");
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var userId)) { return Unauthorized("..."); }
            Console.WriteLine($"[DEBUG CreateQuestion] User ID from token: {userId}");

            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
            if (!userExists) { return NotFound($"..."); }

            try
            {
                // ... (обработка тегов) ...
                var questionTags = new List<Tag>(); // Логика обработки тегов
                


                var question = new Question
                {
                    Title = questionDto.Title,
                    Content = questionDto.Content,
                    AuthorId = userId,
                    Tags = questionTags,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Questions.Add(question);
                await _context.SaveChangesAsync();
                Console.WriteLine($"[DEBUG CreateQuestion] Question created with ID: {question.Id}");

                // Загружаем автора и теги для корректного маппинга в QuestionDto
                await _context.Entry(question).Reference(q => q.Author).LoadAsync();
                await _context.Entry(question).Collection(q => q.Tags).LoadAsync();

                // Возвращаем QuestionDto (не QuestionDetailDto)
                var questionDtoResult = _mapper.Map<QuestionDto>(question);

                return CreatedAtAction(nameof(GetQuestionById), new { id = question.Id }, questionDtoResult);
            }
            catch (DbUpdateException dbEx) { /*...*/ return StatusCode(500, "..."); }
            catch (Exception ex) { /*...*/ return StatusCode(500, "..."); }
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

