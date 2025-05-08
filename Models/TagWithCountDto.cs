// In a DTOs folder or near TagDto
namespace StackOverStadyApi.Dto // Or your preferred DTO namespace
{
    public class TagWithCountDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int QuestionCount { get; set; }
    }
}