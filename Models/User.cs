namespace StackOverStadyApi.Models
{
    public enum UserRole
    {
        User = 0,
        Moderator = 1,
        Admin = 2
    }

    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string GoogleId { get; set; }
        public string PictureUrl { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiryTime { get; set; }
        public int Rating { get; set; }

        public UserRole Role { get; set; } = UserRole.User;

        public List<Question> Questions { get; set; } = new();
        public List<Answer> Answers { get; set; } = new();

        public virtual ICollection<UserAchievement> UserAchievements { get; set; } = new List<UserAchievement>();
    }
}