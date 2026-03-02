using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IUserService userService,
    IJwtService jwtService,
    IConfiguration configuration,
    IAuditService auditService,
    ITwoFactorAuthService twoFactorService,
    ILogger<AuthController> logger) : ControllerBase
{
    private readonly IUserService _userService = userService;
    private readonly IJwtService _jwtService = jwtService;
    private readonly IConfiguration _configuration = configuration;
    private readonly IAuditService _auditService = auditService;
    private readonly ITwoFactorAuthService _twoFactorService = twoFactorService;
    private readonly ILogger<AuthController> _logger = logger;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

    try
    {
        // Parse role coming from client (string) into UserRole enum. Default to Patient on parse failure.
        UserRole roleEnum = UserRole.Patient;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            Enum.TryParse<UserRole>(request.Role, true, out roleEnum);
        }

        var user = new User
        {
                    Username = string.IsNullOrWhiteSpace(request.Username)
                        ? (request.Email?.Split('@')[0] ?? Guid.NewGuid().ToString())
                        : request.Username,
                    Email = request.Email ?? throw new ArgumentException("Email is required"),
                FirstName = request.FirstName,
                LastName = request.LastName,
                PhoneNumber = request.PhoneNumber,
                DateOfBirth = DateTime.TryParse(request.DateOfBirth, out var dob) ? dob : DateTime.MinValue,
                Role = roleEnum,
                TokensInfo = new Token
                {
                    Id = Guid.NewGuid(),
                    AccessToken = string.Empty,
                    RefreshToken = string.Empty,
                    ExpiresAt = DateTime.UtcNow,
                    DeviceInfo = request.DeviceInfo ?? "Unknown"
                }
            };

            var createdUser = await _userService.Create(user, request.Password);
            
            // Log registration
            await _auditService.LogSecurityEventAsync(
                "UserRegistered",
                createdUser.Id,
                ipAddress,
                userAgent,
                true,
                $"New user registered: {createdUser.Username} with role {createdUser.Role}"
            );
            
            var tokenResponse = _jwtService.GenerateSecurityToken(createdUser.Username);

            if (tokenResponse == null)
            {
                return StatusCode(500, new { message = "Failed to generate token" });
            }

            // Return the token key in multiple forms so existing integration tests (and older clients) can find it.
            return Ok(new
            {
                userId = createdUser.Id,
                username = createdUser.Username,
                email = createdUser.Email,
                accessToken = tokenResponse.AccessToken,
                expiresAt = tokenResponse.ExpiresAt,
                refreshToken = tokenResponse.RefreshToken,
                role = createdUser.Role.ToString(),
                // legacy short key used by some tests
                token = tokenResponse.AccessToken
            });
        }
        catch (ArgumentException ex)
        {
            await _auditService.LogSecurityEventAsync(
                "RegistrationFailed",
                null,
                ipAddress,
                userAgent,
                false,
                $"Registration failed: {ex.Message}"
            );
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            await _auditService.LogSecurityEventAsync(
                "RegistrationFailed",
                null,
                ipAddress,
                userAgent,
                false,
                $"Registration conflict: {ex.Message}"
            );
            // Tests expect a BadRequest when trying to register a duplicate user/email
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            await _auditService.LogSecurityEventAsync(
                "RegistrationFailed",
                null,
                ipAddress,
                userAgent,
                false,
                $"Registration error: {ex.Message}"
            );
            return StatusCode(500, new { message = "An error occurred during registration" });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        try
        {
            var identifier = string.IsNullOrWhiteSpace(request.Username) ? request.Email : request.Username;

            if (string.IsNullOrWhiteSpace(identifier))
            {
                await _auditService.LogSecurityEventAsync(
                    "LoginFailed",
                    null,
                    ipAddress,
                    userAgent,
                    false,
                    "Login attempted without username or email"
                );
                return BadRequest(new { message = "Username or email is required" });
            }

            var user = await _userService.Authenticate(identifier, request.Password);

            if (user == null)
            {
                await _auditService.LogSecurityEventAsync(
                    "LoginFailed",
                    null,
                    ipAddress,
                    userAgent,
                    false,
                    $"Invalid credentials for username: {request.Username}"
                );
                return Unauthorized(new { message = "Invalid username or password" });
            }

            // Check if 2FA is enabled
            var requires2FA = await _twoFactorService.IsTwoFactorEnabledAsync(user.Id);
            if (requires2FA)
            {
                // Don't generate full token yet - return challenge for 2FA
                await _auditService.LogSecurityEventAsync(
                    "LoginPending2FA",
                    user.Id,
                    ipAddress,
                    userAgent,
                    true,
                    "Login successful, awaiting 2FA verification"
                );

                return Ok(new
                {
                    requires2FA = true,
                    userId = user.Id,
                    message = "Please provide 2FA code"
                });
            }

            var tokenResponse = _jwtService.GenerateSecurityToken(user.Username);

            if (tokenResponse == null)
            {
                return StatusCode(500, new { message = "Failed to generate token" });
            }

            await _auditService.LogSecurityEventAsync(
                "LoginSuccess",
                user.Id,
                ipAddress,
                userAgent,
                true,
                $"User logged in: {user.Username}"
            );

            return Ok(new AuthResponse
            {
                UserId = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role.ToString(),
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = tokenResponse.ExpiresAt
            });
        }
        catch (ArgumentException ex)
        {
            await _auditService.LogSecurityEventAsync(
                "LoginFailed",
                null,
                ipAddress,
                userAgent,
                false,
                $"Login error: {ex.Message}"
            );
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            await _auditService.LogSecurityEventAsync(
                "LoginFailed",
                null,
                ipAddress,
                userAgent,
                false,
                $"Login error: {ex.Message}"
            );
            return StatusCode(500, new { message = "An error occurred during login" });
        }
    }

    [HttpPost("validate-token")]
    public IActionResult ValidateToken([FromBody] ValidateTokenRequest request)
    {
        try
        {
            var username = _jwtService.ValidateToken(request.Token);

            if (username == null)
            {
                return Unauthorized(new { message = "Invalid or expired token" });
            }

            return Ok(new
            {
                valid = true,
                username
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token validation error");
            return BadRequest(new { message = "Token validation failed" });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        try
        {
            var newAccessToken = await _jwtService.RefreshAccessToken(request.RefreshToken);

            if (newAccessToken == null)
            {
                return Unauthorized(new { message = "Invalid or expired refresh token" });
            }

            var expirationMinutes = int.TryParse(_configuration["Jwt:ExpirationMinutes"], out var mins) ? mins : 30;

            return Ok(new
            {
                accessToken = newAccessToken,
                expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return StatusCode(500, new { message = "An error occurred during token refresh" });
        }
    }
}

public class RegisterRequest
{
    public string? Username { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string PhoneNumber { get; set; }
    public string? DateOfBirth { get; set; }
    public string? DeviceInfo { get; set; }
    // Accept role as string from clients/tests (e.g. "Patient") and parse below.
    public string? Role { get; set; }
}

public class LoginRequest
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public required string Password { get; set; }
}

public class ValidateTokenRequest
{
    public required string Token { get; set; }
}

public class RefreshTokenRequest
{
    public required string RefreshToken { get; set; }
}

public class AuthResponse
{
    public Guid UserId { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string AccessToken { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? RefreshToken { get; set; }
    public string? Role { get; set; }
}
