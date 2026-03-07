using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PasswordResetController(
    IPasswordResetService passwordResetService,
    ILogger<PasswordResetController> logger) : ControllerBase
{
    private readonly IPasswordResetService _passwordResetService = passwordResetService;
    private readonly ILogger<PasswordResetController> _logger = logger;

    /// <summary>
    /// Request password reset (sends email)
    /// </summary>
    [HttpPost("request")]
    [AllowAnonymous]
    public async Task<ActionResult> RequestPasswordReset([FromBody] RequestPasswordResetRequest request)
    {
        try
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var userAgent = Request.Headers["User-Agent"].ToString();

            await _passwordResetService.GenerateResetTokenAsync(request.Email, ipAddress, userAgent);

            // Always return success to prevent email enumeration
            return Ok(new { message = "If an account with that email exists, a password reset link has been sent" });
        }
        catch (KeyNotFoundException)
        {
            // Still return success message for security
            return Ok(new { message = "If an account with that email exists, a password reset link has been sent" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting password reset");
            return StatusCode(500, new { error = "Failed to process password reset request" });
        }
    }

    /// <summary>
    /// Validate password reset token
    /// </summary>
    [HttpPost("validate-token")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> ValidateToken([FromBody] ValidateResetTokenRequest request)
    {
        try
        {
            var isValid = await _passwordResetService.ValidateResetTokenAsync(request.Token);

            return Ok(new { isValid });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating reset token");
            return StatusCode(500, new { error = "Failed to validate token" });
        }
    }

    /// <summary>
    /// Reset password using token
    /// </summary>
    [HttpPost("reset")]
    [AllowAnonymous]
    public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        try
        {
            if (request.NewPassword.Length < 8)
            {
                return BadRequest(new { error = "Password must be at least 8 characters long" });
            }

            if (!request.NewPassword.Any(char.IsDigit))
            {
                return BadRequest(new { error = "Password must contain at least one digit" });
            }

            if (request.NewPassword.All(char.IsLetterOrDigit))
            {
                return BadRequest(new { error = "Password must contain at least one special character" });
            }

            if (request.NewPassword != request.ConfirmPassword)
            {
                return BadRequest(new { error = "Passwords do not match" });
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            var success = await _passwordResetService.ResetPasswordAsync(request.Token, request.NewPassword, ipAddress);

            if (!success)
            {
                return BadRequest(new { error = "Invalid or expired reset token" });
            }

            return Ok(new { message = "Password reset successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting password");
            return StatusCode(500, new { error = "Failed to reset password" });
        }
    }

    /// <summary>
    /// Revoke password reset token
    /// </summary>
    [HttpPost("revoke")]
    [AllowAnonymous]
    public async Task<ActionResult> RevokeToken([FromBody] RevokeTokenRequest request)
    {
        try
        {
            await _passwordResetService.RevokeTokenAsync(request.Token);

            return Ok(new { message = "Token revoked successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking token");
            return StatusCode(500, new { error = "Failed to revoke token" });
        }
    }
}

public record RequestPasswordResetRequest(string Email);

public record ValidateResetTokenRequest(string Token);

public record ResetPasswordRequest(string Token, string NewPassword, string ConfirmPassword);

public record RevokeTokenRequest(string Token);
