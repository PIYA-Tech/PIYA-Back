namespace PIYA_API.Model;

/// <summary>
/// Device token for push notifications (FCM) and trusted 2FA devices.
/// When IsTrusted2FADevice is true, login attempts for this user will
/// send a push-notification OTP to this device instead of using TOTP/SMS.
/// </summary>
public class DeviceToken
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// User who owns this device
    /// </summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    /// <summary>
    /// FCM device registration token
    /// </summary>
    public required string Token { get; set; }
    
    /// <summary>
    /// Device platform (iOS, Android, Web)
    /// </summary>
    public required string Platform { get; set; }
    
    /// <summary>
    /// Human-readable device name shown in the "Active Devices" list
    /// e.g. "iPhone 15 Pro", "Samsung Galaxy S24"
    /// </summary>
    public string? DeviceName { get; set; }
    
    /// <summary>
    /// Device model/name (optional)
    /// </summary>
    public string? DeviceModel { get; set; }
    
    /// <summary>
    /// App version (optional)
    /// </summary>
    public string? AppVersion { get; set; }
    
    /// <summary>
    /// Whether the token is active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When this device was registered / first logged in
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Last time the token was updated/verified
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Last successful login timestamp from this device.
    /// Used in the Active Devices list.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// When set, this device acts as the user's 2FA app:
    /// the server sends a push-notification OTP here instead of
    /// requiring TOTP / SMS on the next login.
    /// </summary>
    public bool IsTrusted2FADevice { get; set; } = false;
}
