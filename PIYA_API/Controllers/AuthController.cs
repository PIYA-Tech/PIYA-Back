using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Model;
using PIYA_API.Security;
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
    IFcmService fcmService,
    IOptions<SecurityOptions> securityOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    private readonly IUserService _userService = userService;
    private readonly IJwtService _jwtService = jwtService;
    private readonly IConfiguration _configuration = configuration;
    private readonly IAuditService _auditService = auditService;
    private readonly ITwoFactorAuthService _twoFactorService = twoFactorService;
    private readonly ISecurityHardeningService _securityHardeningService = securityHardeningService;
    private readonly IFcmService _fcmService = fcmService;
    private readonly SecurityOptions _securityOptions = securityOptions.Value;
    private readonly ILogger<AuthController> _logger = logger;

    [HttpPost("register")]
    [AllowAnonymous]
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

            var usesBodyRefreshToken = RefreshTokenTransportPolicy.UsesResponseBody(Request);
            var tokenResponse = await _jwtService.GenerateSecurityToken(
                createdUser.Username, RefreshTokenTransportPolicy.GetDeviceInfo(Request));

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

            if (!usesBodyRefreshToken)
                SetRefreshTokenCookie(tokenResponse.RefreshToken);

            return Ok(new
            {
                userId = createdUser.Id,
                username = createdUser.Username,
                email = createdUser.Email,
                accessToken = tokenResponse.AccessToken,
                expiresAt = tokenResponse.ExpiresAt,
                // Native clients explicitly opt into body transport with X-PIYA-Client.
                // Browser JavaScript can never receive the refresh token.
                refreshToken = usesBodyRefreshToken ? tokenResponse.RefreshToken : null,
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
    [AllowAnonymous]
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

            // Native clients must opt into response-body refresh tokens explicitly;
            // User-Agent sniffing is not a security boundary.
            var usesBodyRefreshToken = RefreshTokenTransportPolicy.UsesResponseBody(Request);

            // Check if 2FA is enabled
            var requires2FA = await _twoFactorService.IsTwoFactorEnabledAsync(user.Id);
            if (requires2FA)
            {
                // Issue a server-side challenge token — the client must present it when calling /login/complete-2fa.
                var challengeToken = await _twoFactorService.IssueChallenge(user.Id);

                // If the user has a trusted mobile 2FA device, send the OTP via push notification.
                // Otherwise fall back to TOTP / SMS / Email depending on their configured method.
                var hasTrustedDevice = await _twoFactorService.HasTrusted2FADeviceAsync(user.Id);
                string twoFADelivery = "totp";
                if (hasTrustedDevice)
                {
                    var pushSent = await _twoFactorService.SendPush2FACodeAsync(user.Id);
                    if (pushSent)
                        twoFADelivery = "push";
                }

                await _auditService.LogSecurityEventAsync(
                    "LoginPending2FA", user.Id, ipAddress, userAgent, true,
                    $"Login successful, awaiting 2FA verification (delivery: {twoFADelivery})");

                return Ok(new
                {
                    requires2FA = true,
                    userId = user.Id,
                    challengeToken,
                    twoFADelivery, // "push" | "totp" — lets the client show the right UI
                    message = twoFADelivery == "push"
                        ? "A verification code has been sent to your trusted device."
                        : "Please provide your authenticator code."
                });
            }

            var tokenResponse = await _jwtService.GenerateSecurityToken(
                user.Username, RefreshTokenTransportPolicy.GetDeviceInfo(Request));
            if (tokenResponse == null)
                return StatusCode(500, new { message = "Failed to generate token" });

            // If this is a mobile login and the request included an FCM token, update LastLoginAt.
            // Also auto-register the device as the trusted 2FA device on first mobile login.
            if (!string.IsNullOrEmpty(request.FcmToken))
            {
                // Ensure device is registered (upsert)
                await _fcmService.RegisterDeviceTokenAsync(
                    user.Id, request.FcmToken, "iOS",
                    deviceModel: request.DeviceName,
                    deviceName: request.DeviceName);

                await _fcmService.UpdateDeviceLastLoginAsync(user.Id, request.FcmToken);

                // If the user has no trusted 2FA device yet, promote this one automatically
                var hasTrusted = await _twoFactorService.HasTrusted2FADeviceAsync(user.Id);
                if (!hasTrusted)
                {
                    var devices = await _fcmService.GetUserDevicesAsync(user.Id);
                    var thisDevice = devices.FirstOrDefault(d => d.Token == request.FcmToken);
                    if (thisDevice != null)
                        await _fcmService.TrustDeviceFor2FAAsync(user.Id, thisDevice.Id);
                }
            }

            await _auditService.LogSecurityEventAsync(
                "LoginSuccess", user.Id, ipAddress, userAgent, true,
                $"User logged in: {user.Username}");

            // Set the refresh token as an HttpOnly Secure SameSite=Strict cookie.
            // The access token is returned in the body only — the client must store it
            // in memory (not localStorage) to eliminate XSS token-theft risk.
            if (!usesBodyRefreshToken)
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
                RefreshToken = usesBodyRefreshToken ? tokenResponse.RefreshToken : null,
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
                user.Username, RefreshTokenTransportPolicy.GetDeviceInfo(Request));
            if (tokenResponse == null)
                return StatusCode(500, new { message = "Failed to generate token" });

            await _auditService.LogSecurityEventAsync(
                "LoginSuccess2FA", user.Id, ipAddress, userAgent, true,
                $"User completed 2FA login: {user.Username}");

            var usesBodyRefreshToken = RefreshTokenTransportPolicy.UsesResponseBody(Request);
            if (!usesBodyRefreshToken)
                SetRefreshTokenCookie(tokenResponse.RefreshToken);

            return Ok(new AuthResponse
            {
                UserId          = user.Id,
                Username        = user.Username,
                Email           = user.Email,
                FirstName       = user.FirstName,
                LastName        = user.LastName,
                Role            = user.Role.ToString(),
                AccessToken     = tokenResponse.AccessToken,
                RefreshToken    = usesBodyRefreshToken ? tokenResponse.RefreshToken : null,
                ExpiresAt       = tokenResponse.ExpiresAt,
                IsEmailVerified = user.IsEmailVerified,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing 2FA login for user {UserId}", request.UserId);
            return StatusCode(500, new { message = "An error occurred during 2FA verification" });
        }
    }

    /// <summary>
    /// Complete the 2FA login flow using a backup code.
    /// Validates the challenge token, verifies the backup code (consuming it),
    /// then issues access + refresh tokens exactly like /login/complete-2fa.
    /// </summary>
    [HttpPost("login/complete-2fa-backup")]
    [AllowAnonymous]
    public async Task<IActionResult> CompleteLogin2FAWithBackup([FromBody] Complete2FABackupRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        try
        {
            // Validate and consume the one-time challenge token
            if (string.IsNullOrWhiteSpace(request.ChallengeToken) ||
                !await _twoFactorService.ConsumeChallenge(request.UserId, request.ChallengeToken))
            {
                return Unauthorized(new { message = "Invalid or expired challenge token. Please log in again." });
            }

            // Verify the backup code (consuming it so it can't be reused)
            var isValid = await _twoFactorService.VerifyBackupCodeAsync(request.UserId, request.BackupCode);
            if (!isValid)
            {
                await _auditService.LogSecurityEventAsync(
                    "Login2FABackupFailed", request.UserId, ipAddress, userAgent, false, "Invalid backup code");
                return Unauthorized(new { message = "Invalid backup code" });
            }

            // Fetch user and issue tokens
            var user = await _userService.GetByIdAsync(request.UserId);
            if (user == null)
                return NotFound(new { message = "User not found" });

            var tokenResponse = await _jwtService.GenerateSecurityToken(
                user.Username, RefreshTokenTransportPolicy.GetDeviceInfo(Request));
            if (tokenResponse == null)
                return StatusCode(500, new { message = "Failed to generate token" });

            await _auditService.LogSecurityEventAsync(
                "LoginSuccess2FABackup", user.Id, ipAddress, userAgent, true,
                $"User completed 2FA login via backup code: {user.Username}");

            var usesBodyRefreshToken = RefreshTokenTransportPolicy.UsesResponseBody(Request);
            if (!usesBodyRefreshToken)
                SetRefreshTokenCookie(tokenResponse.RefreshToken);

            return Ok(new AuthResponse
            {
                UserId          = user.Id,
                Username        = user.Username,
                Email           = user.Email,
                FirstName       = user.FirstName,
                LastName        = user.LastName,
                Role            = user.Role.ToString(),
                AccessToken     = tokenResponse.AccessToken,
                RefreshToken    = usesBodyRefreshToken ? tokenResponse.RefreshToken : null,
                ExpiresAt       = tokenResponse.ExpiresAt,
                IsEmailVerified = user.IsEmailVerified,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing backup-code 2FA login for user {UserId}", request.UserId);
            return StatusCode(500, new { message = "An error occurred during 2FA verification" });
        }
    }

    [HttpPost("validate-token")]
    [Authorize]
    public IActionResult ValidateToken([FromBody] ValidateTokenRequest request)
    {
        // Authentication middleware has already validated the signature, lifetime,
        // issuer/audience, and revocation state of the bearer token. Never parse an
        // arbitrary body-supplied JWT and perform another revocation-store lookup.
        var bearer = GetAuthenticatedBearerToken();
        if (string.IsNullOrWhiteSpace(bearer) ||
            !FixedTimeEquals(bearer, request.Token))
        {
            return Unauthorized(new { message = "The token must match the authenticated bearer token" });
        }

        return Ok(new
        {
            valid = true,
            username = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
        });
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
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        try
        {
            var usesBodyRefreshToken = RefreshTokenTransportPolicy.UsesResponseBody(Request);
            // Native clients explicitly use the request/response body. Browsers must use
            // the HttpOnly cookie; a browser body value is deliberately ignored.
            var incomingRefresh = usesBodyRefreshToken
                ? request.RefreshToken
                : Request.Cookies["piya_refresh_token"];

            if (string.IsNullOrWhiteSpace(incomingRefresh))
                return Unauthorized(new { message = "Refresh token is required" });

            var tokenResponse = await _jwtService.RefreshAccessToken(incomingRefresh);

            if (tokenResponse == null)
            {
                return Unauthorized(new { message = "Invalid or expired refresh token" });
            }

            if (!usesBodyRefreshToken)
                SetRefreshTokenCookie(tokenResponse.RefreshToken);

            return Ok(new
            {
                accessToken = tokenResponse.AccessToken,
                refreshToken = usesBodyRefreshToken ? tokenResponse.RefreshToken : null,
                expiresAt = tokenResponse.ExpiresAt
            });
        }
        catch (ConcurrentRefreshTokenException)
        {
            // A parallel request already rotated this token. Do not clear the cookie
            // or audit it as an attack: the winning response owns the new credential.
            return Conflict(new { message = "A concurrent refresh already completed. Please retry." });
        }
        catch (RefreshTokenReuseException ex)
        {
            // Token reuse attack detected — entire session family already revoked by JwtService.
            // Clear the cookie and return 401 so the client is forced to re-authenticate.
            _logger.LogWarning(
                "Refresh token reuse attack detected for user {UserId}. All sessions revoked.",
                ex.UserId);
            DeleteRefreshTokenCookie();
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

    /// <summary>
    /// Sets a host-only HttpOnly refresh cookie. piya.life and api.piya.life are
    /// cross-origin but same-site, so SameSite=Strict remains compatible while a
    /// host-only cookie avoids exposing the credential to sibling subdomains.
    /// </summary>
    private void SetRefreshTokenCookie(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;

        var env = HttpContext.RequestServices
                        .GetRequiredService<IWebHostEnvironment>();
        var isNonProd = env.IsDevelopment()
                     || env.IsEnvironment("Test")
                     || env.IsEnvironment("LoadTest");

        Response.Cookies.Append("piya_refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure   = !isNonProd,    // don't force Secure on http://localhost in tests
            SameSite = SameSiteMode.Strict,
            Expires  = DateTimeOffset.UtcNow.AddDays(7),
            Path     = "/api/auth", // only sent to auth endpoints — reduces cookie surface
        });
    }

    /// <summary>
    /// Logout — revokes the refresh token in the database so it cannot be reused
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        try
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Invalid authenticated principal" });

            var refreshToken = RefreshTokenTransportPolicy.UsesResponseBody(Request)
                ? request.RefreshToken
                : Request.Cookies["piya_refresh_token"];

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var revoked = await _jwtService.RevokeRefreshTokenAsync(refreshToken, userId);
                if (!revoked)
                {
                    _logger.LogWarning(
                        "Logout for user {UserId} supplied an unknown or non-owned refresh token; current access token will still be revoked",
                        userId);
                }
            }

            DeleteRefreshTokenCookie();

            // Revoke only the token that authentication middleware validated for this
            // principal. The body AccessToken field is retained for wire compatibility
            // but intentionally ignored.
            var bearerToken = GetAuthenticatedBearerToken();
            if (!string.IsNullOrWhiteSpace(bearerToken))
                await _jwtService.RevokeAccessTokenAsync(bearerToken);

            return Ok(new { message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new { message = "An error occurred during logout" });
        }
    }

    private void DeleteRefreshTokenCookie() =>
        Response.Cookies.Delete("piya_refresh_token", new CookieOptions
        {
            Path = "/api/auth",
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict
        });

    private string? GetAuthenticatedBearerToken()
    {
        var authorization = Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    /// <summary>
    /// Change the authenticated user's own password after verifying the current password.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        try
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Invalid token" });

            await _userService.ChangePasswordAsync(userId, request.OldPassword, request.NewPassword);

            await _auditService.LogSecurityEventAsync(
                "PasswordChanged", userId, ipAddress, userAgent, true,
                "User changed their own password");

            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return Unauthorized(new { message = "User not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return StatusCode(500, new { message = "An error occurred while changing the password" });
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
    /// <summary>
    /// Optional FCM device token — when present, LastLoginAt is updated on the device
    /// and the device becomes eligible to be promoted to a trusted 2FA device.
    /// </summary>
    public string? FcmToken { get; set; }
    /// <summary>
    /// Human-readable device name (e.g. "iPhone 15 Pro") shown in the Active Devices list.
    /// </summary>
    public string? DeviceName { get; set; }
}

public class Complete2FARequest
{
    public Guid   UserId         { get; set; }
    public required string Code           { get; set; }
    public required string ChallengeToken { get; set; }
}

public class Complete2FABackupRequest
{
    public Guid   UserId         { get; set; }
    public required string BackupCode     { get; set; }
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
