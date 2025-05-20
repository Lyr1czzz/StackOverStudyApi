// src/Services/AchievementService.cs (создай новую папку Services, если ее нет)
using Microsoft.EntityFrameworkCore;
using StackOverStadyApi.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace StackOverStadyApi.Services
{
    public interface IAchievementService
    {
        Task AwardAchievementAsync(int userId, string achievementCode);
        // Можно добавить другие методы, например, для получения ачивок пользователя
    }

    public class AchievementService : IAchievementService
    {
        private readonly ApplicationDbContext _context;

        public AchievementService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AwardAchievementAsync(int userId, string achievementCode)
        {
            // 1. Проверяем, существует ли пользователь
            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
            {
                // Логирование или обработка ошибки: пользователь не найден
                Console.WriteLine($"[AchievementService] User with ID {userId} not found for awarding achievement {achievementCode}.");
                return;
            }

            // 2. Находим ачивку по коду
            var achievement = await _context.Achievements
                .FirstOrDefaultAsync(a => a.Code == achievementCode);

            if (achievement == null)
            {
                // Логирование: ачивка с таким кодом не найдена в БД
                Console.WriteLine($"[AchievementService] Achievement with code {achievementCode} not found.");
                return;
            }

            // 3. Проверяем, не получил ли пользователь эту ачивку ранее
            var alreadyAwarded = await _context.UserAchievements
                .AnyAsync(ua => ua.UserId == userId && ua.AchievementId == achievement.Id);

            if (alreadyAwarded)
            {
                // Пользователь уже имеет эту ачивку
                return;
            }

            // 4. Присваиваем ачивку
            var userAchievement = new UserAchievement
            {
                UserId = userId,
                AchievementId = achievement.Id,
                AwardedAt = DateTime.UtcNow
            };

            _context.UserAchievements.Add(userAchievement);
            await _context.SaveChangesAsync();

            Console.WriteLine($"[AchievementService] User {userId} awarded achievement: {achievement.Name} ({achievementCode})");
            // Здесь можно добавить логику уведомлений пользователя (SignalR, email и т.д.)
        }
    }
}