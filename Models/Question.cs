namespace StackOverStadyApi.Models
{
    public class Question
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int Rating { get; set; } // Рейтинг самого вопроса

        // Внешний ключ для автора
        public int AuthorId { get; set; }
        public User Author { get; set; } // Навигационное свойство к автору

        // Связь многие-ко-многим с Тегами
        public List<Tag> Tags { get; set; } = new();
        public List<Answer> Answers { get; set; } = new(); // Ответы на этот вопрос
    }
}
