using AutoMapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore; // <<< ������ ���� using ��� Migrate()
using Microsoft.Extensions.DependencyInjection; // <<< ������ ���� using ��� CreateScope() � GetRequiredService()
using Microsoft.Extensions.Hosting; // <<< ������ ���� using ��� ILogger
using Microsoft.Extensions.Logging; // <<< ������ ���� using ��� ILogger
using Microsoft.IdentityModel.Tokens;
using StackOverStadyApi.Models; // �������, ��� ������������ ���� ������� ���������� � �������� UserRole
using System.Text;
using System.Security.Claims;
using StackOverStadyApi.Services; // ����� ��� MappingProfile � IAchievementService

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// 1. ������������ ��������

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
        // �����������: ��������� ��� Npgsql ��� ������ ������ � ���������� � �������������������
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
        options.ClientId = configuration["GoogleAuth:ClientId"] ?? throw new InvalidOperationException("Google ClientId �� ��������");
        options.ClientSecret = configuration["GoogleAuth:ClientSecret"] ?? throw new InvalidOperationException("Google ClientSecret �� ��������");
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMyOrigin", policy => // ��������� ��� ��� � app.UseCors()
    {
        // ������ ���� ��� origins, � ������� �������� ������
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new string[0];
        if (allowedOrigins.Any())
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else // Fallback ��� ��������� ����������, ���� � ������� �� �������
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

// 2. ������������ Pipeline ����������

var app = builder.Build();

// --- �������������� ���������� �������� ��� ������ ---
// ���� ���� ������ ���� ����� �� ������ ����� app = builder.Build();
// �������� �����, ����� �� ��� �� ����, ��� ���������� ������ ������������ �������.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>(); // �������� ������
    try
    {
        logger.LogInformation("Attempting to get ApplicationDbContext for migration.");
        var context = services.GetRequiredService<ApplicationDbContext>();

        logger.LogInformation("Applying database migrations...");
        context.Database.Migrate(); // ��� ������� ������� ������� � ��������� ��� ��������� ��������
        logger.LogInformation("Database migrations applied successfully (or no new migrations to apply).");

        // �����������: ���������� ���������� ������� (Seed Data)
        // ���� ����� ����� ������� ���� �����/������, ���� �� �����.
        // ��������, ��� ���������� ������� Achievements.
        // await SeedData.InitializeAsync(services); 
        // logger.LogInformation("Seed data initialization complete (if applicable).");

    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating or initializing the database. Application will not start.");
        // �����: ���� �������� ��� ������ ������, ���������� �� ������ ���������� ������
        // � ������������ ���������� ��. ����������� ����������, ����� ���������� ������.
        // ��� ������� ������� �������� � ����� Render (��� ������� ��������) � ��������� ��.
        throw; // ������������� ����������, ����� ���������� ������ ����������
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
    app.UseExceptionHandler("/Error"); // ��������� �������� /Error ��� middleware
    app.UseHsts();
}

// app.UseHttpsRedirection(); // �������������, ���� HTTPS ������������� �� ������-������ (��������, �� Render)
app.UseRouting();

app.UseCors("AllowMyOrigin"); // �������, ��� ��� �������� ��������� � ���, ��� ������ ����

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();