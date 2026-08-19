namespace PIYA_API.Service.Interface;

/// <summary>
/// Webhook event types
/// </summary>
public enum WebhookEventType
{
    AppointmentCreated = 1,
    AppointmentUpdated = 2,
    AppointmentCancelled = 3,
    PrescriptionIssued = 4,
    PrescriptionFilled = 5,
    DoctorNoteCreated = 6,
    UserRegistered = 7,
    PaymentCompleted = 8,
    PaymentFailed = 9
}

/// <summary>
/// Service for managing webhooks and event notifications
/// </summary>
public interface IWebhookService
{
    /// <summary>
    /// Register a new webhook endpoint
    /// </summary>
    Task<Guid> RegisterWebhookAsync(string url, List<WebhookEventType> events, string? secret = null);
    
    /// <summary>
    /// Unregister a webhook
    /// </summary>
    Task<bool> UnregisterWebhookAsync(Guid webhookId);
    
    /// <summary>
    /// Get all webhooks for a specific event type
    /// </summary>
    Task<List<WebhookSubscription>> GetWebhooksForEventAsync(WebhookEventType eventType);
    
    /// <summary>
    /// Send webhook notification
    /// </summary>
    Task<bool> SendWebhookAsync(Guid webhookId, WebhookEventType eventType, object payload);
    
    /// <summary>
    /// Send webhook notification to all subscribers of an event
    /// </summary>
    Task SendWebhookToAllSubscribersAsync(WebhookEventType eventType, object payload);
    
    /// <summary>
    /// Get webhook delivery history
    /// </summary>
    Task<List<WebhookDelivery>> GetDeliveryHistoryAsync(Guid webhookId, int count = 50);
    
    /// <summary>
    /// Retry failed webhook delivery
    /// </summary>
    Task<bool> RetryDeliveryAsync(Guid deliveryId);

    /// <summary>Process due deliveries from the durable queue.</summary>
    Task<int> ProcessPendingDeliveriesAsync(int batchSize = 25, CancellationToken cancellationToken = default);
}

/// <summary>
/// Webhook subscription
/// </summary>
public class WebhookSubscription
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public List<WebhookEventType> Events { get; set; } = [];
    public string? Secret { get; set; }
    public bool IsActive { get; set; }
    public int RetryCount { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 30;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Webhook delivery record
/// </summary>
public class WebhookDelivery
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }
    public WebhookEventType EventType { get; set; }
    public string Payload { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? Response { get; set; }
    public bool Success { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime DeliveredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime? LockedUntil { get; set; }
}
