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
            if (request.Role != null && request.Role != "Patient")
            {
                return BadRequest(new { message = "Invalid role specified. Role must be 'Patient' for self-registration." });
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.PhoneNumber, @"^\+?[1-9]\d{1,14}$"))
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
                // DateOfBirth is optional. We store null rather than silently defaulting
                // to 18-years-ago, which would produce incorrect data for users who omit it.
                DateOfBirth = parsedDob,
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

            var tokenResponse = await _jwtService.GenerateSecurityToken(
                createdUser.Username, Request.Headers.UserAgent.ToString().Contains("Expo") ? "Mobile" : "Web");

            if (tokenResponse == null)
                return StatusCode(500, new { message = "Failed to generate token" });

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
            // Also set the refresh token as an HttpOnly cookie for browser clients.
            SetRefreshTokenCookie(tokenResponse.RefreshToken);

            return Ok(new
            {
                userId = createdUser.Id,
                username = createdUser.Username,
                email = createdUser.Email,
                accessToken = tokenResponse.AccessToken,
                expiresAt = tokenResponse.ExpiresAt,
                // refreshToken is included for mobile/API clients that cannot use HttpOnly cookies.
                // Browser clients should use the cookie set above instead.
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
            // Accept identifier from the dedicated Identifier field (mobile/API clients),
            // or fall back to Username / Email for backward compatibility with web clients.
            var identifier = request.Identifier
                ?? (string.IsNullOrWhiteSpace(request.Username) ? request.Email : request.Username);

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
                // Issue a server-side challenge token — the client must present it when calling /2fa/verify.
                // This prevents any anonymous caller from verifying codes for arbitrary user IDs.
                var challengeToken = await _twoFactorService.IssueChallenge(user.Id);

                await _auditService.LogSecurityEventAsync(
                    "LoginPending2FA", user.Id, ipAddress, userAgent, true,
                    "Login successful, awaiting 2FA verification");

                return Ok(new
                {
                    requires2FA = true,
                    userId = user.Id,
                    challengeToken,
                    message = "Please provide 2FA code"
                });
            }

            var tokenResponse = await _jwtService.GenerateSecurityToken(
                user.Username, Request.Headers.UserAgent.ToString().Contains("Expo") ? "Mobile" : "Web");
            if (tokenResponse == null)
                return StatusCode(500, new { message = "Failed to generate token" });

            await _auditService.LogSecurityEventAsync(
                "LoginSuccess", user.Id, ipAddress, userAgent, true,
                $"User logged in: {user.Username}");

            // Set the refresh token as an HttpOnly Secure SameSite=Strict cookie.
            // The access token is returned in the body only — the client must store it
            // in memory (not localStorage) to eliminate XSS token-theft risk.
            SetRefreshTokenCookie(tokenResponse.RefreshToken);

            return Ok(new AuthResponse
            {
                UserId = user.Id,
                Username = user.Username,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role.ToString(),
                AccessToken = tokenResponse.AccessToken,
                // RefreshToken is intentionally NOT included in the body — it is set as
                // an HttpOnly cookie above. Mobile clients that cannot use cookies must
                // call POST /api/auth/refresh using the cookie or store the token securely
                // via their platform keychain, never in plain memory/storage.
                RefreshToken = null,
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

    /// <summary>
    /// Complete the 2FA login flow for mobile / API clients that cannot use HttpOnly cookies.
    /// Verifies the OTP code against the challenge token, then issues access + refresh tokens
    /// in the response body so the client can persist them via platform secure storage.
    /// </summary>
    [HttpPost("login/complete-2fa")]
    [AllowAnonymous]
    public async Task<IActionResult> CompleteLogin2FA([FromBody] Complete2FARequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        try
        {
            // Validate challenge token (consumed one-time)
            if (string.IsNullOrWhiteSpace(request.ChallengeToken) ||
                !await _twoFactorService.ConsumeChallenge(request.UserId, request.ChallengeToken))
            {
                return Unauthorized(new { message = "Invalid or expired challenge token. Please log in again." });
            }

            // Verify the OTP code
            var isValid = await _twoFactorService.VerifyCodeAsync(request.UserId, request.Code);
            if (!isValid)
            {
                await _auditService.LogSecurityEventAsync(
                    "Login2FAFailed", request.UserId, ipAddress, userAgent, false, "Invalid 2FA code");
                return Unauthorized(new { message = "Invalid or expired code" });
            }

            // Fetch user and issue tokens
            var user = await _userService.GetByIdAsync(request.UserId);
            if (user == null)
                return NotFound(new { message = "User not found" });

            var tokenResponse = await _jwtService.GenerateSecurityToken(
                user.Username, Request.Headers.UserAgent.ToString().Contains("Expo") ? "Mobile" : "Web");
            if (tokenResponse == null)
                return StatusCode(500, new { message = "Failed to generate token" });

            await _auditService.LogSecurityEventAsync(
                "LoginSuccess2FA", user.Id, ipAddress, userAgent, true,
                $"User completed 2FA login: {user.Username}");

            // For browser clients, also set the cookie
            SetRefreshTokenCookie(tokenResponse.RefreshToken);

            return Ok(new AuthResponse
            {
                UserId        = user.Id,
                Username      = user.Username,
                Email         = user.Email,
                FirstName     = user.FirstName,
                LastName      = user.LastName,
                Role          = user.Role.ToString(),
                AccessToken   = tokenResponse.AccessToken,
                RefreshToken  = tokenResponse.RefreshToken, // included for mobile clients
                ExpiresAt     = tokenResponse.ExpiresAt,
                IsEmailVerified = user.IsEmailVerified,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing 2FA login for user {UserId}", request.UserId);
            return StatusCode(500, new { message = "An error occurred during 2FA verification" });
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
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        try
        {
            // Support both: refresh token in body (API/mobile clients) and HttpOnly cookie (browser clients)
            var incomingRefresh = request.RefreshToken;
            if (string.IsNullOrWhiteSpace(incomingRefresh))
                incomingRefresh = Request.Cookies["piya_refresh_token"];

            if (string.IsNullOrWhiteSpace(incomingRefresh))
                return Unauthorized(new { message = "Refresh token is required" });

            var tokenResponse = await _jwtService.RefreshAccessToken(incomingRefresh);

            if (tokenResponse == null)
            {
                return Unauthorized(new { message = "Invalid or expired refresh token" });
            }

            SetRefreshTokenCookie(tokenResponse.RefreshToken);

            return Ok(new
            {
                accessToken = tokenResponse.AccessToken,
                refreshToken = tokenResponse.RefreshToken,
                expiresAt = tokenResponse.ExpiresAt
            });
        }
        catch (RefreshTokenReuseException ex)
        {
            // Token reuse attack detected — entire session family already revoked by JwtService.
            // Clear the cookie and return 401 so the client is forced to re-authenticate.
            _logger.LogWarning(
                "Refresh token reuse attack detected for user {UserId}. All sessions revoked.",
                ex.UserId);
            Response.Cookies.Delete("piya_refresh_token",
                new CookieOptions { Path = "/api/auth", Domain = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment() ? null : ".piya.life" });
            await _auditService.LogSecurityEventAsync(
                "RefreshTokenReuseDetected",
                ex.UserId,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                false,
                "Refresh token reuse detected — all sessions for this user were revoked.");
            return Unauthorized(new { message = "Security violation detected. Please log in again." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return StatusCode(500, new { message = "An error occurred during token refresh" });
        }
    }

    /// <summary>Sets the refresh token as an HttpOnly Secure SameSite=None cookie (7-day window).
    /// SameSite=None is required because the browser frontend (test.piya.life / piya.life) and
    /// the API (api.piya.life) are on different subdomains — browsers block SameSite=Strict/Lax
    /// cookies on cross-origin requests. SameSite=None mandates Secure=true per browser spec.
    /// Domain=.piya.life makes the cookie visible to all *.piya.life subdomains.</summary>
    private void SetRefreshTokenCookie(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var isDev = HttpContext.RequestServices
                        .GetRequiredService<IWebHostEnvironment>()
                        .IsDevelopment();

        Response.Cookies.Append("piya_refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure   = true,          // required by spec when SameSite=None; fine in dev with HTTPS
            SameSite = isDev
                ? SameSiteMode.Lax    // localhost dev: same-site, Lax is sufficient
                : SameSiteMode.None,  // production: cross-subdomain (test.piya.life → api.piya.life)
            Domain   = isDev ? null : ".piya.life",  // share across all *.piya.life subdomains
            Expires  = DateTimeOffset.UtcNow.AddDays(7),
            Path     = "/api/auth", // only sent to auth endpoints — reduces cookie surface
        });
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
            // Support refresh token from body (API/mobile) or HttpOnly cookie (browser)
            var refreshToken = request.RefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken))
                refreshToken = Request.Cookies["piya_refresh_token"];

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                await _jwtService.RevokeRefreshTokenAsync(refreshToken);
            }

            // Clear the HttpOnly cookie regardless of how the refresh token arrived
            Response.Cookies.Delete("piya_refresh_token",
                new CookieOptions { Path = "/api/auth", Domain = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment() ? null : ".piya.life" });

            // Revoke the access token jti so it is rejected immediately (before expiry)
            if (!string.IsNullOrWhiteSpace(request.AccessToken))
            {
                await _jwtService.RevokeAccessTokenAsync(request.AccessToken);
            }
            else
            {
                // Fallback: extract from Authorization header if client didn't send it in body
                var bearerToken = Request.Headers.Authorization.ToString();
                if (bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    await _jwtService.RevokeAccessTokenAsync(
                        bearerToken["Bearer ".Length..].Trim());
                }
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
    public string? AccessToken { get; set; }
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
    /// <summary>
    /// Mobile / API clients send a single identifier (email or username).
    /// Web clients may send Username and Email separately; all three are accepted.
    /// </summary>
    public string? Identifier { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public required string Password { get; set; }
}

public class Complete2FARequest
{
    public Guid   UserId         { get; set; }
    public required string Code           { get; set; }
    public required string ChallengeToken { get; set; }
}

public class ValidateTokenRequest
{
    public required string Token { get; set; }
}

public class RefreshTokenRequest
{
    /// <summary>
    /// Optional for browser clients — they send the refresh token as an HttpOnly
    /// cookie instead.  Required for API / mobile clients that cannot use cookies.
    /// </summary>
    public string? RefreshToken { get; set; }
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
