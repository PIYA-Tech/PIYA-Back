using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Webhook service implementation
/// </summary>
public class WebhookService(HttpClient httpClient, ILogger<WebhookService> logger) : IWebhookService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<WebhookService> _logger = logger;
    
    // In-memory storage - in production, use database
    private static readonly List<WebhookSubscription> _subscriptions = [];
    private static readonly List<WebhookDelivery> _deliveries = [];

    public Task<Guid> RegisterWebhookAsync(string url, List<WebhookEventType> events, string? secret = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Webhook URL is required", nameof(url));

        if (events == null || events.Count == 0)
            throw new ArgumentException("At least one event type is required", nameof(events));

        var webhook = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            Url = url,
            Events = events,
            Secret = secret ?? GenerateSecret(),
            IsActive = true,
            RetryCount = 3,
            TimeoutSeconds = 30,
            CreatedAt = DateTime.UtcNow
        };

        _subscriptions.Add(webhook);
        
        _logger.LogInformation("Registered webhook {WebhookId} for {Url} with {EventCount} events", 
            webhook.Id, url, events.Count);

        return Task.FromResult(webhook.Id);
    }

    public Task<bool> UnregisterWebhookAsync(Guid webhookId)
    {
        var webhook = _subscriptions.FirstOrDefault(w => w.Id == webhookId);
        if (webhook != null)
        {
            webhook.IsActive = false;
            _logger.LogInformation("Unregistered webhook {WebhookId}", webhookId);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<List<WebhookSubscription>> GetWebhooksForEventAsync(WebhookEventType eventType)
    {
        var webhooks = _subscriptions
            .Where(w => w.IsActive && w.Events.Contains(eventType))
            .ToList();

        return Task.FromResult(webhooks);
    }

    public async Task<bool> SendWebhookAsync(Guid webhookId, WebhookEventType eventType, object payload)
    {
        var webhook = _subscriptions.FirstOrDefault(w => w.Id == webhookId && w.IsActive);
        if (webhook == null)
        {
            _logger.LogWarning("Webhook {WebhookId} not found or inactive", webhookId);
            return false;
        }

        if (!webhook.Events.Contains(eventType))
        {
            _logger.LogWarning("Webhook {WebhookId} is not subscribed to event {EventType}", 
                webhookId, eventType);
            return false;
        }

        return await DeliverWebhookAsync(webhook, eventType, payload, 1);
    }

    public async Task SendWebhookToAllSubscribersAsync(WebhookEventType eventType, object payload)
    {
        var webhooks = await GetWebhooksForEventAsync(eventType);
        
        _logger.LogInformation("Sending webhook event {EventType} to {Count} subscribers", 
            eventType, webhooks.Count);

        var tasks = webhooks.Select(webhook => DeliverWebhookAsync(webhook, eventType, payload, 1));
        await Task.WhenAll(tasks);
    }

    public Task<List<WebhookDelivery>> GetDeliveryHistoryAsync(Guid webhookId, int count = 50)
    {
        var deliveries = _deliveries
            .Where(d => d.WebhookId == webhookId)
            .OrderByDescending(d => d.DeliveredAt)
            .Take(count)
            .ToList();

        return Task.FromResult(deliveries);
    }

    public async Task<bool> RetryDeliveryAsync(Guid deliveryId)
    {
        var delivery = _deliveries.FirstOrDefault(d => d.Id == deliveryId);
        if (delivery == null)
        {
            _logger.LogWarning("Delivery {DeliveryId} not found", deliveryId);
            return false;
        }

        var webhook = _subscriptions.FirstOrDefault(w => w.Id == delivery.WebhookId && w.IsActive);
        if (webhook == null)
        {
            _logger.LogWarning("Webhook {WebhookId} not found or inactive for delivery retry", delivery.WebhookId);
            return false;
        }

        var payload = JsonSerializer.Deserialize<object>(delivery.Payload);
        if (payload == null)
        {
            _logger.LogError("Failed to deserialize payload for delivery {DeliveryId}", deliveryId);
            return false;
        }

        return await DeliverWebhookAsync(webhook, delivery.EventType, payload, delivery.AttemptNumber + 1);
    }

    private async Task<bool> DeliverWebhookAsync(
        WebhookSubscription webhook, 
        WebhookEventType eventType, 
        object payload, 
        int attemptNumber)
    {
        var delivery = new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            WebhookId = webhook.Id,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            AttemptNumber = attemptNumber,
            DeliveredAt = DateTime.UtcNow
        };

        try
        {
            var webhookPayload = new
            {
                @event = eventType.ToString(),
                timestamp = DateTime.UtcNow.ToString("o"),
                data = payload
            };

            var json = JsonSerializer.Serialize(webhookPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Add signature header if secret is configured
            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                var signature = GenerateSignature(json, webhook.Secret);
                content.Headers.Add("X-Webhook-Signature", signature);
            }

            // Set timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(webhook.TimeoutSeconds));
            
            var response = await _httpClient.PostAsync(webhook.Url, content, cts.Token);
            
            delivery.StatusCode = (int)response.StatusCode;
            delivery.Response = await response.Content.ReadAsStringAsync();
            delivery.Success = response.IsSuccessStatusCode;

            if (delivery.Success)
            {
                _logger.LogInformation(
                    "Webhook {WebhookId} delivered successfully to {Url} for event {EventType} (attempt {Attempt})",
                    webhook.Id, webhook.Url, eventType, attemptNumber);
            }
            else
            {
                _logger.LogWarning(
                    "Webhook {WebhookId} delivery failed to {Url} for event {EventType} with status {StatusCode} (attempt {Attempt})",
                    webhook.Id, webhook.Url, eventType, delivery.StatusCode, attemptNumber);

                // Retry if attempts remaining
                if (attemptNumber < webhook.RetryCount)
                {
                    _ = Task.Run(async () =>
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attemptNumber)); // Exponential backoff
                        await Task.Delay(delay);
                        await DeliverWebhookAsync(webhook, eventType, payload, attemptNumber + 1);
                    });
                }
            }
        }
        catch (TaskCanceledException)
        {
            delivery.StatusCode = 408; // Request Timeout
            delivery.Response = "Request timed out";
            delivery.Success = false;
            
            _logger.LogError("Webhook {WebhookId} delivery to {Url} timed out (attempt {Attempt})",
                webhook.Id, webhook.Url, attemptNumber);

            // Retry if attempts remaining
            if (attemptNumber < webhook.RetryCount)
            {
                _ = Task.Run(async () =>
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attemptNumber));
                    await Task.Delay(delay);
                    await DeliverWebhookAsync(webhook, eventType, payload, attemptNumber + 1);
                });
            }
        }
        catch (Exception ex)
        {
            delivery.StatusCode = 500;
            delivery.Response = ex.Message;
            delivery.Success = false;
            
            _logger.LogError(ex, "Webhook {WebhookId} delivery to {Url} failed with exception (attempt {Attempt})",
                webhook.Id, webhook.Url, attemptNumber);

            // Retry if attempts remaining
            if (attemptNumber < webhook.RetryCount)
            {
                _ = Task.Run(async () =>
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attemptNumber));
                    await Task.Delay(delay);
                    await DeliverWebhookAsync(webhook, eventType, payload, attemptNumber + 1);
                });
            }
        }

        _deliveries.Add(delivery);
        return delivery.Success;
    }

    private static string GenerateSecret()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string GenerateSignature(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
