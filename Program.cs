using AutoMapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer; // <<< Добавлено
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;              // <<< Добавлено
using StackOverStadyApi.Models; // Убедитесь, что пространство имен моделей правильное
using System.Text;                                   // <<< Добавлено
using System.Security.Claims; // Нужно для MappingProfile

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration; // Получаем конфигурацию для удобства

// 1. Конфигурация сервисов

// Регистрация DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"))); // <<< Проверьте имя строки подключения

// Регистрация AutoMapper
// Убедитесь, что файл MappingProfile существует и настроен
builder.Services.AddAutoMapper(typeof(MappingProfile));

// Регистрация HttpClientFactory (уже было, но на всякий случай)
builder.Services.AddHttpClient();

// Настройка Аутентификации
builder.Services.AddAuthentication(options =>
{
    // Схемы по умолчанию для API ([Authorize])
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    // Схема для процесса входа через внешних провайдеров (Google)
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
    // Схема для временного хранения информации от Google
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        // Можно настроить параметры куки, если нужно, но обычно для этой схемы не требуется
        options.Cookie.Name = "ExternalLoginCookie"; // Дадим понятное имя
    })
    // Схема Google
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = configuration["GoogleAuth:ClientId"] ?? throw new InvalidOperationException("Google ClientId не настроен");
        options.ClientSecret = configuration["GoogleAuth:ClientSecret"] ?? throw new InvalidOperationException("Google ClientSecret не настроен");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.SaveTokens = true; // Важно для получения токенов в GoogleResponse
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme; // Используем куку для Sign In
    })
    // Схема JWT для защиты API
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        // Ищем токен в куке "jwt"
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["jwt"];
                if (!string.IsNullOrEmpty(context.Token))
                {
                    Console.WriteLine("[JwtBearer] Token found in 'jwt' cookie.");
                }
                // Опционально: можно добавить поиск в заголовке Authorization
                return Task.CompletedTask;
            }
            /* // Для отладки ошибок токена:
            ,OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"[JwtBearer] Authentication failed: {context.Exception.Message}");
                return Task.CompletedTask;
            }
            ,OnChallenge = context =>
            {
                 Console.WriteLine($"[JwtBearer] OnChallenge: {context.Error} - {context.ErrorDescription}");
                 return Task.CompletedTask;
            }
            */
        };

        // Параметры валидации (должны совпадать с генерацией в AuthController)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true, // Проверять срок жизни
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer не настроен"),
            ValidAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience не настроен"),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key не настроен"))),
            ClockSkew = TimeSpan.FromMinutes(1) // Небольшой допуск времени
        };
    });

// Настройка Авторизации (если нужны политики)
// builder.Services.AddAuthorization();

// Добавляем контроллеры
builder.Services.AddControllers();

// Настройка CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMyOrigin", policy =>
    {
        policy.WithOrigins("http://localhost:5173") // <<< Адрес вашего фронтенда
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // <<< Обязательно для кук!
    });
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();


// 2. Конфигурация Pipeline приложения

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage(); // Показывать детали ошибок в разработке
}
else
{
    // В продакшене можно настроить обработку ошибок по-другому
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection(); // Перенаправлять HTTP на HTTPS

app.UseRouting(); // Включаем маршрутизацию

// CORS должен быть ДО UseAuthentication / UseAuthorization
app.UseCors("AllowMyOrigin"); // Применяем политику CORS

app.UseAuthentication(); // Включаем аутентификацию (проверяет куки/токены)
app.UseAuthorization(); // Включаем авторизацию (проверяет [Authorize] атрибуты)

app.MapControllers(); // Сопоставляем маршруты с контроллерами

app.Run();