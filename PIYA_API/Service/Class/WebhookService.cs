using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>Database-backed webhook subscriptions and durable retry queue.</summary>
public class WebhookService(
    HttpClient httpClient,
    PharmacyApiDbContext context,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<WebhookService> logger) : IWebhookService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly PharmacyApiDbContext _context = context;
    private readonly IDataProtector _secretProtector =
        dataProtectionProvider.CreateProtector("PIYA.WebhookSecrets.v1");
    private readonly ILogger<WebhookService> _logger = logger;

    public async Task<Guid> RegisterWebhookAsync(
        string url,
        List<WebhookEventType> events,
        string? secret = null)
    {
        await WebhookDestinationPolicy.ValidateAsync(url);
        if (events == null || events.Count == 0)
            throw new ArgumentException("At least one event type is required", nameof(events));

        var webhook = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            Url = url.Trim(),
            Events = events.Distinct().ToList(),
            Secret = _secretProtector.Protect(secret ?? GenerateSecret()),
            IsActive = true,
            RetryCount = 3,
            TimeoutSeconds = 30,
            CreatedAt = DateTime.UtcNow,
        };
        _context.WebhookSubscriptions.Add(webhook);
        await _context.SaveChangesAsync();
        _logger.LogInformation(
            "Registered webhook {WebhookId} with {EventCount} events",
            webhook.Id, webhook.Events.Count);
        return webhook.Id;
    }

    public async Task<bool> UnregisterWebhookAsync(Guid webhookId)
    {
        var webhook = await _context.WebhookSubscriptions.FindAsync(webhookId);
        if (webhook == null) return false;
        webhook.IsActive = false;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<WebhookSubscription>> GetWebhooksForEventAsync(WebhookEventType eventType)
    {
        var active = await _context.WebhookSubscriptions
            .AsNoTracking()
            .Where(item => item.IsActive)
            .ToListAsync();
        return active.Where(item => item.Events.Contains(eventType)).ToList();
    }

    public async Task<bool> SendWebhookAsync(
        Guid webhookId,
        WebhookEventType eventType,
        object payload)
    {
        var webhook = await _context.WebhookSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == webhookId && item.IsActive);
        if (webhook == null || !webhook.Events.Contains(eventType)) return false;

        await EnqueueAsync(webhook.Id, eventType, payload);
        return true;
    }

    public async Task SendWebhookToAllSubscribersAsync(WebhookEventType eventType, object payload)
    {
        var webhooks = await GetWebhooksForEventAsync(eventType);
        var serializedPayload = JsonSerializer.Serialize(payload);
        if (Encoding.UTF8.GetByteCount(serializedPayload) > 256 * 1024)
            throw new InvalidOperationException("Webhook payload exceeds the 256 KB limit.");
        var now = DateTime.UtcNow;
        _context.WebhookDeliveries.AddRange(webhooks.Select(webhook => new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            WebhookId = webhook.Id,
            EventType = eventType,
            Payload = serializedPayload,
            CreatedAt = now,
            NextAttemptAt = now,
            DeliveredAt = now,
        }));
        await _context.SaveChangesAsync();
    }

    public Task<List<WebhookDelivery>> GetDeliveryHistoryAsync(Guid webhookId, int count = 50) =>
        _context.WebhookDeliveries
            .AsNoTracking()
            .Where(item => item.WebhookId == webhookId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Clamp(count, 1, 200))
            .ToListAsync();

    public async Task<bool> RetryDeliveryAsync(Guid deliveryId)
    {
        var delivery = await _context.WebhookDeliveries.FindAsync(deliveryId);
        if (delivery == null) return false;
        var active = await _context.WebhookSubscriptions
            .AnyAsync(item => item.Id == delivery.WebhookId && item.IsActive);
        if (!active) return false;

        delivery.Success = false;
        delivery.CompletedAt = null;
        delivery.LockedUntil = null;
        delivery.NextAttemptAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<int> ProcessPendingDeliveriesAsync(
        int batchSize = 25,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var candidateIds = await _context.WebhookDeliveries
            .AsNoTracking()
            .Where(item => !item.Success && item.CompletedAt == null &&
                           item.NextAttemptAt <= now &&
                           (item.LockedUntil == null || item.LockedUntil < now))
            .OrderBy(item => item.NextAttemptAt)
            .Select(item => item.Id)
            .Take(Math.Clamp(batchSize, 1, 100))
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var id in candidateIds)
        {
            var lockedUntil = DateTime.UtcNow.AddMinutes(2);
            var claimed = await _context.WebhookDeliveries
                .Where(item => item.Id == id && !item.Success && item.CompletedAt == null &&
                               (item.LockedUntil == null || item.LockedUntil < DateTime.UtcNow))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.LockedUntil, lockedUntil),
                    cancellationToken);
            if (claimed == 0) continue;

            var delivery = await _context.WebhookDeliveries.FindAsync([id], cancellationToken);
            var webhook = delivery == null
                ? null
                : await _context.WebhookSubscriptions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == delivery.WebhookId && item.IsActive, cancellationToken);
            if (delivery == null) continue;
            if (webhook == null)
            {
                delivery.Response = "Webhook subscription is inactive";
                delivery.CompletedAt = DateTime.UtcNow;
                delivery.LockedUntil = null;
                await _context.SaveChangesAsync(cancellationToken);
                continue;
            }

            await DeliverAsync(delivery, webhook, cancellationToken);
            processed++;
        }

        return processed;
    }

    private async Task EnqueueAsync(Guid webhookId, WebhookEventType eventType, object payload)
    {
        var now = DateTime.UtcNow;
        var serializedPayload = JsonSerializer.Serialize(payload);
        if (Encoding.UTF8.GetByteCount(serializedPayload) > 256 * 1024)
            throw new InvalidOperationException("Webhook payload exceeds the 256 KB limit.");
        _context.WebhookDeliveries.Add(new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            WebhookId = webhookId,
            EventType = eventType,
            Payload = serializedPayload,
            CreatedAt = now,
            NextAttemptAt = now,
            DeliveredAt = now,
        });
        await _context.SaveChangesAsync();
    }

    private async Task DeliverAsync(
        WebhookDelivery delivery,
        WebhookSubscription webhook,
        CancellationToken cancellationToken)
    {
        delivery.AttemptNumber++;
        delivery.DeliveredAt = DateTime.UtcNow;
        try
        {
            await WebhookDestinationPolicy.ValidateAsync(webhook.Url);
            using var payloadDocument = JsonDocument.Parse(delivery.Payload);
            var body = JsonSerializer.Serialize(new
            {
                id = delivery.Id,
                @event = delivery.EventType.ToString(),
                timestamp = delivery.CreatedAt,
                data = payloadDocument.RootElement,
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                var secret = _secretProtector.Unprotect(webhook.Secret);
                request.Headers.Add("X-Webhook-Signature", GenerateSignature(body, secret));
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(webhook.TimeoutSeconds, 1, 60)));
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            delivery.StatusCode = (int)response.StatusCode;
            delivery.Response = Truncate(await response.Content.ReadAsStringAsync(timeout.Token));
            delivery.Success = response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            delivery.StatusCode = 408;
            delivery.Response = "Request timed out";
        }
        catch (Exception ex)
        {
            delivery.StatusCode = 0;
            delivery.Response = $"Delivery failed ({ex.GetType().Name})";
            _logger.LogWarning(ex, "Webhook delivery {DeliveryId} failed", delivery.Id);
        }

        if (delivery.Success || delivery.AttemptNumber >= Math.Clamp(webhook.RetryCount, 1, 10))
            delivery.CompletedAt = DateTime.UtcNow;
        else
            delivery.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Pow(2, delivery.AttemptNumber));
        delivery.LockedUntil = null;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string GenerateSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string? Truncate(string? value) =>
        value is { Length: > 4096 } ? value[..4096] : value;
}

/// <summary>
/// Validates webhook destinations both at registration and at the actual socket
/// connection. The connect-time check pins the approved address and closes the
/// DNS-rebinding gap between a lookup and HttpClient's connection.
/// </summary>
internal static class WebhookDestinationPolicy
{
    public static async Task ValidateAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            string.IsNullOrWhiteSpace(uri.DnsSafeHost))
            throw new ArgumentException("Webhook URL must be an HTTPS URL on port 443", nameof(url));

        await ResolveAllowedAddressesAsync(uri.DnsSafeHost, cancellationToken);
    }

    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.DnsEndPoint.Port != 443)
            throw new HttpRequestException("Webhook connections are restricted to port 443.");

        var addresses = await ResolveAllowedAddressesAsync(
            context.DnsEndPoint.Host, cancellationToken);
        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, 443), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                lastError = ex;
                socket.Dispose();
                if (ex is OperationCanceledException) throw;
            }
        }

        throw new HttpRequestException("Unable to connect to the approved webhook destination.", lastError);
    }

    private static async Task<IPAddress[]> ResolveAllowedAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
            throw new ArgumentException("Webhook URL resolves to a private or reserved address", nameof(host));
        return addresses;
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
            address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any))
            return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var ipv6Bytes = address.GetAddressBytes();
            // Unique-local fc00::/7 and documentation 2001:db8::/32.
            return (ipv6Bytes[0] & 0xfe) == 0xfc ||
                   ipv6Bytes[0] == 0x20 && ipv6Bytes[1] == 0x01 &&
                   ipv6Bytes[2] == 0x0d && ipv6Bytes[3] == 0xb8;
        }
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] is 0 or 10 or 127 ||
               bytes[0] == 169 && bytes[1] == 254 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
               bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2 ||
               bytes[0] == 198 && bytes[1] is 18 or 19 ||
               bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
               bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113 ||
               bytes[0] >= 224;
    }
}
