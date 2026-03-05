namespace PIYA_API.Model;

/// <summary>
/// Two-factor authentication data for users
/// </summary>
public class TwoFactorAuth
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// User ID this 2FA belongs to
    /// </summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    /// <summary>
    /// Whether 2FA is enabled for this user
    /// </summary>
    public bool IsEnabled { get; set; } = false;
    
    /// <summary>
    /// Secret key for TOTP (Time-based One-Time Password)
    /// Base32 encoded string
    /// </summary>
    public string? SecretKey { get; set; }
    
    /// <summary>
    /// Backup codes for account recovery (hashed)
    /// </summary>
    public List<string> BackupCodes { get; set; } = [];
    
    /// <summary>
    /// Phone number for SMS-based 2FA (optional)
    /// </summary>
    public string? PhoneNumber { get; set; }
    
    /// <summary>
    /// Email for email-based 2FA (optional)
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Preferred 2FA method (TOTP, SMS, Email)
    /// </summary>
    public TwoFactorMethod Method { get; set; } = TwoFactorMethod.TOTP;
    
    /// <summary>
    /// When 2FA was enabled
    /// </summary>
    public DateTime? EnabledAt { get; set; }
    
    /// <summary>
    /// Last time 2FA was used successfully
    /// </summary>
    public DateTime? LastUsedAt { get; set; }
    
    /// <summary>
    /// Number of failed 2FA attempts
    /// </summary>
    public int FailedAttempts { get; set; } = 0;
    
    /// <summary>
    /// When the user is locked out until (after too many failed attempts)
    /// </summary>
    public DateTime? LockedOutUntil { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum TwoFactorMethod
{
    TOTP = 1,    // Time-based One-Time Password (Google Authenticator, Authy)
    SMS = 2,     // SMS code
    Email = 3    // Email code
}
