using AutoMapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackOverStadyApi.Models; // Убедитесь, что пространство имен моделей правильное и содержит UserRole
using System.Text;
using System.Security.Claims; // Нужно для MappingProfile

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// 1. Конфигурация сервисов

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

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
                if (!string.IsNullOrEmpty(context.Token))
                {
                    // Console.WriteLine("[JwtBearer] Token found in 'jwt' cookie."); // Можно убрать для продакшена
                }
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

// --- НАСТРОЙКА АВТОРИЗАЦИИ С ПОЛИТИКАМИ ---
builder.Services.AddAuthorization(options =>
{
    // Политика для действий, доступных модераторам и администраторам
    options.AddPolicy("RequireModeratorRole", policy =>
        policy.RequireAssertion(context => // Используем RequireAssertion для более гибкой проверки
            context.User.IsInRole(UserRole.Moderator.ToString()) ||
            context.User.IsInRole(UserRole.Admin.ToString())));

    // Политика для действий, доступных только администраторам (если понадобится)
    options.AddPolicy("RequireAdminRole", policy =>
        policy.RequireRole(UserRole.Admin.ToString()));

    // Можно добавить и другие политики по необходимости
});
// -------------------------------------------

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMyOrigin", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen(); // Если используешь Swagger, он должен быть здесь

// 2. Конфигурация Pipeline приложения

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    // app.UseSwagger(); // Если используешь Swagger
    // app.UseSwaggerUI(); // Если используешь Swagger
}
else
{
    app.UseExceptionHandler("/Error"); // Настрой кастомную страницу ошибок
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseCors("AllowMyOrigin");

app.UseAuthentication(); // Важно: ДО UseAuthorization
app.UseAuthorization();  // Включает проверку политик и [Authorize] атрибутов

app.MapControllers();

app.Run();