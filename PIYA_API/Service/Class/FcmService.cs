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
    ILogger<FcmService> logger) : IFcmService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly FirebaseOptions _options = options.Value;
    private readonly ILogger<FcmService> _logger = logger;
    private static readonly SemaphoreSlim AccessTokenGate = new(1, 1);
    private static string? _cachedAccessToken;
    private static DateTimeOffset _cachedAccessTokenExpiresAt;

    public async Task<bool> SendNotificationAsync(
        string deviceToken,
        string title,
        string body,
        Dictionary<string, string>? data = null)
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
        var tokens = await GetUserDeviceTokensAsync(userId);
        return await SendToMultipleAsync(tokens, title, body, data);
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

internal sealed record FirebaseServiceAccount(
    [property: JsonPropertyName("client_email")] string ClientEmail,
    [property: JsonPropertyName("private_key")] string PrivateKey,
    [property: JsonPropertyName("token_uri")] string TokenUri);

internal sealed record GoogleTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);
