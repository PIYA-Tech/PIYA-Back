using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmailVerificationController(
    IEmailVerificationService emailVerificationService,
    ILogger<EmailVerificationController> logger) : ControllerBase
{
    private readonly IEmailVerificationService _emailVerificationService = emailVerificationService;
    private readonly ILogger<EmailVerificationController> _logger = logger;

    /// <summary>
    /// Verify email with token
    /// </summary>
    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<ActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        try
        {
            var success = await _emailVerificationService.VerifyEmailAsync(request.Token);

            if (!success)
            {
                return BadRequest(new { error = "Invalid or expired verification token" });
            }

            return Ok(new { message = "Email verified successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying email");
            return StatusCode(500, new { error = "Failed to verify email" });
        }
    }

    /// <summary>
    /// Resend verification email (authenticated users only)
    /// </summary>
    [HttpPost("resend")]
    [Authorize]
    public async Task<ActionResult> ResendVerification()
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            
            await _emailVerificationService.ResendVerificationEmailAsync(userId);
            
            return Ok(new { message = "Verification email sent successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending verification email");
            return StatusCode(500, new { error = "Failed to resend verification email" });
        }
    }

    /// <summary>
    /// Check if current user's email is verified
    /// </summary>
    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<object>> GetVerificationStatus()
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            
            var isVerified = await _emailVerificationService.IsEmailVerifiedAsync(userId);
            
            return Ok(new { isEmailVerified = isVerified });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking verification status");
            return StatusCode(500, new { error = "Failed to check verification status" });
        }
    }
}

public record VerifyEmailRequest(string Token);
