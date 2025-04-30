namespace StackOverStadyApi.Models
{
    public class Answer
    {
        public int Id { get; set; }
        public string Content { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsAccepted { get; set; }
        public int Rating { get; set; }

        public int AuthorId { get; set; }
        public User Author { get; set; }

        public int QuestionId { get; set; }
        public Question Question { get; set; }
    }
}
