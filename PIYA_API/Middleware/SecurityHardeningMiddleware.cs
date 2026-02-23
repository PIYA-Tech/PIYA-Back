using PIYA_API.Service.Interface;

namespace PIYA_API.Middleware;

/// <summary>
/// Middleware for advanced security hardening and threat detection
/// </summary>
public class SecurityHardeningMiddleware(RequestDelegate next, ILogger<SecurityHardeningMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<SecurityHardeningMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context, ISecurityHardeningService? securityService)
    {
        if (securityService == null)
        {
            await _next(context);
            return;
        }

        var ipAddress = GetClientIpAddress(context);
        var userAgent = context.Request.Headers.UserAgent.ToString();

        // Check if IP is blocked
        if (await securityService.IsIpBlockedAsync(ipAddress))
        {
            _logger.LogWarning("Blocked request from IP: {IpAddress}", ipAddress);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Access forbidden" });
            return;
        }

        // Add security headers
        AddSecurityHeaders(context);

        // Check for common attack patterns in query strings and form data
        if (await DetectAttackPatterns(context, securityService, ipAddress))
        {
            return; // Request blocked
        }

        await _next(context);
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for X-Forwarded-For header (reverse proxy)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        // Check for X-Real-IP header
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static void AddSecurityHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Prevent clickjacking
        headers.Append("X-Frame-Options", "DENY");

        // Prevent MIME type sniffing
        headers.Append("X-Content-Type-Options", "nosniff");

        // Enable XSS protection
        headers.Append("X-XSS-Protection", "1; mode=block");

        // Enforce HTTPS
        headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");

        // Content Security Policy
        headers.Append("Content-Security-Policy", 
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self'; connect-src 'self'; frame-ancestors 'none';");

        // Referrer Policy
        headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

        // Permissions Policy
        headers.Append("Permissions-Policy", 
            "geolocation=(), microphone=(), camera=(), payment=()");
    }

    private async Task<bool> DetectAttackPatterns(HttpContext context, ISecurityHardeningService securityService, string ipAddress)
    {
        // Check query strings
        foreach (var query in context.Request.Query)
        {
            var value = query.Value.ToString();

            if (await securityService.DetectSqlInjectionAsync(value))
            {
                _logger.LogWarning("SQL injection attempt detected from IP: {IpAddress}, Query: {Query}", 
                    ipAddress, query.Key);
                await BlockRequest(context, ipAddress, "SQL injection attempt", securityService);
                return true;
            }

            if (await securityService.DetectXssAsync(value))
            {
                _logger.LogWarning("XSS attempt detected from IP: {IpAddress}, Query: {Query}", 
                    ipAddress, query.Key);
                await BlockRequest(context, ipAddress, "XSS attempt", securityService);
                return true;
            }
        }

        // Check form data
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync();
            foreach (var field in form)
            {
                var value = field.Value.ToString();

                if (await securityService.DetectSqlInjectionAsync(value))
                {
                    _logger.LogWarning("SQL injection attempt detected in form data from IP: {IpAddress}", ipAddress);
                    await BlockRequest(context, ipAddress, "SQL injection in form", securityService);
                    return true;
                }

                if (await securityService.DetectXssAsync(value))
                {
                    _logger.LogWarning("XSS attempt detected in form data from IP: {IpAddress}", ipAddress);
                    await BlockRequest(context, ipAddress, "XSS in form", securityService);
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task BlockRequest(HttpContext context, string ipAddress, string reason, ISecurityHardeningService securityService)
    {
        await securityService.BlockIpAddressAsync(ipAddress, reason, TimeSpan.FromHours(24));
        
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new 
        { 
            error = "Security violation detected",
            message = "Your request has been blocked due to suspicious activity"
        });
    }
}

public static class SecurityHardeningMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHardening(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHardeningMiddleware>();
    }
}
