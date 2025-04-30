using AutoMapper;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer; // <<< ���������
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;              // <<< ���������
using StackOverStadyApi.Models; // ���������, ��� ������������ ���� ������� ����������
using System.Text;                                   // <<< ���������
using System.Security.Claims; // ����� ��� MappingProfile

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration; // �������� ������������ ��� ��������

// 1. ������������ ��������

// ����������� DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"))); // <<< ��������� ��� ������ �����������

// ����������� AutoMapper
// ���������, ��� ���� MappingProfile ���������� � ��������
builder.Services.AddAutoMapper(typeof(MappingProfile));

// ����������� HttpClientFactory (��� ����, �� �� ������ ������)
builder.Services.AddHttpClient();

// ��������� ��������������
builder.Services.AddAuthentication(options =>
{
    // ����� �� ��������� ��� API ([Authorize])
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    // ����� ��� �������� ����� ����� ������� ����������� (Google)
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
    // ����� ��� ���������� �������� ���������� �� Google
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        // ����� ��������� ��������� ����, ���� �����, �� ������ ��� ���� ����� �� ���������
        options.Cookie.Name = "ExternalLoginCookie"; // ����� �������� ���
    })
    // ����� Google
    .AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = configuration["GoogleAuth:ClientId"] ?? throw new InvalidOperationException("Google ClientId �� ��������");
        options.ClientSecret = configuration["GoogleAuth:ClientSecret"] ?? throw new InvalidOperationException("Google ClientSecret �� ��������");
        options.Scope.Add("email");
        options.Scope.Add("profile");
        options.SaveTokens = true; // ����� ��� ��������� ������� � GoogleResponse
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme; // ���������� ���� ��� Sign In
    })
    // ����� JWT ��� ������ API
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        // ���� ����� � ���� "jwt"
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["jwt"];
                if (!string.IsNullOrEmpty(context.Token))
                {
                    Console.WriteLine("[JwtBearer] Token found in 'jwt' cookie.");
                }
                // �����������: ����� �������� ����� � ��������� Authorization
                return Task.CompletedTask;
            }
            /* // ��� ������� ������ ������:
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

        // ��������� ��������� (������ ��������� � ���������� � AuthController)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true, // ��������� ���� �����
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer �� ��������"),
            ValidAudience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience �� ��������"),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key �� ��������"))),
            ClockSkew = TimeSpan.FromMinutes(1) // ��������� ������ �������
        };
    });

// ��������� ����������� (���� ����� ��������)
// builder.Services.AddAuthorization();

// ��������� �����������
builder.Services.AddControllers();

// ��������� CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMyOrigin", policy =>
    {
        policy.WithOrigins("http://localhost:5173") // <<< ����� ������ ���������
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // <<< ����������� ��� ���!
    });
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();


// 2. ������������ Pipeline ����������

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage(); // ���������� ������ ������ � ����������
}
else
{
    // � ���������� ����� ��������� ��������� ������ ��-�������
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection(); // �������������� HTTP �� HTTPS

app.UseRouting(); // �������� �������������

// CORS ������ ���� �� UseAuthentication / UseAuthorization
app.UseCors("AllowMyOrigin"); // ��������� �������� CORS

app.UseAuthentication(); // �������� �������������� (��������� ����/������)
app.UseAuthorization(); // �������� ����������� (��������� [Authorize] ��������)

app.MapControllers(); // ������������ �������� � �������������

app.Run();