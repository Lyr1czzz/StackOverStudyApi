using AutoMapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackOverStadyApi.Models; // ���������, ��� ������������ ���� ������� ���������� � �������� UserRole
using System.Text;
using System.Security.Claims; // ����� ��� MappingProfile

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// 1. ������������ ��������

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
                if (!string.IsNullOrEmpty(context.Token))
                {
                    // Console.WriteLine("[JwtBearer] Token found in 'jwt' cookie."); // ����� ������ ��� ����������
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
            ValidIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer �� ��������"),
            ValidAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience �� ��������"),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key �� ��������"))),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// --- ��������� ����������� � ���������� ---
builder.Services.AddAuthorization(options =>
{
    // �������� ��� ��������, ��������� ����������� � ���������������
    options.AddPolicy("RequireModeratorRole", policy =>
        policy.RequireAssertion(context => // ���������� RequireAssertion ��� ����� ������ ��������
            context.User.IsInRole(UserRole.Moderator.ToString()) ||
            context.User.IsInRole(UserRole.Admin.ToString())));

    // �������� ��� ��������, ��������� ������ ��������������� (���� �����������)
    options.AddPolicy("RequireAdminRole", policy =>
        policy.RequireRole(UserRole.Admin.ToString()));

    // ����� �������� � ������ �������� �� �������������
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
// builder.Services.AddSwaggerGen(); // ���� ����������� Swagger, �� ������ ���� �����

// 2. ������������ Pipeline ����������

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    // app.UseSwagger(); // ���� ����������� Swagger
    // app.UseSwaggerUI(); // ���� ����������� Swagger
}
else
{
    app.UseExceptionHandler("/Error"); // ������� ��������� �������� ������
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseCors("AllowMyOrigin");

app.UseAuthentication(); // �����: �� UseAuthorization
app.UseAuthorization();  // �������� �������� ������� � [Authorize] ���������

app.MapControllers();

app.Run();