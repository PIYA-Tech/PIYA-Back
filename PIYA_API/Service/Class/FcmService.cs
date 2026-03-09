using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class FcmService : IFcmService
{
    private readonly PharmacyApiDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FcmService> _logger;
    private readonly FirebaseMessaging? _messaging;
    private readonly bool _isEnabled;

    public FcmService(PharmacyApiDbContext context, IConfiguration configuration, ILogger<FcmService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;

        // Initialize Firebase Admin SDK
        try
        {
            var credentialsPath = _configuration["Firebase:CredentialsPath"];
            
            if (!string.IsNullOrEmpty(credentialsPath) && File.Exists(credentialsPath))
            {
                if (FirebaseApp.DefaultInstance == null)
                {
                    FirebaseApp.Create(new AppOptions()
                    {
                        Credential = GoogleCredential.FromFile(credentialsPath)
                    });
                }
                
                _messaging = FirebaseMessaging.DefaultInstance;
                _isEnabled = true;
                _logger.LogInformation("Firebase Cloud Messaging initialized successfully");
            }
            else
            {
                _logger.LogWarning("Firebase credentials not found at '{Path}'. FCM is disabled", credentialsPath);
                _isEnabled = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Firebase. FCM is disabled");
            _isEnabled = false;
        }
    }

    public async Task<bool> SendNotificationAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null)
    {
        if (!_isEnabled || _messaging == null)
        {
            _logger.LogDebug("FCM is not enabled. Notification not sent");
            return false;
        }

        try
        {
            var message = new Message
            {
                Token = deviceToken,
                Notification = new Notification
                {
                    Title = title,
                    Body = body
                },
                Data = data
            };

            var response = await _messaging.SendAsync(message);
            _logger.LogInformation("FCM message sent: {Response}", response);
            
            // Update last used timestamp
            await UpdateTokenLastUsedAsync(deviceToken);
            
            return true;
        }
        catch (FirebaseMessagingException ex)
        {
            _logger.LogWarning(ex, "Failed to send FCM message to token");
            
            // If token is invalid, deactivate it
            if (ex.MessagingErrorCode == MessagingErrorCode.Unregistered || 
                ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
            {
                await DeactivateTokenAsync(deviceToken);
            }
            
            return false;
        }
    }

    public async Task<int> SendToMultipleAsync(List<string> deviceTokens, string title, string body, Dictionary<string, string>? data = null)
    {
        if (!_isEnabled || _messaging == null || deviceTokens.Count == 0)
        {
            return 0;
        }

        try
        {
            var message = new MulticastMessage
            {
                Tokens = deviceTokens,
                Notification = new Notification
                {
                    Title = title,
                    Body = body
                },
                Data = data
            };

            var response = await _messaging.SendEachForMulticastAsync(message);
            _logger.LogInformation("FCM multicast: {Success}/{Total} messages sent", response.SuccessCount, deviceTokens.Count);
            
            // Update last used timestamp for successful tokens
            foreach (var token in deviceTokens)
            {
                await UpdateTokenLastUsedAsync(token);
            }
            
            return response.SuccessCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send multicast FCM message");
            return 0;
        }
    }

    public async Task<int> SendToUserAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null)
    {
        var deviceTokens = await GetUserDeviceTokensAsync(userId);
        
        if (deviceTokens.Count == 0)
        {
            Console.WriteLine($"No active device tokens found for user {userId}");
            return 0;
        }

        return await SendToMultipleAsync(deviceTokens, title, body, data);
    }

    public async Task<bool> RegisterDeviceTokenAsync(Guid userId, string token, string platform, string? deviceModel = null, string? appVersion = null)
    {
        // Check if token already exists
        var existingToken = await _context.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.Token == token);

        if (existingToken != null)
        {
            // Update existing token
            existingToken.UserId = userId;
            existingToken.Platform = platform;
            existingToken.DeviceModel = deviceModel;
            existingToken.AppVersion = appVersion;
            existingToken.IsActive = true;
            existingToken.LastUsedAt = DateTime.UtcNow;
        }
        else
        {
            // Create new token
            var deviceToken = new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token = token,
                Platform = platform,
                DeviceModel = deviceModel,
                AppVersion = appVersion,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            };

            _context.DeviceTokens.Add(deviceToken);
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnregisterDeviceTokenAsync(string token)
    {
        var deviceToken = await _context.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.Token == token);

        if (deviceToken == null)
        {
            return false;
        }

        deviceToken.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<string>> GetUserDeviceTokensAsync(Guid userId)
    {
        return await _context.DeviceTokens
            .Where(dt => dt.UserId == userId && dt.IsActive)
            .Select(dt => dt.Token)
            .ToListAsync();
    }

    private async Task UpdateTokenLastUsedAsync(string token)
    {
        var deviceToken = await _context.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.Token == token);

        if (deviceToken != null)
        {
            deviceToken.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    private async Task DeactivateTokenAsync(string token)
    {
        var deviceToken = await _context.DeviceTokens
            .FirstOrDefaultAsync(dt => dt.Token == token);

        if (deviceToken != null)
        {
            deviceToken.IsActive = false;
            await _context.SaveChangesAsync();
            Console.WriteLine($"Deactivated invalid device token: {token}");
        }
    }
}
