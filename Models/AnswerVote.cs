// src/Models/AnswerVote.cs
namespace StackOverStadyApi.Models
{
    public class AnswerVote
    {
        public int UserId { get; set; }
        public virtual User User { get; set; } // Убедитесь, что у вас есть модель User

        public int AnswerId { get; set; }
        public virtual Answer Answer { get; set; }

        public bool IsUpVote { get; set; } // true for upvote, false for downvote
    }
}