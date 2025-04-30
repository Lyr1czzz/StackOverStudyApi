using Microsoft.EntityFrameworkCore;

namespace StackOverStadyApi.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Comment> Comments { get; set; }
        public DbSet<Vote> Votes { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<Answer> Answers { get; set; }
        public DbSet<Tag> Tags { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Настройка уникальности GoogleId
            modelBuilder.Entity<User>()
                .HasIndex(u => u.GoogleId)
                .IsUnique();

            // Настройка уникальности Email (опционально)
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // Настройка уникальности имен тегов (если не используется атрибут Index)
            modelBuilder.Entity<Tag>()
                .HasIndex(t => t.Name)
                .IsUnique();

            // Настройка связи Question -> Author
            modelBuilder.Entity<Question>()
                .HasOne(q => q.Author)
                .WithMany(u => u.Questions)
                .HasForeignKey(q => q.AuthorId)
                .OnDelete(DeleteBehavior.Restrict); // Не удалять юзера, если есть вопросы

            // Настройка связи Answer -> Author
            modelBuilder.Entity<Answer>()
               .HasOne(a => a.Author)
               .WithMany(u => u.Answers)
               .HasForeignKey(a => a.AuthorId)
               .OnDelete(DeleteBehavior.Restrict); // Не удалять юзера, если есть ответы

            // Настройка связи Answer -> Question
            modelBuilder.Entity<Answer>()
               .HasOne(a => a.Question)
               .WithMany(q => q.Answers)
               .HasForeignKey(a => a.QuestionId)
               .OnDelete(DeleteBehavior.Cascade); // Удалять ответы при удалении вопроса

            // Настройка связи Многие-ко-многим Question <-> Tag (EF Core 5+)
            modelBuilder.Entity<Question>()
                .HasMany(q => q.Tags)
                .WithMany(t => t.Questions)
                .UsingEntity(j => j.ToTable("QuestionTags")); // Явно указываем имя связующей таблицы



            modelBuilder.Entity<Vote>(entity =>
            {
                // Уникальный индекс: один пользователь - один голос за один пост (вопрос ИЛИ ответ)
                entity.HasIndex(v => new { v.UserId, v.QuestionId }).IsUnique();
                entity.HasIndex(v => new { v.UserId, v.AnswerId }).IsUnique();

                // Связи
                entity.HasOne(v => v.User)
                      .WithMany() // У пользователя много голосов (не будем добавлять List<Vote> в User)
                      .HasForeignKey(v => v.UserId)
                      .OnDelete(DeleteBehavior.Cascade); // Удалять голоса при удалении юзера

                entity.HasOne(v => v.Question)
                      .WithMany() // У вопроса много голосов (не будем добавлять List<Vote> в Question)
                      .HasForeignKey(v => v.QuestionId)
                      .OnDelete(DeleteBehavior.Cascade); // Удалять голоса при удалении вопроса

                entity.HasOne(v => v.Answer)
                      .WithMany() // У ответа много голосов (не будем добавлять List<Vote> в Answer)
                      .HasForeignKey(v => v.AnswerId)
                      .OnDelete(DeleteBehavior.Cascade); // Удалять голоса при удалении ответа

                // Ограничение: Либо QuestionId, либо AnswerId должны быть заданы, но не оба
                entity.HasCheckConstraint("CK_Vote_Target",
             @"(""QuestionId"" IS NOT NULL AND ""AnswerId"" IS NULL) OR (""QuestionId"" IS NULL AND ""AnswerId"" IS NOT NULL)");
            });

            modelBuilder.Entity<Comment>(entity =>
            {
                // Связи
                entity.HasOne(c => c.User)
                      .WithMany() // У пользователя много комментариев
                      .HasForeignKey(c => c.UserId)
                      .OnDelete(DeleteBehavior.Cascade); // Удалять комменты при удалении юзера

                entity.HasOne(c => c.Question)
                      .WithMany() // У вопроса много комментов (не добавляем List<Comment> в Question)
                      .HasForeignKey(c => c.QuestionId)
                      .OnDelete(DeleteBehavior.Cascade); // Удалять комменты при удалении вопроса

                entity.HasOne(c => c.Answer)
                      .WithMany() // У ответа много комментов
                      .HasForeignKey(c => c.AnswerId)
                      .OnDelete(DeleteBehavior.Cascade); // Удалять комменты при удалении ответа

                // Ограничение: Либо QuestionId, либо AnswerId
                entity.HasCheckConstraint("CK_Comment_Target",
               @"(""QuestionId"" IS NOT NULL AND ""AnswerId"" IS NULL) OR (""QuestionId"" IS NULL AND ""AnswerId"" IS NOT NULL)");
            });
        }
    }
}