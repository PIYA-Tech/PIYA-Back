using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class TwoFactorAuthService(PharmacyApiDbContext context, IPasswordHasher passwordHasher, ILogger<TwoFactorAuthService> logger) : ITwoFactorAuthService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;
    private readonly ILogger<TwoFactorAuthService> _logger = logger;
    private readonly Dictionary<Guid, (string Code, DateTime ExpiresAt)> _tempCodes = new();

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
                SecretKey = secretKey,
                BackupCodes = hashedBackupCodes,
                Method = method,
                IsEnabled = true,
                EnabledAt = DateTime.UtcNow
            };
            _context.TwoFactorAuths.Add(user.TwoFactorAuth);
        }
        else
        {
            user.TwoFactorAuth.SecretKey = secretKey;
            user.TwoFactorAuth.BackupCodes = hashedBackupCodes;
            user.TwoFactorAuth.Method = method;
            user.TwoFactorAuth.IsEnabled = true;
            user.TwoFactorAuth.EnabledAt = DateTime.UtcNow;
            user.TwoFactorAuth.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

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
        }
    }

    public async Task<bool> VerifyCodeAsync(Guid userId, string code)
    {
        var twoFactor = await _context.TwoFactorAuths.FirstOrDefaultAsync(t => t.UserId == userId && t.IsEnabled);
        if (twoFactor == null)
            return false;

        // Check if user is locked out
        if (twoFactor.LockedOutUntil.HasValue && twoFactor.LockedOutUntil.Value > DateTime.UtcNow)
            return false;

        bool isValid = false;

        switch (twoFactor.Method)
        {
            case TwoFactorMethod.TOTP:
                isValid = VerifyTotpCode(twoFactor.SecretKey!, code);
                break;
            case TwoFactorMethod.SMS:
            case TwoFactorMethod.Email:
                isValid = VerifyTempCode(userId, code);
                break;
        }

        if (isValid)
        {
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
        if (user?.TwoFactorAuth == null || !user.TwoFactorAuth.IsEnabled)
            return false;

        var code = GenerateNumericCode();
        _tempCodes[userId] = (code, DateTime.UtcNow.AddMinutes(5));

        // TODO: Integrate with SMS service (Twilio, etc.)
        _logger.LogInformation("SMS 2FA code for user {UserId}: {Code}", userId, code);

        return true;
    }

    public async Task<bool> SendEmailCodeAsync(Guid userId)
    {
        var user = await _context.Users.Include(u => u.TwoFactorAuth).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.TwoFactorAuth == null || !user.TwoFactorAuth.IsEnabled)
            return false;

        var code = GenerateNumericCode();
        _tempCodes[userId] = (code, DateTime.UtcNow.AddMinutes(5));

        // TODO: Integrate with email service (SendGrid, etc.)
        _logger.LogInformation("Email 2FA code for user {UserId}: {Code}", userId, code);

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

    private string GenerateSecretKey()
    {
        // Generate a random 20-byte secret and encode as Base32
        var bytes = new byte[20];
        RandomNumberGenerator.Fill(bytes);
        return Base32Encode(bytes);
    }

    private List<string> GenerateBackupCodes(int count = 10)
    {
        var codes = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var bytes = new byte[4];
            RandomNumberGenerator.Fill(bytes);
            var code = BitConverter.ToUInt32(bytes).ToString("D8");
            codes.Add(code);
        }
        return codes;
    }

    private string GenerateNumericCode(int length = 6)
    {
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        var num = BitConverter.ToUInt32(bytes) % (int)Math.Pow(10, length);
        return num.ToString($"D{length}");
    }

    private bool VerifyTempCode(Guid userId, string code)
    {
        if (_tempCodes.TryGetValue(userId, out var tempCode))
        {
            if (tempCode.ExpiresAt > DateTime.UtcNow && tempCode.Code == code)
            {
                _tempCodes.Remove(userId);
                return true;
            }
        }
        return false;
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

    private string GenerateTotpCode(string secretKey, long timeStep)
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

    private string GenerateQrCodeUri(string email, string secretKey)
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
}
