using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using StackOverStadyApi.Models; // Проверьте пространство имен
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using StackOverStadyApi.Services; // <<< Добавлено

namespace StackOverStadyApi.Controllers
{
    [ApiController]
    [Route("[controller]")] // Маршрут /Auth
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly IAchievementService _achievementService;

        public AuthController(ApplicationDbContext context, IConfiguration configuration, IHttpClientFactory httpClientFactory, IAchievementService achievementService)
        {
            _context = context;
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient();
            _achievementService = achievementService;
        }

        // Инициирует процесс входа через Google
        [HttpGet("google-login")]
        public IActionResult GoogleLogin()
        {
            // Указываем, куда Google должен вернуть пользователя ПОСЛЕ своей аутентификации
            // Это эндпоинт нашего бэкенда, который обработает ответ Google
            var properties = new AuthenticationProperties { RedirectUri = Url.Action(nameof(GoogleResponse)) };

            // Используем схему Google для Challenge
            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        // Обрабатывает ответ от Google
        [HttpGet("google-response")]
        public async Task<IActionResult> GoogleResponse()
        {
            // Получаем результат аутентификации от схемы CookieAuthenticationDefaults.AuthenticationScheme,
            // которую использовал GoogleSignIn для сохранения временной информации
            var authenticateResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                Console.WriteLine("[ERROR GoogleResponse] External authentication failed.");
                return BadRequest("Ошибка внешней аутентификации.");
            }

            // Извлекаем нужные клеймы (Google ID, Email, Name)
            var claims = authenticateResult.Principal.Claims.ToList();
            var googleId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value; // Это ID от Google
            var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            var name = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
            // Пытаемся получить картинку из клеймов (может не быть стандартного)
            // Google обычно кладет ее в "urn:google:picture" или просто "picture"
            var pictureUrl = claims.FirstOrDefault(c => c.Type == "urn:google:picture" || c.Type == "picture")?.Value;

            // --- Или получаем access_token и запрашиваем userinfo API (как было раньше) ---
            // Этот подход надежнее для получения картинки и других данных
            var accessToken = authenticateResult.Properties?.GetTokenValue("access_token");
            if (!string.IsNullOrEmpty(accessToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                try
                {
                    var userInfoResponse = await _httpClient.GetAsync("https://www.googleapis.com/oauth2/v3/userinfo");
                    if (userInfoResponse.IsSuccessStatusCode)
                    {
                        var userInfoJson = await userInfoResponse.Content.ReadAsStringAsync();
                        var userInfo = JObject.Parse(userInfoJson);
                        googleId = userInfo["sub"]?.ToString() ?? googleId; // Перезаписываем, если есть
                        email = userInfo["email"]?.ToString() ?? email;
                        name = userInfo["name"]?.ToString() ?? name;
                        pictureUrl = userInfo["picture"]?.ToString() ?? pictureUrl;
                        Console.WriteLine("[DEBUG GoogleResponse] UserInfo fetched from Google API.");
                    }
                    else
                    {
                        Console.WriteLine($"[WARN GoogleResponse] Failed to fetch UserInfo from Google API. Status: {userInfoResponse.StatusCode}");
                    }
                }
                catch (Exception apiEx)
                {
                    Console.WriteLine($"[ERROR GoogleResponse] Error fetching UserInfo from Google API: {apiEx.Message}");
                }
            }
            else
            {
                Console.WriteLine("[WARN GoogleResponse] Access token not found in authentication properties.");
            }
            // ---------------------------------------------------------------------------


            if (string.IsNullOrEmpty(googleId) || string.IsNullOrEmpty(email))
            {
                Console.WriteLine("[ERROR GoogleResponse] Missing GoogleId or Email after authentication.");
                return BadRequest("Не удалось получить Google ID или Email пользователя.");
            }

            Console.WriteLine($"[DEBUG GoogleResponse] User Authenticated: GoogleId={googleId}, Email={email}");

            try
            {
                // Ищем пользователя по GoogleId
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.GoogleId == googleId);
                User userEntity;
                bool isNewUser = false;

                if (existingUser == null)
                {
                    // Создаем нового пользователя
                    Console.WriteLine($"[DEBUG GoogleResponse] Creating new user for GoogleId: {googleId}");
                    isNewUser = true;
                    userEntity = new User
                    {
                        GoogleId = googleId,
                        Email = email,
                        Name = name ?? "Пользователь", // Имя по умолчанию
                        PictureUrl = pictureUrl ?? "https://via.placeholder.com/150", // Картинка по умолчанию
                        Rating = 0
                    };
                    _context.Users.Add(userEntity);
                    await _context.SaveChangesAsync(); // <<< Сохраняем, чтобы получить ID
                    await _achievementService.AwardAchievementAsync(userEntity.Id, "REGISTRATION");
                    Console.WriteLine($"[DEBUG GoogleResponse] New user created with ID: {userEntity.Id}");
                }
                else
                {
                    // Обновляем существующего пользователя
                    Console.WriteLine($"[DEBUG GoogleResponse] Found existing user with ID: {existingUser.Id}");
                    userEntity = existingUser;
                    userEntity.Name = name ?? userEntity.Name; // Обновляем имя, если оно пришло
                    userEntity.PictureUrl = pictureUrl ?? userEntity.PictureUrl; // Обновляем картинку
                    userEntity.Role = userEntity.Role;
                    // Можно обновлять и Email, если он изменился в Google, но осторожно
                    // userEntity.Email = email;
                    _context.Users.Update(userEntity);
                    // SaveChangesAsync будет вызван позже, после установки RefreshToken
                }

                // Генерируем наши JWT и Refresh токены
                var tokens = GenerateJwtTokens(userEntity);
                userEntity.RefreshToken = tokens.RefreshToken; // Сохраняем Refresh токен

                // Сохраняем изменения (обновление RefreshToken или создание нового юзера)
                await _context.SaveChangesAsync();
                Console.WriteLine($"[DEBUG GoogleResponse] RefreshToken updated/set for User ID: {userEntity.Id}");

                // Удаляем временную куку внешнего входа
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                // Устанавливаем наши куки для сессии API
                var cookieOptionsBase = new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,   // Требует HTTPS
                    SameSite = SameSiteMode.None, // Нужно для cross-site (frontend/backend на разных портах/доменах)
                    Path = "/",
                    // Domain = "localhost" // Обычно не нужно для localhost
                };

                Response.Cookies.Append("jwt", tokens.AccessToken, new CookieOptions(cookieOptionsBase)
                {
                    Expires = DateTimeOffset.UtcNow.AddMinutes(30), // Время жизни Access Token
                });
                Console.WriteLine("[DEBUG GoogleResponse] 'jwt' cookie appended.");

                Response.Cookies.Append("refreshToken", tokens.RefreshToken, new CookieOptions(cookieOptionsBase)
                {
                    Expires = DateTimeOffset.UtcNow.AddDays(7), // Время жизни Refresh Token
                });
                Console.WriteLine("[DEBUG GoogleResponse] 'refreshToken' cookie appended.");

                // Редирект на страницу профиля фронтенда
                return Redirect("http://localhost:5173/profile");
            }
            catch (DbUpdateException dbEx)
            {
                Console.WriteLine($"[ERROR GoogleResponse] Database error: {dbEx.InnerException?.Message ?? dbEx.Message}");
                return StatusCode(500, "Ошибка базы данных при обработке входа.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL ERROR GoogleResponse]: {ex.Message}\n{ex.StackTrace}");
                return StatusCode(500, "Внутренняя ошибка сервера при обработке входа.");
            }
        }

        // Получение данных текущего пользователя (защищено JWT)
        [HttpGet("user")]
        [Authorize] // <<< Требует валидный JWT токен (из куки 'jwt')
        public async Task<IActionResult> GetUser()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier); // ID из токена

            if (!int.TryParse(userIdString, out var userId))
            {
                Console.WriteLine("[ERROR GetUser] Invalid User ID format in token.");
                return Unauthorized("Неверный формат ID пользователя в токене.");
            }

            Console.WriteLine($"[DEBUG GetUser] Request for user ID: {userId}");

            var user = await _context.Users.FindAsync(userId);

            if (user == null)
            {
                Console.WriteLine($"[ERROR GetUser] User with ID {userId} not found in DB.");
                // Возвращаем 401, т.к. токен валидный, но юзера нет (удален?)
                return Unauthorized("Пользователь, связанный с токеном, не найден.");
            }

            // Возвращаем DTO с нужными полями
            return Ok(new
            {
                Id = user.Id, // <<< ОБЯЗАТЕЛЬНО ВОЗВРАЩАЕМ ID
                Name = user.Name,
                Email = user.Email,
                PictureUrl = user.PictureUrl,
                Role = user.Role.ToString(),
                // Можно добавить другие поля, если они нужны AuthContext
            });
        }

        // Выход из системы
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            Console.WriteLine("[DEBUG Logout] Attempting to delete cookies.");
            // Настройки удаления куки должны точно совпадать с настройками установки
            var cookieOptionsDelete = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                // Domain = "localhost"
            };
            Response.Cookies.Delete("jwt", cookieOptionsDelete);
            Response.Cookies.Delete("refreshToken", cookieOptionsDelete);
            Console.WriteLine("[DEBUG Logout] Cookies deleted.");
            return Ok(new { message = "Вы успешно вышли" });
        }


        // Обновление токена (если вы реализуете эту логику на фронтенде)
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            if (string.IsNullOrEmpty(request.RefreshToken))
                return BadRequest("Refresh token не предоставлен");

            Console.WriteLine($"[DEBUG RefreshToken] Attempting refresh with token: ...{request.RefreshToken.Substring(request.RefreshToken.Length - 5)}");

            // Находим пользователя по RefreshToken
            // Важно: RefreshToken должен быть уникальным!
            var user = await _context.Users.FirstOrDefaultAsync(u => u.RefreshToken == request.RefreshToken);

            if (user == null)
            {
                Console.WriteLine("[WARN RefreshToken] Invalid or expired refresh token provided.");
                return Unauthorized("Невалидный refresh token");
            }

            // Генерируем новые токены
            var newTokens = GenerateJwtTokens(user);

            // Обновляем RefreshToken в базе данных новым значением
            user.RefreshToken = newTokens.RefreshToken;
            await _context.SaveChangesAsync();

            Console.WriteLine($"[DEBUG RefreshToken] Tokens refreshed for User ID: {user.Id}");

            // Возвращаем новые токены клиенту
            // КЛИЕНТ ДОЛЖЕН САМ ОБНОВИТЬ КУКИ ИЛИ ИСПОЛЬЗОВАТЬ НОВЫЙ ACCESS TOKEN
            return Ok(new
            {
                AccessToken = newTokens.AccessToken,
                RefreshToken = newTokens.RefreshToken // Возвращаем новый refresh token
            });
        }


        // --- Вспомогательные методы ---

        private TokenModel GenerateJwtTokens(User user)
        {
            var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key не настроен");
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer не настроен");
            var jwtAudience = _configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience не настроен");

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), // Используем Sub для ID
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), // Дублируем в NameIdentifier для совместимости
                new Claim(JwtRegisteredClaimNames.Name, user.Name),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // Уникальный ID токена
                new Claim(ClaimTypes.Role, user.Role.ToString())
            };

            var accessToken = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30), // Короткое время жизни Access Token
                signingCredentials: credentials);

            var accessTokenString = new JwtSecurityTokenHandler().WriteToken(accessToken);

            // Генерируем простой Refresh Token (в реальном приложении лучше использовать более надежный способ)
            var refreshToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

            return new TokenModel
            {
                AccessToken = accessTokenString,
                RefreshToken = refreshToken
            };
        }
    }

    // Модели DTO для Refresh Token
    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; }
    }

    public class TokenModel
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
    }
}