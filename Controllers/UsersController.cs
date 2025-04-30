using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackOverStadyApi.Models; // Убедитесь, что пространство имен правильное

namespace StackOverStadyApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Маршрут будет /api/Users
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public UsersController(ApplicationDbContext context)
        {
            _context = context;
        }

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