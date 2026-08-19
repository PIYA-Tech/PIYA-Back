using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PIYA_API.Middleware;

/// <summary>
/// Rate limiting middleware to prevent API abuse
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private static readonly ConcurrentDictionary<string, ClientRateLimitInfo> _clients = new();
    private readonly int _globalRequestLimit;
    private readonly TimeSpan _globalTimeWindow;
    private readonly List<string> _whitelistedPaths;
    private readonly IConfiguration _configuration;
    private readonly string? _bypassSecret;

    public RateLimitingMiddleware(
        RequestDelegate next,
        ILogger<RateLimitingMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
        _globalRequestLimit = int.Parse(configuration["RateLimiting:PermitLimit"] ?? "100");
        _globalTimeWindow = TimeSpan.FromSeconds(int.Parse(configuration["RateLimiting:WindowSeconds"] ?? "60"));
        _whitelistedPaths = configuration.GetSection("RateLimiting:WhitelistedPaths").Get<List<string>>() ??
        [
            "/api/Health",
            "/swagger"
        ];
        // Secret token required for bypass headers — must be set via env/config to be usable
        _bypassSecret = configuration["RateLimiting:BypassSecret"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Respect configuration: skip rate limiting when disabled (useful for tests/load runs)
        try
        {
            var enabledGlobally = _configuration.GetValue<bool?>("RateLimiting:EnableGlobal") ?? false;
            var featureFlag = _configuration.GetValue<bool?>("Features:EnableRateLimiting") ?? false;
            if (!enabledGlobally && !featureFlag)
            {
                // Rate limiting disabled via configuration
                await _next(context);
                return;
            }
        }
        catch
        {
            // if config reading fails, continue with enforcement by default
        }

        // Skip rate limiting for whitelisted paths
        if (_whitelistedPaths.Any(path => context.Request.Path.StartsWithSegments(path)))
        {
            await _next(context);
            return;
        }

        // Support a header-based bypass for test/load runners, but ONLY when the caller
        // presents the correct secret configured in RateLimiting:BypassSecret.
        // If no secret is configured, bypass headers are completely disabled.
        if (!string.IsNullOrEmpty(_bypassSecret))
        {
            if (context.Request.Headers.TryGetValue("X-RateLimit-Bypass", out var bypassValues))
            {
                var bypassVal = bypassValues.FirstOrDefault();
                if (bypassVal == _bypassSecret)
                {
                    await _next(context);
                    return;
                }
            }

            if (context.Request.Headers.TryGetValue("X-NBomber-Bypass", out var nbomberVals))
            {
                var v = nbomberVals.FirstOrDefault();
                if (v == _bypassSecret)
                {
                    await _next(context);
                    return;
                }
            }
        }

        // NBomber User-Agent bypass is intentionally removed — it can be trivially spoofed.

        var clientId = GetClientIdentifier(context);

        // Default bucket key is client id (ip or user). For login attempts we prefer per-username buckets
        var rateLimitKey = clientId;

        // If this looks like a login attempt, try to extract the username/email from the JSON body to rate-limit per account
        var isLoginAttempt = context.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
                             && string.Equals(context.Request.Method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase);

        if (isLoginAttempt &&
            context.Request.ContentLength is > 0 and <= 65_536)
        {
            try
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(body);
                        // Accept 'identifier' (mobile/API clients), 'username', or 'email'
                        string? resolvedKey = null;
                        if (doc.RootElement.TryGetProperty("identifier", out var id))
                            resolvedKey = id.GetString();
                        else if (doc.RootElement.TryGetProperty("username", out var u))
                            resolvedKey = u.GetString();
                        else if (doc.RootElement.TryGetProperty("email", out var e))
                            resolvedKey = e.GetString();

                        if (!string.IsNullOrWhiteSpace(resolvedKey))
                        {
                            rateLimitKey =
                                $"{clientId}:login:{HashLoginIdentifier(resolvedKey)}";
                        }
                    }
                    catch
                    {
                        // ignore JSON parse errors and fall back to client id
                    }
                }
            }
            catch
            {
                // ignore body read issues; fall back to client id
            }
        }

        // Determine if there's an endpoint-specific override (e.g., Authentication)
        var requestLimit = _globalRequestLimit;
        var timeWindow = _globalTimeWindow;

        try
        {
            // Apply the Authentication-specific limit only to login attempts (avoid limiting register/refresh)
            if (context.Request.Path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
                && string.Equals(context.Request.Method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
            {
                var permit = _configuration.GetValue<int?>("RateLimiting:Endpoints:Authentication:PermitLimit");
                var windowSeconds = _configuration.GetValue<int?>("RateLimiting:Endpoints:Authentication:WindowSeconds");
                if (permit.HasValue) requestLimit = permit.Value;
                if (windowSeconds.HasValue) timeWindow = TimeSpan.FromSeconds(windowSeconds.Value);
            }

            if (IsQrGenerationRequest(context.Request))
            {
                var permit = _configuration.GetValue<int?>(
                    "RateLimiting:Endpoints:QrGeneration:PermitLimit");
                var windowSeconds = _configuration.GetValue<int?>(
                    "RateLimiting:Endpoints:QrGeneration:WindowSeconds");
                if (permit.HasValue) requestLimit = permit.Value;
                if (windowSeconds.HasValue)
                    timeWindow = TimeSpan.FromSeconds(windowSeconds.Value);
                rateLimitKey = $"qrgeneration:{clientId}";
            }

            if (IsQrValidationRequest(context.Request))
            {
                var permit = _configuration.GetValue<int?>(
                    "RateLimiting:Endpoints:QrValidation:PermitLimit");
                var windowSeconds = _configuration.GetValue<int?>(
                    "RateLimiting:Endpoints:QrValidation:WindowSeconds");
                if (permit.HasValue) requestLimit = permit.Value;
                if (windowSeconds.HasValue)
                    timeWindow = TimeSpan.FromSeconds(windowSeconds.Value);
                rateLimitKey = $"qrvalidation:{clientId}";
            }

            // Apply a tighter limit to public (unauthenticated) search endpoints
            var isPublicSearch = context.User.Identity?.IsAuthenticated != true && (
                context.Request.Path.StartsWithSegments("/api/pharmacy/search") ||
                context.Request.Path.StartsWithSegments("/api/pharmacy/searchBy") ||
                context.Request.Path.StartsWithSegments("/api/hospital") ||
                context.Request.Path.StartsWithSegments("/api/medication/search"));

            if (isPublicSearch)
            {
                var permit = _configuration.GetValue<int?>("RateLimiting:Endpoints:PublicSearch:PermitLimit");
                var windowSeconds = _configuration.GetValue<int?>("RateLimiting:Endpoints:PublicSearch:WindowSeconds");
                if (permit.HasValue) requestLimit = permit.Value;
                if (windowSeconds.HasValue) timeWindow = TimeSpan.FromSeconds(windowSeconds.Value);
                // Always key by IP for anonymous callers — ignore any user id claim
                var remoteAddr = context.Connection.RemoteIpAddress;
                var ip = remoteAddr?.ToString() ?? "unknown";
                rateLimitKey = $"publicsearch:ip:{ip}";
            }
        }
        catch
        {
            // ignore and fall back to global limits
        }

        var clientInfo = _clients.GetOrAdd(rateLimitKey, _ => new ClientRateLimitInfo());
        var now = DateTime.UtcNow;

        // Determine if this is a login attempt (we only count failed logins for authentication throttling)
    // isLoginAttempt already computed above
    if (isLoginAttempt)
        {
            // Check current number of recent failed login attempts and block early if already over the limit
            var loginLimitExceeded = false;
            int loginRetryAfter = 0;
            long loginResetTime = 0;

            lock (clientInfo)
            {
                clientInfo.Requests.RemoveAll(r => now - r > timeWindow);
                if (clientInfo.Requests.Count >= requestLimit)
                {
                    var oldestRequest = clientInfo.Requests.Min();
                    loginRetryAfter = (int)(timeWindow - (now - oldestRequest)).TotalSeconds;
                    loginResetTime = DateTimeOffset.UtcNow.AddSeconds(loginRetryAfter).ToUnixTimeSeconds();
                    loginLimitExceeded = true;
                }
            }

            if (loginLimitExceeded)
            {
                _logger.LogWarning("Rate limit exceeded for client {ClientId}. Path: {Path}", clientId, context.Request.Path);
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                context.Response.Headers["Retry-After"] = loginRetryAfter.ToString();
                context.Response.Headers["X-RateLimit-Limit"] = requestLimit.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = "0";
                context.Response.Headers["X-RateLimit-Reset"] = loginResetTime.ToString();

                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit exceeded",
                    message = $"Too many requests. Please try again in {loginRetryAfter} seconds.",
                    retryAfter = loginRetryAfter
                });

                return;
            }

            // Allow the login attempt to proceed; we will record failures after the attempt
            await _next(context);

            // If the login failed (401), record the failure for future throttling
            if (context.Response.StatusCode == (int)HttpStatusCode.Unauthorized)
            {
                var timestamp = DateTime.UtcNow;
                lock (clientInfo)
                {
                    clientInfo.Requests.RemoveAll(r => timestamp - r > timeWindow);
                    clientInfo.Requests.Add(timestamp);
                }
            }

            return;
        }

        // Non-login paths: use default behavior (count every request)
        bool limitExceeded = false;
        int retryAfter = 0;
        int remaining = 0;
        long resetTime = 0;

        lock (clientInfo)
        {
            // Remove requests outside the time window
            clientInfo.Requests.RemoveAll(r => now - r > timeWindow);

            // Check if limit exceeded
            if (clientInfo.Requests.Count >= requestLimit)
            {
                limitExceeded = true;
                var oldestRequest = clientInfo.Requests.Min();
                retryAfter = (int)(timeWindow - (now - oldestRequest)).TotalSeconds;
                resetTime = DateTimeOffset.UtcNow.AddSeconds(retryAfter).ToUnixTimeSeconds();
            }
            else
            {
                // Add current request
                clientInfo.Requests.Add(now);
                remaining = requestLimit - clientInfo.Requests.Count;
                var nextReset = clientInfo.Requests.Min().Add(timeWindow);
                resetTime = new DateTimeOffset(nextReset).ToUnixTimeSeconds();
            }
        }

        if (limitExceeded)
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientId}. Path: {Path}",
                clientId, context.Request.Path);

            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers["Retry-After"] = retryAfter.ToString();
            context.Response.Headers["X-RateLimit-Limit"] = requestLimit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = "0";
            context.Response.Headers["X-RateLimit-Reset"] = resetTime.ToString();

            await context.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit exceeded",
                message = $"Too many requests. Please try again in {retryAfter} seconds.",
                retryAfter
            });
            return;
        }

        // Add rate limit headers for successful requests
        context.Response.Headers["X-RateLimit-Limit"] = requestLimit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = resetTime.ToString();

        await _next(context);
    }

    private static bool IsQrGenerationRequest(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
            return false;

        var path = request.Path.Value?.TrimEnd('/') ?? string.Empty;
        return HasSingleGuidSegment(
                   path,
                   "/api/qrvalidation/prescription/",
                   "/generate") ||
               HasSingleGuidSegment(
                   path,
                   "/api/prescription/",
                   "/generate-qr");
    }

    private static bool IsQrValidationRequest(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
            return false;

        var path = request.Path.Value?.TrimEnd('/') ?? string.Empty;
        return path.Equals(
                   "/api/qrvalidation/prescription/scan",
                   StringComparison.OrdinalIgnoreCase) ||
               path.Equals(
                   "/api/qrvalidation/validate",
                   StringComparison.OrdinalIgnoreCase) ||
               path.Equals(
                   "/api/prescription/validate-qr",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSingleGuidSegment(
        string path,
        string prefix,
        string suffix)
    {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segmentLength = path.Length - prefix.Length - suffix.Length;
        return segmentLength > 0 &&
               Guid.TryParse(path.AsSpan(prefix.Length, segmentLength), out _);
    }

    private static string GetClientIdentifier(HttpContext context)
    {
        // Try to get user ID from claims
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        // ForwardedHeadersMiddleware has already replaced RemoteIpAddress when,
        // and only when, the immediate peer is an explicitly trusted proxy.
        // Never parse forwarding headers again at this layer.
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }

    internal static string HashLoginIdentifier(string identifier)
    {
        var normalized = identifier.Trim().ToLowerInvariant();
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    internal static int CleanupExpiredEntries(
        TimeSpan retentionWindow,
        DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var removed = 0;

        foreach (var (key, clientInfo) in _clients)
        {
            if (PruneExpiredRequests(
                    clientInfo,
                    retentionWindow,
                    now) &&
                _clients.TryRemove(key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    internal static bool PruneExpiredRequests(
        ClientRateLimitInfo clientInfo,
        TimeSpan retentionWindow,
        DateTime utcNow)
    {
        var cutoff = utcNow - retentionWindow;
        lock (clientInfo)
        {
            clientInfo.Requests.RemoveAll(timestamp => timestamp <= cutoff);
            return clientInfo.Requests.Count == 0;
        }
    }
}

public class ClientRateLimitInfo
{
    public List<DateTime> Requests { get; } = [];
}

/// <summary>
/// Hosted service that periodically purges stale entries from the rate-limit in-memory store.
/// Replaces the fire-and-forget Task.Run loop that was previously in the middleware constructor.
/// </summary>
public class RateLimitCleanupService(
    IConfiguration configuration,
    ILogger<RateLimitCleanupService> logger) : BackgroundService
{
    private readonly ILogger<RateLimitCleanupService> _logger = logger;
    private readonly TimeSpan _retentionWindow =
        ResolveMaximumWindow(configuration);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RateLimitCleanupService started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            var removed = RateLimitingMiddleware.CleanupExpiredEntries(
                _retentionWindow);
            _logger.LogDebug(
                "Rate-limit cleanup removed {Count} expired bucket(s)",
                removed);
        }
        _logger.LogInformation("RateLimitCleanupService stopped.");
    }

    private static TimeSpan ResolveMaximumWindow(IConfiguration configuration)
    {
        var maximumSeconds = Math.Max(
            1,
            configuration.GetValue<int?>("RateLimiting:WindowSeconds") ?? 60);

        foreach (var endpoint in configuration
                     .GetSection("RateLimiting:Endpoints")
                     .GetChildren())
        {
            maximumSeconds = Math.Max(
                maximumSeconds,
                endpoint.GetValue<int?>("WindowSeconds") ?? 0);
        }

        return TimeSpan.FromSeconds(maximumSeconds);
    }
}
