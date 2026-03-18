using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

        if (isLoginAttempt)
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

                        if (!string.IsNullOrEmpty(resolvedKey))
                            rateLimitKey = $"{clientId}:user:{resolvedKey.ToLowerInvariant()}";
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

        var clientInfo = _clients.GetOrAdd(rateLimitKey, _ => new ClientRateLimitInfo());

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

            // Apply a tighter limit to public (unauthenticated) search endpoints
            var isPublicSearch = !context.User.Identity?.IsAuthenticated == true && (
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
                if (remoteAddr != null && IsPrivateOrLoopback(remoteAddr) &&
                    context.Request.Headers.TryGetValue("X-Forwarded-For", out var xffPublic))
                {
                    var candidate = xffPublic.ToString().Split(',')[0].Trim();
                    if (!string.IsNullOrEmpty(candidate)) ip = candidate;
                }
                rateLimitKey = $"publicsearch:ip:{ip}";
            }
        }
        catch
        {
            // ignore and fall back to global limits
        }

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

    private static string GetClientIdentifier(HttpContext context)
    {
        // Try to get user ID from claims
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        // Fall back to IP address
        var remoteIp = context.Connection.RemoteIpAddress;
        var ip = remoteIp?.ToString() ?? "unknown";

        // Only honour X-Forwarded-For when the direct connection comes from a trusted
        // proxy (loopback or RFC-1918 private range). Accepting it unconditionally
        // allows any client to spoof their IP and bypass per-IP rate limiting.
        if (remoteIp != null && IsPrivateOrLoopback(remoteIp) &&
            context.Request.Headers.TryGetValue("X-Forwarded-For", out var xff))
        {
            var candidate = xff.ToString().Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(candidate))
                ip = candidate;
        }

        return $"ip:{ip}";
    }

    /// <summary>Returns true for loopback and RFC-1918 private addresses (IPv4 and IPv6).</summary>
    private static bool IsPrivateOrLoopback(System.Net.IPAddress address)
    {
        if (System.Net.IPAddress.IsLoopback(address)) return true;

        // Map IPv4-in-IPv6 to plain IPv4 for range checks
        var addr = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = addr.GetAddressBytes();
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        // IPv6 unique-local (fc00::/7)
        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = addr.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    internal static void CleanupExpiredEntries()
    {
        var keysToRemove = _clients
            .Where(kvp => !kvp.Value.Requests.Any())
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _clients.TryRemove(key, out _);
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
public class RateLimitCleanupService(ILogger<RateLimitCleanupService> logger) : BackgroundService
{
    private readonly ILogger<RateLimitCleanupService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RateLimitCleanupService started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            RateLimitingMiddleware.CleanupExpiredEntries();
            _logger.LogDebug("Rate-limit cleanup executed.");
        }
        _logger.LogInformation("RateLimitCleanupService stopped.");
    }
}
