using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class TwoFactorAuthService(
    PharmacyApiDbContext context,
    IPasswordHasher passwordHasher,
    IDistributedCacheWrapper cache,
    IFcmService fcmService,
    ISmsService smsService,
    IEmailService emailService,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<TwoFactorAuthService> logger) : ITwoFactorAuthService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;
    private readonly IDistributedCacheWrapper _cache = cache;
    private readonly IFcmService _fcmService = fcmService;
    private readonly ISmsService _smsService = smsService;
    private readonly IEmailService _emailService = emailService;
    private readonly IDataProtector _secretProtector =
        dataProtectionProvider.CreateProtector("PIYA.TwoFactor.TotpSecret.v1");
    private readonly ILogger<TwoFactorAuthService> _logger = logger;

    // Cache key helpers — one-time OTP codes and login challenge tokens.
    // Stored in IDistributedCache (Redis or in-memory) so multi-instance deploys share state.
    private static string OtpKey(Guid userId)       => $"2fa:otp:{userId}";
    private static string ChallengeKey(Guid userId) => $"2fa:challenge:{userId}";
    private static string DeliveryCooldownKey(Guid userId) => $"2fa:delivery-cooldown:{userId}";
    private static string SetupPendingKey(Guid userId) => $"2fa:setup-pending:{userId}";



    public async Task<(string SecretKey, string QrCodeUri, List<string> BackupCodes)> EnableTwoFactorAsync(Guid userId, TwoFactorMethod method = TwoFactorMethod.TOTP)
    {
        var user = await _context.Users.Include(u => u.TwoFactorAuth).FirstOrDefaultAsync(u => u.Id == userId) ?? throw new InvalidOperationException("User not found");

        // Generate secret key for TOTP
        var secretKey = GenerateSecretKey();
        var backupCodes = GenerateBackupCodes();
        var hashedBackupCodes = backupCodes.Select(c => _passwordHasher.HashPassword(c)).ToList();

        if (user.TwoFactorAuth == null)
        {
            user.TwoFactorAuth = new TwoFactorAuth
            {
                UserId = userId,
                SecretKey = _secretProtector.Protect(secretKey),
                BackupCodes = hashedBackupCodes,
                Method = method,
                IsEnabled = false,
                EnabledAt = null
            };
            _context.TwoFactorAuths.Add(user.TwoFactorAuth);
        }
        else
        {
            user.TwoFactorAuth.SecretKey = _secretProtector.Protect(secretKey);
            user.TwoFactorAuth.BackupCodes = hashedBackupCodes;
            user.TwoFactorAuth.Method = method;
            user.TwoFactorAuth.IsEnabled = false;
            user.TwoFactorAuth.EnabledAt = null;
            user.TwoFactorAuth.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        await _cache.SetStringAsync(SetupPendingKey(userId), "1", TimeSpan.FromMinutes(10));

        // Generate QR code URI for authenticator apps
        var qrCodeUri = GenerateQrCodeUri(user.Email, secretKey);

        return (secretKey, qrCodeUri, backupCodes);
    }

    public async Task DisableTwoFactorAsync(Guid userId)
    {
        var twoFactor = await _context.TwoFactorAuths.FirstOrDefaultAsync(t => t.UserId == userId);
        if (twoFactor != null)
        {
            twoFactor.IsEnabled = false;
            twoFactor.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _cache.RemoveAsync(SetupPendingKey(userId));
            await _cache.RemoveAsync(OtpKey(userId));
        }
    }

    public async Task<bool> VerifyCodeAsync(Guid userId, string code)
    {
        var twoFactor = await _context.TwoFactorAuths.FirstOrDefaultAsync(t => t.UserId == userId);
        if (twoFactor == null)
            return false;

        var isPendingSetup = !twoFactor.IsEnabled &&
                             await _cache.GetStringAsync(SetupPendingKey(userId)) != null;
        if (!twoFactor.IsEnabled && !isPendingSetup)
            return false;

        // Check if user is locked out
        if (twoFactor.LockedOutUntil.HasValue && twoFactor.LockedOutUntil.Value > DateTime.UtcNow)
            return false;

        bool isValid = false;

        switch (twoFactor.Method)
        {
            case TwoFactorMethod.TOTP:
                var (plainSecret, wasLegacyPlaintext) = UnprotectTotpSecret(twoFactor.SecretKey);
                isValid = plainSecret != null && VerifyTotpCode(plainSecret, code);
                if (isValid && wasLegacyPlaintext)
                    twoFactor.SecretKey = _secretProtector.Protect(plainSecret!);
                break;
            case TwoFactorMethod.SMS:
            case TwoFactorMethod.Email:
            case TwoFactorMethod.PushNotification:
                // SMS, Email and Push all store a cache-backed OTP — verify via cache lookup.
                isValid = await VerifyTempCodeAsync(userId, code);
                break;
        }

        if (isValid)
        {
            // A newly generated setup remains pending until the user proves they
            // possess the configured factor.
            if (!twoFactor.IsEnabled)
            {
                twoFactor.IsEnabled = true;
                twoFactor.EnabledAt = DateTime.UtcNow;
                await _cache.RemoveAsync(SetupPendingKey(userId));
            }
            twoFactor.LastUsedAt = DateTime.UtcNow;
            twoFactor.FailedAttempts = 0;
            twoFactor.LockedOutUntil = null;
            await _context.SaveChangesAsync();
        }
        else
        {
            twoFactor.FailedAttempts++;
            if (twoFactor.FailedAttempts >= 5)
            {
                twoFactor.LockedOutUntil = DateTime.UtcNow.AddMinutes(15);
            }
            await _context.SaveChangesAsync();
        }

        return isValid;
    }

    public async Task<bool> VerifyBackupCodeAsync(Guid userId, string backupCode)
    {
        var twoFactor = await _context.TwoFactorAuths.FirstOrDefaultAsync(t => t.UserId == userId && t.IsEnabled);
        if (twoFactor == null || twoFactor.BackupCodes == null)
            return false;

        foreach (var hashedCode in twoFactor.BackupCodes.ToList())
        {
            if (_passwordHasher.VerifyPassword(backupCode, hashedCode))
            {
                // Remove used backup code
                twoFactor.BackupCodes.Remove(hashedCode);
                twoFactor.LastUsedAt = DateTime.UtcNow;
                twoFactor.FailedAttempts = 0;
                await _context.SaveChangesAsync();
                return true;
            }
        }

        return false;
    }

    public async Task<List<string>> RegenerateBackupCodesAsync(Guid userId)
    {
        var twoFactor = await _context.TwoFactorAuths.FirstOrDefaultAsync(t => t.UserId == userId) ?? throw new InvalidOperationException("2FA not enabled for this user");
        var backupCodes = GenerateBackupCodes();
        twoFactor.BackupCodes = backupCodes.Select(c => _passwordHasher.HashPassword(c)).ToList();
        twoFactor.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return backupCodes;
    }

    public async Task<bool> SendSmsCodeAsync(Guid userId)
    {
        var user = await _context.Users.Include(u => u.TwoFactorAuth).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.TwoFactorAuth == null)
            return false;
        if (!await AcquireDeliveryCooldownAsync(userId))
            return false;

        var code = GenerateNumericCode();
        await _cache.SetStringAsync(OtpKey(userId), HashEphemeralToken(code), TimeSpan.FromMinutes(5));

        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            _logger.LogWarning("Cannot send SMS 2FA code: user {UserId} has no phone number", userId);
            return false;
        }

        var sent = await _smsService.SendVerificationCodeAsync(user.PhoneNumber, code);
        if (!sent)
            _logger.LogWarning("SMS 2FA code delivery failed for user {UserId}", userId);

        return sent;
    }

    public async Task<bool> SendEmailCodeAsync(Guid userId)
    {
        var user = await _context.Users.Include(u => u.TwoFactorAuth).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.TwoFactorAuth == null)
            return false;
        if (!await AcquireDeliveryCooldownAsync(userId))
            return false;

        var code = GenerateNumericCode();
        await _cache.SetStringAsync(OtpKey(userId), HashEphemeralToken(code), TimeSpan.FromMinutes(5));

        await _emailService.Send2FACodeAsync(user.Email, code);
        _logger.LogInformation("Email 2FA code sent for user {UserId}", userId);
        return true;
    }

    public async Task<bool> IsTwoFactorEnabledAsync(Guid userId)
    {
        var twoFactor = await _context.TwoFactorAuths.FirstOrDefaultAsync(t => t.UserId == userId);
        return twoFactor?.IsEnabled ?? false;
    }

    public async Task<TwoFactorAuth?> GetTwoFactorStatusAsync(Guid userId)
    {
        return await _context.TwoFactorAuths.FirstOrDefaultAsync(t => t.UserId == userId);
    }

    // Helper methods

    private static string GenerateSecretKey()
    {
        // Generate a random 20-byte secret and encode as Base32
        var bytes = new byte[20];
        RandomNumberGenerator.Fill(bytes);
        return Base32Encode(bytes);
    }

    private static List<string> GenerateBackupCodes(int count = 10)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        while (codes.Count < count)
        {
            // GetInt32 provides unbiased cryptographic sampling in [0, 100_000_000).
            // D8 is then an exact eight-character decimal representation, matching
            // every API validator and native client.
            codes.Add(RandomNumberGenerator
                .GetInt32(100_000_000)
                .ToString("D8", CultureInfo.InvariantCulture));
        }
        return codes.ToList();
    }

    private static string GenerateNumericCode(int length = 6)
    {
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        var num = BitConverter.ToUInt32(bytes) % (int)Math.Pow(10, length);
        return num.ToString($"D{length}");
    }

    private async Task<bool> VerifyTempCodeAsync(Guid userId, string code)
    {
        var storedHash = await _cache.GetStringAsync(OtpKey(userId));
        if (storedHash == null ||
            !FixedTimeTokenEquals(HashEphemeralToken(code), storedHash))
            return false;
        await _cache.RemoveAsync(OtpKey(userId));
        return true;
    }

    private async Task<bool> AcquireDeliveryCooldownAsync(Guid userId)
    {
        var key = DeliveryCooldownKey(userId);
        if (await _cache.GetStringAsync(key) != null)
        {
            _logger.LogWarning("2FA delivery throttled for user {UserId}", userId);
            return false;
        }

        await _cache.SetStringAsync(key, "1", TimeSpan.FromMinutes(1));
        return true;
    }

    private bool VerifyTotpCode(string secretKey, string code)
    {
        // Implement TOTP verification (RFC 6238)
        // For simplicity, using a basic time-based check
        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timeStep = unixTimestamp / 30; // 30-second time step

        // Check current time step and +/- 1 step for clock skew
        for (int i = -1; i <= 1; i++)
        {
            var generatedCode = GenerateTotpCode(secretKey, timeStep + i);
            if (generatedCode == code)
                return true;
        }

        return false;
    }

    private (string? PlainSecret, bool WasLegacyPlaintext) UnprotectTotpSecret(string? storedSecret)
    {
        if (string.IsNullOrWhiteSpace(storedSecret))
            return (null, false);

        try
        {
            return (_secretProtector.Unprotect(storedSecret), false);
        }
        catch (CryptographicException)
        {
            // Backward compatibility for records created before at-rest encryption.
            // A Base32 TOTP secret contains only A-Z and 2-7.
            if (storedSecret.All(c => c is >= 'A' and <= 'Z' or >= '2' and <= '7'))
                return (storedSecret, true);

            _logger.LogError("Unable to decrypt TOTP secret; Data Protection keys may be unavailable");
            return (null, false);
        }
    }

    private static string GenerateTotpCode(string secretKey, long timeStep)
    {
        var keyBytes = Base32Decode(secretKey);
        var timeBytes = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(timeBytes);

        using var hmac = new HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(timeBytes);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        var otp = binary % 1000000;
        return otp.ToString("D6");
    }

    private static string GenerateQrCodeUri(string email, string secretKey)
    {
        var issuer = "PIYA";
        return $"otpauth://totp/{issuer}:{email}?secret={secretKey}&issuer={issuer}";
    }

    private static string Base32Encode(byte[] data)
    {
        const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new StringBuilder();
        int buffer = data[0];
        int bitsLeft = 8;
        int index = 0;

        while (bitsLeft > 0 || index < data.Length)
        {
            if (bitsLeft < 5)
            {
                if (index < data.Length)
                {
                    buffer <<= 8;
                    buffer |= data[index++];
                    bitsLeft += 8;
                }
                else
                {
                    int pad = 5 - bitsLeft;
                    buffer <<= pad;
                    bitsLeft += pad;
                }
            }

            int value = (buffer >> (bitsLeft - 5)) & 0x1F;
            bitsLeft -= 5;
            result.Append(base32Chars[value]);
        }

        return result.ToString();
    }

    private static byte[] Base32Decode(string encoded)
    {
        const string base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        encoded = encoded.TrimEnd('=').ToUpper();
        var result = new List<byte>();
        int buffer = 0;
        int bitsLeft = 0;

        foreach (char c in encoded)
        {
            int value = base32Chars.IndexOf(c);
            if (value < 0)
                throw new ArgumentException("Invalid Base32 character");

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                result.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return result.ToArray();
    }

    /// <inheritdoc/>
    public async Task<string> IssueChallenge(Guid userId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive)
            ?? throw new InvalidOperationException("The account is unavailable.");
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        // Store a SHA-256 hash so the raw token never lives in the cache as plaintext
        var hashed = HashEphemeralToken(raw);
        await _cache.SetStringAsync(ChallengeKey(userId), $"{user.SecurityStamp:D}:{hashed}", TimeSpan.FromMinutes(5));
        return raw;
    }

    /// <inheritdoc/>
    public async Task<bool> ConsumeChallenge(Guid userId, string challengeToken)
    {
        if (!await ValidateChallenge(userId, challengeToken))
            return false;

        // Single-use: remove immediately after successful validation
        await _cache.RemoveAsync(ChallengeKey(userId));
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateChallenge(Guid userId, string challengeToken)
    {
        if (string.IsNullOrWhiteSpace(challengeToken))
            return false;

        var storedHash = await _cache.GetStringAsync(ChallengeKey(userId));
        if (storedHash == null)
            return false;
        var parts = storedHash.Split(':', 2);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var stamp)) return false;
        if (!await _context.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive && u.SecurityStamp == stamp))
            return false;
        return FixedTimeTokenEquals(HashEphemeralToken(challengeToken), parts[1]);
    }

    /// <inheritdoc/>
    public async Task<bool> HasTrusted2FADeviceAsync(Guid userId)
    {
        return await _context.DeviceTokens
            .AnyAsync(d => d.UserId == userId && d.IsActive && d.IsTrusted2FADevice);
    }

    /// <inheritdoc/>
    public async Task<bool> SendPush2FACodeAsync(Guid userId)
    {
        if (!await AcquireDeliveryCooldownAsync(userId))
            return false;

        // Generate a 6-digit OTP and store it in cache (5-minute TTL, same as other methods)
        var code = GenerateNumericCode();
        await _cache.SetStringAsync(OtpKey(userId), HashEphemeralToken(code), TimeSpan.FromMinutes(5));

        // Fetch trusted 2FA device tokens for this user
        var deviceTokens = await _context.DeviceTokens
            .Where(d => d.UserId == userId && d.IsActive && d.IsTrusted2FADevice)
            .Select(d => d.Token)
            .ToListAsync();

        if (deviceTokens.Count == 0)
        {
            _logger.LogWarning("No trusted 2FA devices found for user {UserId}", userId);
            return false;
        }

        var data = new Dictionary<string, string>
        {
            ["type"] = "2fa_code",
            ["code"] = code,
            ["expiresInSeconds"] = "300"
        };

        var sent = await _fcmService.SendToMultipleAsync(
            deviceTokens,
            "PIYA Login Verification",
            $"Your login code is: {code}\nExpires in 5 minutes. Never share this code.",
            data);

        _logger.LogInformation("Push 2FA code sent to {Count}/{Total} devices for user {UserId}",
            sent, deviceTokens.Count, userId);

        return sent > 0;
    }

    private static string HashEphemeralToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeTokenEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
