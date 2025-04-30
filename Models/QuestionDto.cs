namespace StackOverStadyApi.Models
{
    public class QuestionDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsSolved { get; set; }

        public UserDto Author { get; set; }
        public List<TagDto> Tags { get; set; } = new();
    }
}
