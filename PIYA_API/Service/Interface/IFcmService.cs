using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for sending push notifications via Firebase Cloud Messaging
/// </summary>
public interface IFcmService
{
    /// <summary>
    /// Send a push notification to a specific device token
    /// </summary>
    Task<bool> SendNotificationAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null);
    
    /// <summary>
    /// Send a push notification to multiple device tokens
    /// </summary>
    Task<int> SendToMultipleAsync(List<string> deviceTokens, string title, string body, Dictionary<string, string>? data = null);
    
    /// <summary>
    /// Send a push notification to a specific user (all their devices)
    /// </summary>
    Task<int> SendToUserAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null);
    
    /// <summary>
    /// Register a device token for a user
    /// </summary>
    Task<bool> RegisterDeviceTokenAsync(Guid userId, string token, string platform, string? deviceModel = null, string? appVersion = null, string? deviceName = null);
    
    /// <summary>
    /// Unregister a device token
    /// </summary>
    Task<bool> UnregisterDeviceTokenAsync(Guid userId, string token);
    
    /// <summary>
    /// Get all active device tokens (raw strings) for a user
    /// </summary>
    Task<List<string>> GetUserDeviceTokensAsync(Guid userId);

    /// <summary>
    /// Get all active DeviceToken records for a user (full objects for the active-devices UI).
    /// </summary>
    Task<List<DeviceToken>> GetUserDevicesAsync(Guid userId);

    /// <summary>
    /// Remove a device by its ID (revoke push token and untrust for 2FA).
    /// Returns false when the device was not found or does not belong to the user.
    /// </summary>
    Task<bool> RemoveDeviceAsync(Guid userId, Guid deviceId);

    /// <summary>
    /// Mark a device as the user's trusted 2FA push-notification device
    /// and switch the user's 2FA method to PushNotification.
    /// </summary>
    Task<bool> TrustDeviceFor2FAAsync(Guid userId, Guid deviceId);

    /// <summary>
    /// Update the last-login timestamp on a device by its FCM token string.
    /// Called after a successful login so the active-devices list shows the correct time.
    /// </summary>
    Task UpdateDeviceLastLoginAsync(Guid userId, string fcmToken);

    Task<bool> UpdateNotificationPreferencesAsync(
        Guid userId, string token, bool appointments, bool prescriptions,
        bool medicationReminders, bool news);
}
