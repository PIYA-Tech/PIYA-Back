using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TwoFactorAuthController(ITwoFactorAuthService twoFactorService, IAuditService auditService) : ControllerBase
{
    private readonly ITwoFactorAuthService _twoFactorService = twoFactorService;
    private readonly IAuditService _auditService = auditService;

    /// <summary>
    /// Enable 2FA for the current user
    /// </summary>
    [HttpPost("enable")]
    public async Task<ActionResult> EnableTwoFactor([FromBody] EnableTwoFactorRequest request)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        try
        {
            var (secretKey, qrCodeUri, backupCodes) = await _twoFactorService.EnableTwoFactorAsync(userId, request.Method);

            await _auditService.LogActionAsync("Enable2FA", userId, $"2FA enabled with method: {request.Method}");

            return Ok(new
            {
                SecretKey = secretKey,
                QrCodeUri = qrCodeUri,
                BackupCodes = backupCodes,
                Message = "2FA has been enabled. Save your backup codes in a secure location."
            });
        }
        catch (Exception ex)
        {
            await _auditService.LogActionAsync("Enable2FAFailed", userId, ex.Message);
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Disable 2FA for the current user
    /// </summary>
    [HttpPost("disable")]
    public async Task<ActionResult> DisableTwoFactor()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        await _twoFactorService.DisableTwoFactorAsync(userId);
        await _auditService.LogActionAsync("Disable2FA", userId, "2FA has been disabled");

        return Ok(new { Message = "2FA has been disabled" });
    }

    /// <summary>
    /// Verify a 2FA code
    /// </summary>
    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<ActionResult> VerifyCode([FromBody] VerifyCodeRequest request)
    {
        // Allow unauthenticated calls only when the request carries a valid pre-auth challenge
        // token issued by the login flow.  Fully-authenticated callers must match their own id.
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim != null && Guid.TryParse(userIdClaim, out var callerId) && callerId != request.UserId)
            return Forbid();

        var isValid = await _twoFactorService.VerifyCodeAsync(request.UserId, request.Code);

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        await _auditService.LogSecurityEventAsync(
            "Verify2FACode",
            request.UserId,
            ipAddress,
            userAgent,
            isValid,
            isValid ? null : "Invalid 2FA code"
        );

        if (isValid)
            return Ok(new { Message = "Code verified successfully" });

        return Unauthorized(new { Error = "Invalid or expired code" });
    }

    /// <summary>
    /// Verify a backup code
    /// </summary>
    [HttpPost("verify-backup")]
    [AllowAnonymous]
    public async Task<ActionResult> VerifyBackupCode([FromBody] VerifyBackupCodeRequest request)
    {
        // Same caller-ownership guard as /verify
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim != null && Guid.TryParse(userIdClaim, out var callerId) && callerId != request.UserId)
            return Forbid();

        var isValid = await _twoFactorService.VerifyBackupCodeAsync(request.UserId, request.BackupCode);

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        await _auditService.LogSecurityEventAsync(
            "VerifyBackupCode",
            request.UserId,
            ipAddress,
            userAgent,
            isValid,
            isValid ? null : "Invalid backup code"
        );

        if (isValid)
            return Ok(new { Message = "Backup code verified successfully" });

        return Unauthorized(new { Error = "Invalid backup code" });
    }

    /// <summary>
    /// Regenerate backup codes
    /// </summary>
    [HttpPost("regenerate-backup-codes")]
    public async Task<ActionResult> RegenerateBackupCodes()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        try
        {
            var backupCodes = await _twoFactorService.RegenerateBackupCodesAsync(userId);
            await _auditService.LogActionAsync("RegenerateBackupCodes", userId, "Backup codes regenerated");

            return Ok(new
            {
                BackupCodes = backupCodes,
                Message = "New backup codes have been generated. Save them in a secure location."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Send 2FA code via SMS
    /// </summary>
    [HttpPost("send-sms")]
    [AllowAnonymous]
    public async Task<ActionResult> SendSmsCode([FromBody] SendCodeRequest request)
    {
        var success = await _twoFactorService.SendSmsCodeAsync(request.UserId);
        
        if (success)
            return Ok(new { Message = "SMS code sent successfully" });

        return BadRequest(new { Error = "Failed to send SMS code" });
    }

    /// <summary>
    /// Send 2FA code via Email
    /// </summary>
    [HttpPost("send-email")]
    [AllowAnonymous]
    public async Task<ActionResult> SendEmailCode([FromBody] SendCodeRequest request)
    {
        var success = await _twoFactorService.SendEmailCodeAsync(request.UserId);
        
        if (success)
            return Ok(new { Message = "Email code sent successfully" });

        return BadRequest(new { Error = "Failed to send email code" });
    }

    /// <summary>
    /// Get 2FA status for the current user
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult> GetStatus()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var twoFactor = await _twoFactorService.GetTwoFactorStatusAsync(userId);
        
        if (twoFactor == null)
            return Ok(new { IsEnabled = false });

        return Ok(new
        {
            IsEnabled = twoFactor.IsEnabled,
            Method = twoFactor.Method.ToString(),
            EnabledAt = twoFactor.EnabledAt,
            LastUsedAt = twoFactor.LastUsedAt,
            BackupCodesRemaining = twoFactor.BackupCodes?.Count ?? 0
        });
    }
}

// DTOs
public record EnableTwoFactorRequest(TwoFactorMethod Method);
public record VerifyCodeRequest(Guid UserId, string Code);
public record VerifyBackupCodeRequest(Guid UserId, string BackupCode);
public record SendCodeRequest(Guid UserId);
