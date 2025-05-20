// src/Models/UserAchievement.cs
using System;
using System.ComponentModel.DataAnnotations;

namespace StackOverStadyApi.Models
{
    public class UserAchievement
    {
        public int Id { get; set; } // PK

        public int UserId { get; set; }
        public virtual User User { get; set; }

        public int AchievementId { get; set; }
        public virtual Achievement Achievement { get; set; }

        public DateTime AwardedAt { get; set; } = DateTime.UtcNow; // Когда получена ачивка
    }
}