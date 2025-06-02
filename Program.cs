using AutoMapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore; // <<< Добавь этот using для Migrate()
using Microsoft.Extensions.DependencyInjection; // <<< Добавь этот using для CreateScope() и GetRequiredService()
using Microsoft.Extensions.Hosting; // <<< Добавь этот using для ILogger
using Microsoft.Extensions.Logging; // <<< Добавь этот using для ILogger
using Microsoft.IdentityModel.Tokens;
using StackOverStadyApi.Models; // Убедись, что пространство имен моделей правильное и содержит UserRole
using System.Text;
using System.Security.Claims;
using StackOverStadyApi.Services; // Нужно для MappingProfile и IAchievementService

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// 1. Конфигурация сервисов

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
        // Опционально: настройка для Npgsql для лучшей работы с миграциями и производительностью
        npgsqlOptionsAction: sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorCodesToAdd: null);
        }
    )
);

builder.Services.AddScoped<IAchievementService, AchievementService>();
builder.Services.AddAutoMapper(typeof(MappingProfile));
builder.Services.AddHttpClient();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "ExternalLoginCookie";
    })
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = configuration["GoogleAuth:ClientId"] ?? throw new InvalidOperationException("Google ClientId не настроен");
        options.ClientSecret = configuration["GoogleAuth:ClientSecret"] ?? throw new InvalidOperationException("Google ClientSecret не настроен");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.SaveTokens = true;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["jwt"];
                return Task.CompletedTask;
            }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer не настроен"),
            ValidAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience не настроен"),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key не настроен"))),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireModeratorRole", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole(UserRole.Moderator.ToString()) ||
            context.User.IsInRole(UserRole.Admin.ToString())));
    options.AddPolicy("RequireAdminRole", policy =>
        policy.RequireRole(UserRole.Admin.ToString()));
});

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMyOrigin", policy => // Используй это имя в app.UseCors()
    {
        // Добавь сюда все origins, с которых разрешен доступ
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new string[0];
        if (allowedOrigins.Any())
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else // Fallback для локальной разработки, если в конфиге не указано
        {
            policy.WithOrigins("https://stack-over-study-front.vercel.app")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();

// 2. Конфигурация Pipeline приложения

var app = builder.Build();

// --- АВТОМАТИЧЕСКОЕ ПРИМЕНЕНИЕ МИГРАЦИЙ ПРИ СТАРТЕ ---
// Этот блок должен быть одним из первых после app = builder.Build();
// Особенно важно, чтобы он был до того, как приложение начнет обрабатывать запросы.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>(); // Получаем логгер
    try
    {
        logger.LogInformation("Attempting to get ApplicationDbContext for migration.");
        var context = services.GetRequiredService<ApplicationDbContext>();

        logger.LogInformation("Applying database migrations...");
        context.Database.Migrate(); // Эта команда создает таблицы и применяет все ожидающие миграции
        logger.LogInformation("Database migrations applied successfully (or no new migrations to apply).");

        // Опционально: Заполнение начальными данными (Seed Data)
        // Тебе нужно будет создать этот метод/сервис, если он нужен.
        // Например, для заполнения таблицы Achievements.
        // await SeedData.InitializeAsync(services); 
        // logger.LogInformation("Seed data initialization complete (if applicable).");

    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating or initializing the database. Application will not start.");
        // Важно: если миграции или сидинг падают, приложение не должно продолжать работу
        // с некорректным состоянием БД. Выбрасываем исключение, чтобы остановить запуск.
        // Это поможет увидеть проблему в логах Render (или другого хостинга) и исправить ее.
        throw; // Перебрасываем исключение, чтобы остановить запуск приложения
    }
}
// --------------------------------------------------

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    // app.UseSwagger();
    // app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error"); // Настроить страницу /Error или middleware
    app.UseHsts();
}

// app.UseHttpsRedirection(); // Закомментируй, если HTTPS терминируется на реверс-прокси (например, на Render)
app.UseRouting();

app.UseCors("AllowMyOrigin"); // Убедись, что имя политики совпадает с тем, что задано выше

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();