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

    private Guid? CurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>
    /// Register a device token for push notifications.
    /// Also marks the device as trusted for 2FA if the user has no trusted device yet.
    /// </summary>
    [HttpPost("register-device")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceRequest request)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var success = await _fcmService.RegisterDeviceTokenAsync(
            userId.Value,
            request.DeviceToken,
            request.Platform,
            request.DeviceModel,
            request.AppVersion,
            request.DeviceName);

        return success
            ? Ok(new { message = "Device registered successfully" })
            : BadRequest(new { message = "Failed to register device" });
    }

    /// <summary>
    /// Unregister a device token (soft-delete, marks inactive)
    /// </summary>
    [HttpPost("unregister-device")]
    public async Task<IActionResult> UnregisterDevice([FromBody] UnregisterDeviceRequest request)
    {
        var success = await _fcmService.UnregisterDeviceTokenAsync(request.DeviceToken);
        return success
            ? Ok(new { message = "Device unregistered successfully" })
            : NotFound(new { message = "Device token not found" });
    }

    /// <summary>
    /// List all active devices for the current user.
    /// Used to power the "Active Devices" screen in the mobile app.
    /// </summary>
    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var devices = await _fcmService.GetUserDevicesAsync(userId.Value);

        var result = devices.Select(d => new
        {
            id             = d.Id,
            deviceName     = d.DeviceName ?? d.DeviceModel ?? "Unknown Device",
            platform       = d.Platform,
            appVersion     = d.AppVersion,
            isTrusted2FA   = d.IsTrusted2FADevice,
            lastLoginAt    = d.LastLoginAt,
            lastUsedAt     = d.LastUsedAt,
            registeredAt   = d.CreatedAt
        });

        return Ok(result);
    }

    /// <summary>
    /// Remove (revoke) a device by ID. The device is deactivated and untrusted for 2FA.
    /// </summary>
    [HttpDelete("devices/{id:guid}")]
    public async Task<IActionResult> RemoveDevice(Guid id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var success = await _fcmService.RemoveDeviceAsync(userId.Value, id);
        return success
            ? Ok(new { message = "Device removed successfully" })
            : NotFound(new { message = "Device not found or does not belong to you" });
    }

    /// <summary>
    /// Mark a specific device as the trusted 2FA push-notification device.
    /// All other devices are untrusted. Login 2FA challenges will now be sent
    /// as a push notification to this device.
    /// </summary>
    [HttpPost("devices/{id:guid}/trust-2fa")]
    public async Task<IActionResult> TrustDeviceFor2FA(Guid id)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var success = await _fcmService.TrustDeviceFor2FAAsync(userId.Value, id);
        return success
            ? Ok(new { message = "Device is now your trusted 2FA device. Future logins will send a push notification here." })
            : NotFound(new { message = "Device not found or does not belong to you" });
    }

    /// <summary>
    /// Send a test push notification to the current user
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTestNotification()
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var count = await _fcmService.SendToUserAsync(
            userId.Value,
            "Test Notification",
            "This is a test push notification from PIYA Healthcare",
            new Dictionary<string, string>
            {
                { "type", "test" },
                { "timestamp", DateTime.UtcNow.ToString("O") }
            });

        return count > 0
            ? Ok(new { message = $"Test notification sent to {count} device(s)" })
            : NotFound(new { message = "No active device tokens found for user" });
    }
}

public record RegisterDeviceRequest(
    string DeviceToken,
    string Platform,
    string? DeviceModel = null,
    string? AppVersion = null,
    string? DeviceName = null);

public record UnregisterDeviceRequest(string DeviceToken);
