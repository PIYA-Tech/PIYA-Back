using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Device-token management service. Push notifications via Firebase are not used;
/// this service handles device registration, 2FA trust and last-login tracking only.
/// </summary>
public class FcmService : IFcmService
{
    private readonly PharmacyApiDbContext _context;
    private readonly ILogger<FcmService> _logger;

    public FcmService(PharmacyApiDbContext context, ILogger<FcmService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // Push notification methods - no-op (Firebase not configured)

    public Task<bool> SendNotificationAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null)
    {
        _logger.LogDebug("Push notifications are disabled. Notification '{Title}' not sent", title);
        return Task.FromResult(false);
    }

    public Task<int> SendToMultipleAsync(List<string> deviceTokens, string title, string body, Dictionary<string, string>? data = null)
    {
        _logger.LogDebug("Push notifications are disabled. Multicast '{Title}' not sent", title);
        return Task.FromResult(0);
    }

    public async Task<int> SendToUserAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null)
    {
        var tokens = await GetUserDeviceTokensAsync(userId);
        _logger.LogDebug("Push notifications are disabled. Skipped {Count} device(s) for user {UserId}", tokens.Count, userId);
        return 0;
    }

    // Device token DB operations

    public async Task<bool> RegisterDeviceTokenAsync(Guid userId, string token, string platform, string? deviceModel = null, string? appVersion = null, string? deviceName = null)
    {
        var existing = await _context.DeviceTokens.FirstOrDefaultAsync(dt => dt.Token == token);

        if (existing != null)
        {
            existing.UserId      = userId;
            existing.Platform    = platform;
            existing.DeviceModel = deviceModel;
            existing.DeviceName  = deviceName ?? existing.DeviceName;
            existing.AppVersion  = appVersion;
            existing.IsActive    = true;
            existing.LastUsedAt  = DateTime.UtcNow;
        }
        else
        {
            _context.DeviceTokens.Add(new DeviceToken
            {
                Id          = Guid.NewGuid(),
                UserId      = userId,
                Token       = token,
                Platform    = platform,
                DeviceModel = deviceModel,
                DeviceName  = deviceName,
                AppVersion  = appVersion,
                IsActive    = true,
                CreatedAt   = DateTime.UtcNow,
                LastUsedAt  = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnregisterDeviceTokenAsync(string token)
    {
        var deviceToken = await _context.DeviceTokens.FirstOrDefaultAsync(dt => dt.Token == token);
        if (deviceToken == null) return false;

        deviceToken.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public Task<List<string>> GetUserDeviceTokensAsync(Guid userId) =>
        _context.DeviceTokens
            .Where(dt => dt.UserId == userId && dt.IsActive)
            .Select(dt => dt.Token)
            .ToListAsync();

    public Task<List<DeviceToken>> GetUserDevicesAsync(Guid userId) =>
        _context.DeviceTokens
            .Where(dt => dt.UserId == userId && dt.IsActive)
            .OrderByDescending(dt => dt.LastLoginAt ?? dt.CreatedAt)
            .ToListAsync();

    public async Task<bool> RemoveDeviceAsync(Guid userId, Guid deviceId)
    {
        var device = await _context.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.Id == deviceId && dt.UserId == userId);

        if (device == null) return false;

        device.IsActive           = false;
        device.IsTrusted2FADevice = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> TrustDeviceFor2FAAsync(Guid userId, Guid deviceId)
    {
        var allDevices = await _context.DeviceTokens
            .Where(dt => dt.UserId == userId && dt.IsActive)
            .ToListAsync();

        foreach (var d in allDevices)
            d.IsTrusted2FADevice = (d.Id == deviceId);

        await _context.SaveChangesAsync();
        return allDevices.Any(d => d.Id == deviceId);
    }

    public async Task UpdateDeviceLastLoginAsync(Guid userId, string fcmToken)
    {
        var device = await _context.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.UserId == userId && dt.Token == fcmToken && dt.IsActive);

        if (device != null)
        {
            device.LastLoginAt = DateTime.UtcNow;
            device.LastUsedAt  = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}
