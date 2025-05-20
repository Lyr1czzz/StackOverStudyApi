using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackOverStadyApi.Dto;
using StackOverStadyApi.Models;
using System.Security.Claims; // Убедитесь, что пространство имен правильное

namespace StackOverStadyApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Маршрут будет /api/Users
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IMapper _mapper;

        public UsersController(ApplicationDbContext context, IMapper mapper) { _context = context; _mapper = mapper; }

        // DTO для публичного профиля пользователя
        public class UserProfileDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string PictureUrl { get; set; }
            public int Rating { get; set; }
            // Можно добавить другие публичные поля, например, дату регистрации, количество вопросов/ответов
            // public DateTime RegistrationDate { get; set; } // Пример
        }

        [HttpGet("{userId}/achievements")]
        public async Task<ActionResult<IEnumerable<UserAchievementDto>>> GetUserAchievements(int userId)
        {
            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
            {
                return NotFound(new { message = "Пользователь не найден." });
            }

            var userAchievements = await _context.UserAchievements
                .Where(ua => ua.UserId == userId)
                .Include(ua => ua.Achievement) // Важно для загрузки данных ачивки
                .OrderByDescending(ua => ua.AwardedAt)
                .ProjectTo<UserAchievementDto>(_mapper.ConfigurationProvider) // Используем AutoMapper
                .ToListAsync();

            return Ok(userAchievements);
        }

        // Также можно сделать эндпоинт для /api/me/achievements для текущего пользователя
        [HttpGet("me/achievements")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<UserAchievementDto>>> GetMyAchievements()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var userId))
            {
                return Unauthorized("Не удалось определить пользователя.");
            }
            // Дальше логика как в GetUserAchievements, только с полученным userId
            var userAchievements = await _context.UserAchievements
                .Where(ua => ua.UserId == userId)
                .Include(ua => ua.Achievement)
                .OrderByDescending(ua => ua.AwardedAt)
                .ProjectTo<UserAchievementDto>(_mapper.ConfigurationProvider)
                .ToListAsync();

            return Ok(userAchievements);
        }

        // GET: api/Users/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<UserProfileDto>> GetUserProfile(int id)
        {
            // Ищем пользователя по ID, включая только нужные поля (оптимизация)
            var user = await _context.Users
                .Where(u => u.Id == id)
                .Select(u => new UserProfileDto // Сразу проецируем в DTO
                {
                    Id = u.Id,
                    Name = u.Name,
                    PictureUrl = u.PictureUrl,
                    Rating = u.Rating
                    // RegistrationDate = u.RegistrationDate // Пример
                })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                Console.WriteLine($"[API /api/Users/{id}] User not found.");
                return NotFound(new { message = "Пользователь с таким ID не найден." });
            }

            Console.WriteLine($"[API /api/Users/{id}] Returning profile for user: {user.Name}");
            return Ok(user);
        }
    }
}