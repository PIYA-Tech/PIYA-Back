using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for Two-Factor Authentication (2FA)
/// </summary>
public interface ITwoFactorAuthService
{
    /// <summary>
    /// Enable 2FA for a user and generate a secret key
    /// </summary>
    Task<(string SecretKey, string QrCodeUri, List<string> BackupCodes)> EnableTwoFactorAsync(Guid userId, TwoFactorMethod method = TwoFactorMethod.TOTP);
    
    /// <summary>
    /// Disable 2FA for a user
    /// </summary>
    Task DisableTwoFactorAsync(Guid userId);
    
    /// <summary>
    /// Verify a 2FA code
    /// </summary>
    Task<bool> VerifyCodeAsync(Guid userId, string code);
    
    /// <summary>
    /// Verify a backup code
    /// </summary>
    Task<bool> VerifyBackupCodeAsync(Guid userId, string backupCode);
    
    /// <summary>
    /// Generate new backup codes
    /// </summary>
    Task<List<string>> RegenerateBackupCodesAsync(Guid userId);
    
    /// <summary>
    /// Send 2FA code via SMS
    /// </summary>
    Task<bool> SendSmsCodeAsync(Guid userId);
    
    /// <summary>
    /// Send 2FA code via Email
    /// </summary>
    Task<bool> SendEmailCodeAsync(Guid userId);
    
    /// <summary>
    /// Check if user has 2FA enabled
    /// </summary>
    Task<bool> IsTwoFactorEnabledAsync(Guid userId);
    
    /// <summary>
    /// Get 2FA status for a user
    /// </summary>
    Task<TwoFactorAuth?> GetTwoFactorStatusAsync(Guid userId);

    /// <summary>
    /// Issues a short-lived (5-min) challenge token that must be presented alongside the 2FA code.
    /// Prevents anonymous callers from verifying codes for arbitrary user IDs.
    /// </summary>
    Task<string> IssueChallenge(Guid userId);

    /// <summary>
    /// Returns true if <paramref name="challengeToken"/> is valid for <paramref name="userId"/>
    /// and has not yet expired, then immediately removes it (single-use).
    /// </summary>
    Task<bool> ConsumeChallenge(Guid userId, string challengeToken);

    /// <summary>
    /// Generate a 6-digit OTP and send it as a push notification to all trusted
    /// 2FA devices for <paramref name="userId"/>.
    /// Returns true when at least one device was reached.
    /// </summary>
    Task<bool> SendPush2FACodeAsync(Guid userId);

    /// <summary>
    /// Returns true when the user has at least one active trusted-2FA device
    /// registered (i.e., push-notification 2FA is available for them).
    /// </summary>
    Task<bool> HasTrusted2FADeviceAsync(Guid userId);
}
