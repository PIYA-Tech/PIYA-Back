using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Device-token management and Firebase Cloud Messaging HTTP v1 delivery.
/// </summary>
public class FcmService(
    PharmacyApiDbContext context,
    IHttpClientFactory httpClientFactory,
    IOptions<FirebaseOptions> options,
    IOptions<ApplePushOptions> applePushOptions,
    ILogger<FcmService> logger) : IFcmService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly FirebaseOptions _options = options.Value;
    private readonly ApplePushOptions _applePushOptions = applePushOptions.Value;
    private readonly ILogger<FcmService> _logger = logger;
    private static readonly SemaphoreSlim AccessTokenGate = new(1, 1);
    private static readonly SemaphoreSlim AppleProviderTokenGate = new(1, 1);
    private static string? _cachedAccessToken;
    private static DateTimeOffset _cachedAccessTokenExpiresAt;
    private static string? _cachedAppleProviderToken;
    private static DateTimeOffset _cachedAppleProviderTokenExpiresAt;

    public async Task<bool> SendNotificationAsync(
        string deviceToken,
        string title,
        string body,
        Dictionary<string, string>? data = null)
    {
        var platform = await _context.DeviceTokens.AsNoTracking()
            .Where(item => item.Token == deviceToken)
            .Select(item => item.Platform)
            .FirstOrDefaultAsync();
        return await SendForPlatformAsync(platform, deviceToken, title, body, data);
    }

    private async Task<bool> SendForPlatformAsync(
        string? platform,
        string deviceToken,
        string title,
        string body,
        Dictionary<string, string>? data)
    {
        if (string.Equals(platform, "ios", StringComparison.OrdinalIgnoreCase))
            return await SendAppleNotificationAsync(deviceToken, title, body, data);

        return await SendFirebaseNotificationAsync(deviceToken, title, body, data);
    }

    private async Task<bool> SendFirebaseNotificationAsync(
        string deviceToken,
        string title,
        string body,
        Dictionary<string, string>? data)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("Push notification '{Title}' was not sent because Firebase is disabled", title);
            return false;
        }

        var accessToken = await GetGoogleAccessTokenAsync();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(_options.ProjectId)}/messages:send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                token = deviceToken,
                notification = new { title, body },
                data = data ?? new Dictionary<string, string>(),
            },
        });

        var client = _httpClientFactory.CreateClient("FirebaseCloudMessaging");
        using var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode) return true;

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogWarning(
            "Firebase rejected a push notification with status {StatusCode}",
            (int)response.StatusCode);

        if ((response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound) &&
            (responseBody.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase) ||
             responseBody.Contains("registration-token-not-registered", StringComparison.OrdinalIgnoreCase)))
        {
            var storedToken = await _context.DeviceTokens.FirstOrDefaultAsync(item => item.Token == deviceToken);
            if (storedToken != null)
            {
                storedToken.IsActive = false;
                await _context.SaveChangesAsync();
            }
        }

        return false;
    }

    public async Task<int> SendToMultipleAsync(
        List<string> deviceTokens,
        string title,
        string body,
        Dictionary<string, string>? data = null)
    {
        var delivered = 0;
        foreach (var token in deviceTokens.Distinct(StringComparer.Ordinal))
        {
            if (await SendNotificationAsync(token, title, body, data)) delivered++;
        }
        return delivered;
    }

    public async Task<int> SendToUserAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null)
    {
        var devices = await _context.DeviceTokens
            .Where(item => item.UserId == userId && item.IsActive)
            .ToListAsync();
        var type = data?.GetValueOrDefault("type")?.ToLowerInvariant();
        var delivered = 0;
        foreach (var device in devices.Where(item => PreferenceAllows(item, type)))
        {
            if (await SendForPlatformAsync(device.Platform, device.Token, title, body, data))
                delivered++;
        }
        return delivered;
    }

    private static bool PreferenceAllows(DeviceToken device, string? type) => type switch
    {
        "appointment_reminder" or "appointment_update" => device.AppointmentNotificationsEnabled,
        "prescription_ready" or "prescription_update" => device.PrescriptionNotificationsEnabled,
        "medication_reminder" => device.MedicationReminderNotificationsEnabled,
        "news" or "announcement" => device.NewsNotificationsEnabled,
        _ => true,
    };

    private async Task<bool> SendAppleNotificationAsync(
        string deviceToken,
        string title,
        string body,
        Dictionary<string, string>? data)
    {
        if (!_applePushOptions.Enabled)
        {
            _logger.LogWarning("Apple push notification '{Title}' was not sent because APNs is disabled", title);
            return false;
        }

        if (string.IsNullOrWhiteSpace(deviceToken) || !deviceToken.All(Uri.IsHexDigit))
        {
            _logger.LogWarning("An invalid APNs device-token format was rejected");
            return false;
        }

        var providerToken = await GetAppleProviderTokenAsync();
        var host = _applePushOptions.UseSandbox
            ? "https://api.sandbox.push.apple.com"
            : "https://api.push.apple.com";
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{host}/3/device/{Uri.EscapeDataString(deviceToken)}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", providerToken);
        request.Headers.TryAddWithoutValidation("apns-topic", _applePushOptions.BundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");

        var payload = new Dictionary<string, object?>
        {
            ["aps"] = new
            {
                alert = new { title, body },
                sound = "default"
            }
        };
        if (data is not null)
        {
            foreach (var item in data.Where(item => item.Key != "aps"))
                payload[item.Key] = item.Value;
        }
        request.Content = JsonContent.Create(payload);

        var client = _httpClientFactory.CreateClient("ApplePushNotifications");
        using var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode) return true;

        var responseBody = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("APNs rejected a push notification with status {StatusCode}",
            (int)response.StatusCode);
        if ((response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Gone) &&
            (responseBody.Contains("BadDeviceToken", StringComparison.OrdinalIgnoreCase) ||
             responseBody.Contains("Unregistered", StringComparison.OrdinalIgnoreCase)))
        {
            await DeactivateTokenAsync(deviceToken);
        }
        return false;
    }

    private async Task<string> GetAppleProviderTokenAsync()
    {
        if (_cachedAppleProviderToken is not null &&
            _cachedAppleProviderTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return _cachedAppleProviderToken;

        await AppleProviderTokenGate.WaitAsync();
        try
        {
            if (_cachedAppleProviderToken is not null &&
                _cachedAppleProviderTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                return _cachedAppleProviderToken;

            var now = DateTimeOffset.UtcNow;
            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(
                new { alg = "ES256", kid = _applePushOptions.KeyId }));
            var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(
                new { iss = _applePushOptions.TeamId, iat = now.ToUnixTimeSeconds() }));
            var unsigned = $"{header}.{claims}";
            var privateKey = await File.ReadAllTextAsync(_applePushOptions.PrivateKeyPath);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privateKey);
            var signature = ecdsa.SignData(
                Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            _cachedAppleProviderToken = $"{unsigned}.{Base64Url(signature)}";
            _cachedAppleProviderTokenExpiresAt = now.AddMinutes(50);
            return _cachedAppleProviderToken;
        }
        finally
        {
            AppleProviderTokenGate.Release();
        }
    }

    private async Task DeactivateTokenAsync(string deviceToken)
    {
        var storedToken = await _context.DeviceTokens.FirstOrDefaultAsync(item => item.Token == deviceToken);
        if (storedToken is null) return;
        storedToken.IsActive = false;
        await _context.SaveChangesAsync();
    }

    private async Task<string> GetGoogleAccessTokenAsync()
    {
        if (_cachedAccessToken != null && _cachedAccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return _cachedAccessToken;

        await AccessTokenGate.WaitAsync();
        try
        {
            if (_cachedAccessToken != null && _cachedAccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                return _cachedAccessToken;

            if (!File.Exists(_options.CredentialsPath))
                throw new InvalidOperationException("Firebase service-account credentials are unavailable.");

            var json = await File.ReadAllTextAsync(_options.CredentialsPath);
            var credentials = JsonSerializer.Deserialize<FirebaseServiceAccount>(json)
                ?? throw new InvalidOperationException("Firebase service-account credentials are invalid.");
            if (!Uri.TryCreate(credentials.TokenUri, UriKind.Absolute, out var tokenUri) ||
                tokenUri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(tokenUri.Host, "oauth2.googleapis.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Firebase service-account token_uri is not trusted.");
            var now = DateTimeOffset.UtcNow;
            var assertion = CreateServiceAccountAssertion(credentials, now);

            var client = _httpClientFactory.CreateClient("FirebaseCloudMessaging");
            using var response = await client.PostAsync(
                credentials.TokenUri,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion,
                }));
            response.EnsureSuccessStatusCode();

            var tokenJson = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(tokenJson)
                ?? throw new InvalidOperationException("Google OAuth returned an invalid token response.");
            _cachedAccessToken = tokenResponse.AccessToken;
            _cachedAccessTokenExpiresAt = now.AddSeconds(tokenResponse.ExpiresIn);
            return _cachedAccessToken;
        }
        finally
        {
            AccessTokenGate.Release();
        }
    }

    private static string CreateServiceAccountAssertion(
        FirebaseServiceAccount credentials,
        DateTimeOffset now)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = credentials.ClientEmail,
            scope = "https://www.googleapis.com/auth/firebase.messaging",
            aud = credentials.TokenUri,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddMinutes(55).ToUnixTimeSeconds(),
        }));
        var unsigned = $"{header}.{payload}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(credentials.PrivateKey);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsigned),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return $"{unsigned}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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

    public async Task<bool> UnregisterDeviceTokenAsync(Guid userId, string token)
    {
        var deviceToken = await _context.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.UserId == userId && dt.Token == token);
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

    public async Task<bool> UpdateNotificationPreferencesAsync(
        Guid userId, string token, bool appointments, bool prescriptions,
        bool medicationReminders, bool news)
    {
        var device = await _context.DeviceTokens
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Token == token && item.IsActive);
        if (device is null) return false;
        device.AppointmentNotificationsEnabled = appointments;
        device.PrescriptionNotificationsEnabled = prescriptions;
        device.MedicationReminderNotificationsEnabled = medicationReminders;
        device.NewsNotificationsEnabled = news;
        device.LastUsedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }
}

internal sealed record FirebaseServiceAccount(
    [property: JsonPropertyName("client_email")] string ClientEmail,
    [property: JsonPropertyName("private_key")] string PrivateKey,
    [property: JsonPropertyName("token_uri")] string TokenUri);

internal sealed record GoogleTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);
