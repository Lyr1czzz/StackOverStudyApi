namespace StackOverStadyApi.Models
{
    public class Tag
    {
        public int Id { get; set; }
        public string Name { get; set; } // Имя тега должно быть уникальным

        // Связь многие-ко-многим с Вопросами
        public List<Question> Questions { get; set; } = new();
    }
}
