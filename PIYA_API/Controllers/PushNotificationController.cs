using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PushNotificationController(IFcmService fcmService) : ControllerBase
{
    private readonly IFcmService _fcmService = fcmService;

    /// <summary>
    /// Register a device token for push notifications
    /// </summary>
    [HttpPost("register-device")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceRequest request)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            var success = await _fcmService.RegisterDeviceTokenAsync(
                userId, 
                request.DeviceToken, 
                request.Platform,
                request.DeviceModel,
                request.AppVersion);

            if (success)
            {
                return Ok(new { message = "Device registered successfully for push notifications" });
            }

            return BadRequest(new { message = "Failed to register device" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while registering device", error = ex.Message });
        }
    }

    /// <summary>
    /// Unregister a device token
    /// </summary>
    [HttpPost("unregister-device")]
    public async Task<IActionResult> UnregisterDevice([FromBody] UnregisterDeviceRequest request)
    {
        try
        {
            var success = await _fcmService.UnregisterDeviceTokenAsync(request.DeviceToken);

            if (success)
            {
                return Ok(new { message = "Device unregistered successfully" });
            }

            return NotFound(new { message = "Device token not found" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while unregistering device", error = ex.Message });
        }
    }

    /// <summary>
    /// Send a test push notification to the current user
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTestNotification()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            var count = await _fcmService.SendToUserAsync(
                userId,
                "Test Notification",
                "This is a test push notification from PIYA Healthcare",
                new Dictionary<string, string>
                {
                    { "type", "test" },
                    { "timestamp", DateTime.UtcNow.ToString("O") }
                });

            if (count > 0)
            {
                return Ok(new { message = $"Test notification sent to {count} device(s)" });
            }

            return NotFound(new { message = "No active device tokens found for user" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while sending notification", error = ex.Message });
        }
    }
}

public record RegisterDeviceRequest(
    string DeviceToken,
    string Platform,
    string? DeviceModel = null,
    string? AppVersion = null);

public record UnregisterDeviceRequest(string DeviceToken);
