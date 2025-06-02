using AutoMapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StackOverStadyApi.Models;
using System.Text;
using StackOverStadyApi.Services;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// 1. Конфигурация сервисов
string connectionString;
if (builder.Environment.IsProduction())
{
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (string.IsNullOrEmpty(databaseUrl))
    {
        throw new InvalidOperationException("DATABASE_URL environment variable is not set for Production environment.");
    }
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':');
    connectionString = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.Trim('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true;";
    Console.WriteLine($"[DB Connection] Using DATABASE_URL for Production. Host: {uri.Host}");
}
else
{
    connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("DefaultConnection not found in appsettings.json for Development environment.");
    Console.WriteLine($"[DB Connection] Using DefaultConnection from appsettings.json for Development.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptionsAction: sqlOptions =>
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
        // Для продакшена с HTTPS важно настроить куки безопасности
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Отправлять только по HTTPS
        options.Cookie.SameSite = SameSiteMode.Lax; // Или None, если есть сложные сценарии с iframe/редиректами
    })
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = configuration["GoogleAuth:ClientId"] ?? throw new InvalidOperationException("GoogleAuth:ClientId не настроен");
        options.ClientSecret = configuration["GoogleAuth:ClientSecret"] ?? throw new InvalidOperationException("GoogleAuth:ClientSecret не настроен");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.SaveTokens = true;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        // CallbackPath по умолчанию /signin-google. Убедись, что этот URI (с твоим доменом) добавлен в Google Cloud Console.
        // https://your-api-domain.com/signin-google
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

const string CorsPolicyName = "AllowSpecificOrigins"; // Вынесли имя политики в константу

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

        if (allowedOrigins != null && allowedOrigins.Any())
        {
            Console.WriteLine($"[CORS] Allowed origins from configuration: {string.Join(", ", allowedOrigins)}");
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else if (builder.Environment.IsDevelopment()) // Fallback для локальной разработки, если в конфиге не указано
        {
            Console.WriteLine("[CORS] No AllowedOrigins in config, using fallback for Development: http://localhost:5173");
            policy.WithOrigins("http://localhost:5173") // Убедись, что это твой локальный порт фронтенда
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            Console.WriteLine("[CORS] No AllowedOrigins configured for Production. CORS might not work as expected.");
            // В продакшене лучше, чтобы origins были явно заданы.
            // Можно разрешить все для отладки, но это НЕБЕЗОПАСНО:
            // policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});

builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen();

// 2. Конфигурация Pipeline приложения
var app = builder.Build();

// Автоматическое применение миграций и сидинг данных (если нужно)
try
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();
        var dbContext = services.GetRequiredService<ApplicationDbContext>();

        logger.LogInformation("Applying database migrations...");
        dbContext.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully (or no new migrations to apply).");

        // Пример вызова сидинга (раскомментируй и реализуй SeedData, если нужно)
        // await SeedData.InitializeAsync(services);
        // logger.LogInformation("Seed data initialization complete (if applicable).");
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "An error occurred while migrating or initializing the database. Application will shut down.");
    // При критической ошибке с БД на старте лучше остановить приложение
    // Environment.Exit(1); // Или просто позволить throw распространиться, если это не ловится выше
    throw; // Перебрасываем, чтобы Render и другие хостинги зафиксировали сбой запуска
}


if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    // app.UseSwagger();
    // app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// HTTPS Redirection:
// На Render HTTPS обычно терминируется на их балансировщике нагрузки.
// Если ты уверен, что перед твоим приложением всегда будет HTTPS-терминация,
// UseHttpsRedirection может быть не нужен или даже вызывать проблемы с редиректами.
// Однако, если ты не уверен или хочешь дополнительный слой, оставь его.
// Для Render может быть полезно настроить Forwarded Headers, если они еще не настроены по умолчанию.
/*
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
*/
app.UseHttpsRedirection();

app.UseRouting();

app.UseCors(CorsPolicyName); // Используем имя политики

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();