using System.ComponentModel.DataAnnotations;

namespace StackOverStadyApi.Models
{
    public class Comment
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(500)] // Ограничим длину комментария
        public string Text { get; set; }

        [Required]
        public int UserId { get; set; } // Автор комментария
        public User User { get; set; }

        public int? QuestionId { get; set; } // К какому вопросу (может быть null)
        public Question? Question { get; set; }

        public int? AnswerId { get; set; } // К какому ответу (может быть null)
        public Answer? Answer { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}