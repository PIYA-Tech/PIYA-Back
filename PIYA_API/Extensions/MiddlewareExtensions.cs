using Microsoft.AspNetCore.HttpOverrides;

namespace PIYA_API.Extensions;

/// <summary>
/// Middleware pipeline configuration.
/// </summary>
public static class MiddlewareExtensions
{
    public static WebApplication UsePiyaMiddleware(this WebApplication app)
    {
        // Global exception handler MUST be first so it wraps all downstream middleware exceptions.
        app.UseMiddleware<PIYA_API.Middleware.GlobalExceptionHandlingMiddleware>();

        // CORS
        app.UseCors(app.Environment.IsDevelopment() ? "Development" : "PIYAPolicy");

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

        // Forwarded headers for reverse-proxy (production)
        if (!app.Environment.IsDevelopment())
        {
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor
                                 | ForwardedHeaders.XForwardedProto
            });
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        // SignalR hubs
        app.MapHub<PIYA_API.Hubs.NotificationHub>("/notificationHub");
        app.MapHub<PIYA_API.Hubs.PharmacyHub>("/hubs/pharmacy");
        app.MapHub<PIYA_API.Hubs.InventoryHub>("/hubs/inventory");

        return app;
    }
}
