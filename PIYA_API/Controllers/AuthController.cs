using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
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
    ISecurityHardeningService securityHardeningService,
    IOptions<SecurityOptions> securityOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    private readonly IUserService _userService = userService;
    private readonly IJwtService _jwtService = jwtService;
    private readonly IConfiguration _configuration = configuration;
    private readonly IAuditService _auditService = auditService;
    private readonly ITwoFactorAuthService _twoFactorService = twoFactorService;
    private readonly ISecurityHardeningService _securityHardeningService = securityHardeningService;
    private readonly SecurityOptions _securityOptions = securityOptions.Value;
    private readonly ILogger<AuthController> _logger = logger;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

    try
    {
        // Role is always Patient on self-registration. Admins use POST /{id}/assign-role.
        UserRole roleEnum = UserRole.Patient;

        // Validate DateOfBirth if provided
        DateTime? parsedDob = null;
        if (!string.IsNullOrWhiteSpace(request.DateOfBirth))
        {
            if (!DateTime.TryParse(request.DateOfBirth, out var dob))
                return BadRequest(new { message = "Invalid DateOfBirth format. Expected ISO 8601 (yyyy-MM-dd)." });
            parsedDob = dob;
        }
        if (parsedDob.HasValue && parsedDob.Value > DateTime.UtcNow.AddYears(-18))
        {
            return BadRequest(new { message = "You must be at least 18 years old to register." });
        }
        if(request.Role != null && request.Role != "Patient")
        {
            return BadRequest(new { message = "Invalid role specified. Role must be 'Patient' for self-registration." });
        }
        if(request.PhoneNumber == null || !System.Text.RegularExpressions.Regex.IsMatch(request.PhoneNumber, @"^\+?[1-9]\d{1,14}$"))
        {
            return BadRequest(new { message = "Invalid phone number format. Expected E.164 format (e.g. +1234567890)." });
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
                DateOfBirth = parsedDob ?? DateTime.UtcNow.AddYears(-18),
                Role = roleEnum,
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
            
            var tokenResponse = await _jwtService.GenerateSecurityToken(createdUser.Username);

            if (tokenResponse == null)
            {
                return StatusCode(500, new { message = "Failed to generate token" });
            }

            // Trigger email verification (best-effort — don't fail registration if email send fails)
            try
            {
                var emailVerificationService = HttpContext.RequestServices
                    .GetRequiredService<IEmailVerificationService>();
                await emailVerificationService.GenerateVerificationTokenAsync(
                    createdUser.Id, ipAddress ?? "", userAgent);
            }
            catch (Exception evEx)
            {
                _logger.LogWarning(evEx, "Could not send verification email for user {UserId}", createdUser.Id);
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
                isEmailVerified = createdUser.IsEmailVerified,
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
                    "LoginFailed", null, ipAddress, userAgent, false,
                    "Login attempted without username or email");
                return BadRequest(new { message = "Username or email is required" });
            }

            // Check brute-force lockout before touching the DB for the user
            var failedAttempts = await _securityHardeningService.GetFailedLoginAttemptsAsync(
                identifier, TimeSpan.FromMinutes(_securityOptions.LockoutDurationMinutes));
            if (failedAttempts.Count >= _securityOptions.MaxLoginAttempts)
            {
                await _auditService.LogSecurityEventAsync(
                    "LoginLockedOut", null, ipAddress, userAgent, false,
                    $"Locked-out account login attempt: {identifier}");
                return StatusCode(429, new { message = "Too many failed attempts. Please try again later." });
            }

            var user = await _userService.Authenticate(identifier, request.Password);

            if (user == null)
            {
                // Record failed attempt for lockout tracking
                await _securityHardeningService.RecordFailedLoginAttemptAsync(identifier, ipAddress);

                await _auditService.LogSecurityEventAsync(
                    "LoginFailed", null, ipAddress, userAgent, false,
                    $"Invalid credentials for identifier: {identifier}");
                return Unauthorized(new { message = "Invalid username or password" });
            }

            // Successful login — clear any prior failed attempts
            await _securityHardeningService.ResetFailedLoginAttemptsAsync(identifier);

            // Check if 2FA is enabled
            var requires2FA = await _twoFactorService.IsTwoFactorEnabledAsync(user.Id);
            if (requires2FA)
            {
                var challengeToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                await _auditService.LogSecurityEventAsync(
                    "LoginPending2FA", user.Id, ipAddress, userAgent, true,
                    $"Login successful, awaiting 2FA verification. Challenge: {challengeToken[..8]}…");

                return Ok(new
                {
                    requires2FA = true,
                    userId = user.Id,
                    message = "Please provide 2FA code"
                });
            }

            var tokenResponse = await _jwtService.GenerateSecurityToken(user.Username);
            if (tokenResponse == null)
                return StatusCode(500, new { message = "Failed to generate token" });

            await _auditService.LogSecurityEventAsync(
                "LoginSuccess", user.Id, ipAddress, userAgent, true,
                $"User logged in: {user.Username}");

            return Ok(new AuthResponse
            {
                UserId = user.Id,
                Username = user.Username,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role.ToString(),
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = tokenResponse.ExpiresAt,
                IsEmailVerified = user.IsEmailVerified
            });
        }
        catch (ArgumentException ex)
        {
            await _auditService.LogSecurityEventAsync(
                "LoginFailed", null, ipAddress, userAgent, false, $"Login error: {ex.Message}");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            await _auditService.LogSecurityEventAsync(
                "LoginFailed", null, ipAddress, userAgent, false, $"Login error: {ex.Message}");
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

    /// <summary>
    /// Returns the current authenticated user's profile — used by the frontend
    /// to rehydrate the session after a page refresh without a full re-login.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        try
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Invalid token" });

            var user = await _userService.GetByIdAsync(userId);
            if (user == null)
                return Unauthorized(new { message = "User not found" });

            return Ok(new
            {
                userId    = user.Id,
                username  = user.Username,
                email     = user.Email,
                firstName = user.FirstName,
                lastName  = user.LastName,
                role      = user.Role.ToString(),
                isEmailVerified = user.IsEmailVerified,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in /auth/me");
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)    {
        try
        {
            var tokenResponse = await _jwtService.RefreshAccessToken(request.RefreshToken);

            if (tokenResponse == null)
            {
                return Unauthorized(new { message = "Invalid or expired refresh token" });
            }

            return Ok(new
            {
                accessToken = tokenResponse.AccessToken,
                refreshToken = tokenResponse.RefreshToken,
                expiresAt = tokenResponse.ExpiresAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return StatusCode(500, new { message = "An error occurred during token refresh" });
        }
    }

    /// <summary>
    /// Logout — revokes the refresh token in the database so it cannot be reused
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                await _jwtService.RevokeRefreshTokenAsync(request.RefreshToken);
            }
            return Ok(new { message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new { message = "An error occurred during logout" });
        }
    }
}

public class LogoutRequest
{
    public string? RefreshToken { get; set; }
}

public class RegisterRequest
{
    public string? Username { get; set; }
    public required string Email { get; set; }
    public required string Password { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? PhoneNumber { get; set; }
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
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public required string AccessToken { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? RefreshToken { get; set; }
    public string? Role { get; set; }
    public bool IsEmailVerified { get; set; }
}
