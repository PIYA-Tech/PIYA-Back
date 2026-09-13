using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

namespace PIYA_API.Extensions;

/// <summary>
/// Middleware pipeline configuration.
/// </summary>
public static class MiddlewareExtensions
{
    public static WebApplication UsePiyaMiddleware(this WebApplication app)
    {
        // Resolve the public client address and scheme before any middleware uses
        // RemoteIpAddress / IsHttps for throttling, audit, or cookie handling.
        // Only explicitly configured proxies/networks are trusted; when none are
        // configured ASP.NET Core's loopback-only defaults remain in force.
        if (!app.Environment.IsDevelopment())
        {
            app.UseForwardedHeaders(BuildForwardedHeadersOptions(app.Configuration));
        }

        // Global exception handler MUST be first so it wraps all downstream middleware exceptions.
        app.UseMiddleware<PIYA_API.Middleware.GlobalExceptionHandlingMiddleware>();

        // Resolve the selected controller action before CORS and request guards.
        // Security hardening uses endpoint metadata to distinguish intentionally
        // public read-only routes from protected medical-data routes.
        app.UseRouting();

        // CORS
        app.UseCors(app.Environment.IsDevelopment() ? "Development" : "PIYAPolicy");

        // Populate HttpContext.User before rate/security middleware chooses
        // per-user versus anonymous-IP buckets. Authorization still runs after
        // those request guards and before endpoint execution.
        app.UseAuthentication();
        app.UseMiddleware<PIYA_API.Middleware.ConferenceDoctorMiddleware>();

        // Rate limiting → security hardening → performance monitoring
        app.UseMiddleware<PIYA_API.Middleware.RateLimitingMiddleware>();
        app.UseMiddleware<PIYA_API.Middleware.SecurityHardeningMiddleware>();
        app.UseMiddleware<PIYA_API.Middleware.PerformanceMonitoringMiddleware>();

        // Dev-only: Swagger / OpenAPI
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "PIYA API V1");
                c.RoutePrefix = "swagger";
            });
        }

        app.UseAuthorization();

        app.MapControllers();

        // SignalR hubs
        app.MapHub<PIYA_API.Hubs.NotificationHub>("/notificationHub", options => options.CloseOnAuthenticationExpiration = true);
        app.MapHub<PIYA_API.Hubs.PharmacyHub>("/hubs/pharmacy", options => options.CloseOnAuthenticationExpiration = true);
        app.MapHub<PIYA_API.Hubs.InventoryHub>("/hubs/inventory", options => options.CloseOnAuthenticationExpiration = true);

        return app;
    }

    private static ForwardedHeadersOptions BuildForwardedHeadersOptions(
        IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto,
            ForwardLimit = Math.Clamp(
                configuration.GetValue<int?>("ReverseProxy:ForwardLimit") ?? 1,
                1,
                10),
            RequireHeaderSymmetry = true
        };

        var configuredProxies =
            configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>()
            ?? [];
        var configuredNetworks =
            configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>()
            ?? [];

        if (configuredProxies.Length == 0 && configuredNetworks.Length == 0)
            return options;

        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        foreach (var configuredProxy in configuredProxies)
        {
            if (!IPAddress.TryParse(configuredProxy, out var address))
            {
                throw new InvalidOperationException(
                    $"ReverseProxy:KnownProxies contains invalid IP address '{configuredProxy}'.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var configuredNetwork in configuredNetworks)
        {
            var parts = configuredNetwork.Split('/', 2);
            if (parts.Length != 2 ||
                !IPAddress.TryParse(parts[0], out var prefix) ||
                !int.TryParse(parts[1], out var prefixLength))
            {
                throw new InvalidOperationException(
                    $"ReverseProxy:KnownNetworks contains invalid CIDR '{configuredNetwork}'.");
            }

            var maximumPrefixLength = prefix.AddressFamily ==
                System.Net.Sockets.AddressFamily.InterNetwork
                ? 32
                : 128;
            if (prefixLength < 0 || prefixLength > maximumPrefixLength)
            {
                throw new InvalidOperationException(
                    $"ReverseProxy:KnownNetworks contains invalid CIDR '{configuredNetwork}'.");
            }

            options.KnownNetworks.Add(
                new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
                    prefix,
                    prefixLength));
        }

        return options;
    }
}
