// src/Models/Achievement.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StackOverStadyApi.Models
{
    public class Achievement
    {
        public int Id { get; set; } // PK

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } // Название ачивки, например, "Первопроходец"

        [Required]
        [MaxLength(255)]
        public string Description { get; set; } // Описание, например, "Зарегистрировался на платформе"

        [Required]
        [MaxLength(50)]
        public string IconName { get; set; } // Имя файла иконки или CSS класс для иконки

        // Уникальный код ачивки для легкой идентификации в коде
        // (например, "REGISTRATION", "FIRST_QUESTION")
        [Required]
        [MaxLength(50)]
        public string Code { get; set; }

        public virtual ICollection<UserAchievement> UserAchievements { get; set; } = new List<UserAchievement>();
    }
}