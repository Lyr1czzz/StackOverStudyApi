using AutoMapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides; // Для ForwardedHeaders
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging; // Для ILogger
using Microsoft.IdentityModel.Tokens;
using StackOverStadyApi.Models; // Для ApplicationDbContext, UserRole, MappingProfile
using StackOverStadyApi.Services; // Для IAchievementService, AchievementService
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks; // Для Task.CompletedTask

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration; // Получаем конфигурацию для удобства

// 1. Конфигурация сервисов (Services Configuration)

// Регистрация DbContext с политикой повторных попыток
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
        npgsqlOptionsAction: sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorCodesToAdd: null); // null означает стандартный список ошибок PostgreSQL для повтора
        }
    )
);

// Регистрация сервисов приложения
builder.Services.AddScoped<IAchievementService, AchievementService>();

// Регистрация AutoMapper
builder.Services.AddAutoMapper(typeof(MappingProfile)); // Предполагается, что MappingProfile в том же assembly

// Регистрация HttpClientFactory (полезно для управляемых HTTP-клиентов, если будешь использовать)
builder.Services.AddHttpClient();

// Настройка Аутентификации
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme; // Для внешних провайдеров
})
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "ExternalLoginCookie";
        // Для работы с куками через HTTPS и межсайтовыми запросами (если OAuth редиректы идут на другой сайт)
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    })
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = configuration["GoogleAuth:ClientId"] ?? throw new InvalidOperationException("Google ClientId не настроен в конфигурации.");
        options.ClientSecret = configuration["GoogleAuth:ClientSecret"] ?? throw new InvalidOperationException("Google ClientSecret не настроен в конфигурации.");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.SaveTokens = true; // Важно для доступа к токенам Google после аутентификации
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme; // Указываем схему для временной куки
        // options.CallbackPath = "/signin-google"; // Оставь, если ты его явно настраивал в Google Cloud Console
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["jwt"]; // Читаем JWT из куки "jwt"
                return Task.CompletedTask;
            }
            // Можно добавить OnAuthenticationFailed для отладки, если есть проблемы с валидацией токена
            // , OnAuthenticationFailed = context => { Console.WriteLine($"JWT Auth Failed: {context.Exception.Message}"); return Task.CompletedTask; }
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer не настроен в конфигурации."),
            ValidAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience не настроен в конфигурации."),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key (длинный секретный ключ) не настроен в конфигурации."))),
            ClockSkew = TimeSpan.FromMinutes(1) // Допустимое расхождение времени
        };
    });

// Настройка Авторизации с политиками
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireModeratorRole", policy =>
        policy.RequireAssertion(context =>
            context.User.IsInRole(UserRole.Moderator.ToString()) ||
            context.User.IsInRole(UserRole.Admin.ToString())));

    options.AddPolicy("RequireAdminRole", policy =>
        policy.RequireRole(UserRole.Admin.ToString()));
});

// Добавляем контроллеры
builder.Services.AddControllers();

// Настройка CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy => // Используй одно и то же имя политики везде
    {
        // Укажи ТОЧНЫЕ URL твоего фронтенда. Без слеша в конце.
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
                             new[] {
                                 "http://localhost:5173", // Для локальной разработки Vite
                                 "https://stack-over-study-front.vercel.app" // Твой Vercel URL
                                 // Добавь другие, если нужно (например, кастомный домен)
                             };

        Console.WriteLine($"[CORS] Allowed Origins: {string.Join(", ", allowedOrigins)}");

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Обязательно для передачи кук (включая JWT в куке)
    });
});

// Настройка для работы за реверс-прокси (как Render)
// Это поможет приложению правильно определять схему (http/https) и IP клиента
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Важно: НЕ очищай KnownNetworks и KnownProxies, если ты не полностью контролируешь всю цепочку прокси.
    // Render и другие PaaS обычно настраивают это корректно.
    // Если после этого все еще проблемы с редиректами HTTPS, тогда можно рассмотреть очистку.
});

builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen(); // Раскомментируй, если используешь Swagger

// 2. Конфигурация Pipeline приложения (HTTP request pipeline)

var app = builder.Build();

// Применение миграций и начальное заполнение данными (если нужно)
// Делать это здесь удобно для Docker-окружений или при первом запуске.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Attempting to get ApplicationDbContext for migration.");
        var dbContext = services.GetRequiredService<ApplicationDbContext>();

        logger.LogInformation("Applying database migrations...");
        // Проверяем, есть ли ожидающие миграции перед их применением
        if (dbContext.Database.GetPendingMigrations().Any())
        {
            dbContext.Database.Migrate();
            logger.LogInformation("Database migrations applied successfully.");
        }
        else
        {
            logger.LogInformation("No pending migrations to apply.");
        }

        // Здесь можно добавить вызов сервиса для Seed Data, если он у тебя есть
        // Например: SeedData.Initialize(services);
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "An error occurred while migrating or initializing the database. Application will terminate.");
        // Важно: если БД не готова, приложение может не работать корректно.
        // Можно либо остановить приложение, либо продолжить с логированием критической ошибки.
        // Для простоты, пока оставляем throw, чтобы увидеть проблему при запуске.
        throw;
    }
}

// Конфигурация HTTP пайплайна
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage(); // Более детальные ошибки для разработки
    // app.UseSwagger();
    // app.UseSwaggerUI();
}
else
{
    // Для продакшена:
    app.UseExceptionHandler("/Error"); // Нужна реализация /Error эндпоинта или middleware
    app.UseHsts(); // Добавляет заголовок Strict-Transport-Security
}

// Используем ForwardedHeaders для корректной работы за реверс-прокси
// Это должно быть одним из первых middleware, особенно перед UseHttpsRedirection, если он используется.
app.UseForwardedHeaders();

// HTTPS Redirection: Render обычно предоставляет HTTPS, поэтому это может быть не нужно
// или даже вызывать проблемы с циклами редиректа, если прокси уже терминирует SSL.
// Если HTTPS терминируется на уровне Render, эту строку лучше закомментировать.
// Если ты уверен, что твое приложение само должно обрабатывать редирект на HTTPS, раскомментируй.
// app.UseHttpsRedirection(); 

app.UseRouting(); // Включаем маршрутизацию

// CORS должен быть вызван ПОСЛЕ UseRouting и ПЕРЕД UseAuthentication/UseAuthorization и MapControllers.
app.UseCors("AllowSpecificOrigins");

app.UseAuthentication(); // Включаем аутентификацию (проверяет куки/токены)
app.UseAuthorization();  // Включаем авторизацию (проверяет политики и [Authorize] атрибуты)

app.MapControllers();    // Сопоставляем маршруты с атрибутами в контроллерах

// Простой эндпоинт для проверки, что API работает
app.MapGet("/", () => TypedResults.Ok("StackOverStudy API is running!"));

app.Run();