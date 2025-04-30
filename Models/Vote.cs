using System.ComponentModel.DataAnnotations;

namespace StackOverStadyApi.Models
{
    public enum VoteType
    {
        Up = 1,
        Down = -1
    }

    public class Vote
    {
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; } // Кто проголосовал
        public User User { get; set; }

        public int? QuestionId { get; set; } // За какой вопрос (может быть null)
        public Question? Question { get; set; }

        public int? AnswerId { get; set; } // За какой ответ (может быть null)
        public Answer? Answer { get; set; }

        [Required]
        public VoteType VoteType { get; set; } // Тип голоса (вверх/вниз)

        public DateTime VotedAt { get; set; } = DateTime.UtcNow;
    }
}