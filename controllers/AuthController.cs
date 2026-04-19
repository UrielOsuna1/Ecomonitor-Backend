using Microsoft.AspNetCore.Mvc;
using backend_iot.Services;
using backend_iot.Models;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using MongoDB.Driver;

namespace backend_iot.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IConfiguration _config;
        private readonly MongoService _mongoService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IAuthService authService,
            IConfiguration config,
            MongoService mongoService,
            ILogger<AuthController> logger)
        {
            _authService = authService;
            _config = config;
            _mongoService = mongoService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest(new { message = "Email y contraseña son obligatorios" });
                }

                var user = _authService.Login(request.Email, request.Password);

                if (user == null)
                {
                    await SafeRegisterLogAsync(null, "LOGIN_FAILED", $"Intento de acceso fallido para: {request.Email}");
                    return Unauthorized(new { message = "Credenciales inválidas" });
                }

                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? _config["Jwt:Key"];
                var key = Encoding.ASCII.GetBytes(jwtKey);

                string userRole = user.Rol?.Equals("admin", StringComparison.OrdinalIgnoreCase) == true
                                  ? "Admin"
                                  : (user.Rol ?? "user");

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new[] {
                        new Claim(ClaimTypes.NameIdentifier, user.Id ?? ""),
                        new Claim(ClaimTypes.Name, user.Nombre ?? ""),
                        new Claim(ClaimTypes.Email, user.Email ?? ""),
                        new Claim(ClaimTypes.Role, userRole)
                    }),
                    Expires = DateTime.UtcNow.AddHours(2),
                    SigningCredentials = new SigningCredentials(
                        new SymmetricSecurityKey(key),
                        SecurityAlgorithms.HmacSha256Signature)
                };

                var token = tokenHandler.CreateToken(tokenDescriptor);

                await SafeRegisterLogAsync(user.Id, "LOGIN_SUCCESS", $"Sesión iniciada por el usuario: {user.Email}");

                return Ok(new
                {
                    id = user.Id,
                    nombre = user.Nombre,
                    rol = user.Rol,
                    token = tokenHandler.WriteToken(token)
                });
            }
            catch (Exception ex) when (IsMongoUnavailable(ex))
            {
                _logger.LogError(ex, "MongoDB no disponible durante el login para {Email}", request?.Email);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Servicio de autenticación temporalmente no disponible",
                    code = "database_unavailable"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado durante el login para {Email}", request?.Email);
                return StatusCode(500, new { message = "Error interno al iniciar sesión" });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] User user)
        {
            try
            {
                await _authService.Register(user);
                await SafeRegisterLogAsync(null, "USER_REGISTER", $"Nuevo usuario registrado: {user.Email}");
                return Ok(new { message = "Registro exitoso" });
            }
            catch (Exception ex) when (IsMongoUnavailable(ex))
            {
                _logger.LogError(ex, "MongoDB no disponible durante el registro para {Email}", user?.Email);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Servicio de registro temporalmente no disponible",
                    code = "database_unavailable"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private async Task SafeRegisterLogAsync(string? userId, string accion, string detalle)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                await _mongoService.RegistrarLogAsync(userId, accion, detalle, ip);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo registrar el log {Accion}", accion);
            }
        }

        private static bool IsMongoUnavailable(Exception ex)
        {
            return ex is MongoConnectionException
                || ex is TimeoutException
                || ex is MongoAuthenticationException
                || ex.InnerException is not null && IsMongoUnavailable(ex.InnerException);
        }
    }

    public class LoginDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
