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
using System.Security.Claims;
using StackOverStadyApi.Services;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// 1. ������������ ��������

// ���������� ���� ������������ ������ ��� DbContext, ��� ��� ��������
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
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
        // ��������� ������������ ��� ���
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // ����� ��� HTTPS
        options.Cookie.SameSite = SameSiteMode.Lax; // ��� None, ���� ��� ���������� ��� ������ ������ OAuth
    })
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = configuration["GoogleAuth:ClientId"] ?? throw new InvalidOperationException("GoogleAuth:ClientId �� ��������");
        options.ClientSecret = configuration["GoogleAuth:ClientSecret"] ?? throw new InvalidOperationException("GoogleAuth:ClientSecret �� ��������");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.SaveTokens = true;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        // CallbackPath �� ��������� /signin-google.
        // URI ��������������� � Google Console ������ ����: https://���_�����_�������/signin-google
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
            ValidIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer �� ��������"),
            ValidAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience �� ��������"),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key �� ��������"))),
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

const string CorsPolicyName = "AllowSpecificOrigins";

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        // �������, ��� Vercel URL ����� ��� ����� �� �����
        string[] allowedOrigins = {
            "http://localhost:5173",                // ��� ��������� ���������� ���������
            "https://stack-over-study-front.vercel.app"  // ��� Vercel
            // ������ ���� ��������� �����, ���� ����
        };

        var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (configuredOrigins != null && configuredOrigins.Any())
        {
            Console.WriteLine($"[CORS] Using origins from configuration: {string.Join(", ", configuredOrigins)}");
            allowedOrigins = configuredOrigins;
        }
        else
        {
            Console.WriteLine($"[CORS] Using hardcoded fallback origins: {string.Join(", ", allowedOrigins)}");
        }

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// �������������� ���������� ��������
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Attempting to get ApplicationDbContext for migration.");
        var context = services.GetRequiredService<ApplicationDbContext>();
        logger.LogInformation("Applying database migrations...");
        context.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully (or no new migrations to apply).");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "An error occurred while migrating or initializing the database. Application might not start correctly or at all.");
        // � ���������� ��� ����� ���� �������� ��������� ����������, ���� �� �������� ��� ������
        // throw; // ��������������, ���� ������, ����� ���������� ������ ��� ������ ��������
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// HTTPS Redirection: ���� Render ����������� HTTPS, ��� ����� ���� �� �����.
// app.UseHttpsRedirection(); 
app.UseRouting();

app.UseCors(CorsPolicyName);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();