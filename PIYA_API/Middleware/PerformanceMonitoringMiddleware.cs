using System.Diagnostics;
using PIYA_API.Service.Interface;

namespace PIYA_API.Middleware;

/// <summary>
/// Middleware to monitor API endpoint performance and collect metrics
/// </summary>
public class PerformanceMonitoringMiddleware(RequestDelegate next, ILogger<PerformanceMonitoringMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<PerformanceMonitoringMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context, IPerformanceMonitoringService? performanceMonitoring)
    {
        var stopwatch = Stopwatch.StartNew();
        var initialMemory = GC.GetTotalMemory(false);

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var finalMemory = GC.GetTotalMemory(false);
            var memoryUsed = finalMemory - initialMemory;

            var endpoint = context.Request.Path.Value ?? "/";
            var method = context.Request.Method;
            var statusCode = context.Response.StatusCode;
            var durationMs = stopwatch.ElapsedMilliseconds;

            // Log slow requests (> 1 second)
            if (durationMs > 1000)
            {
                _logger.LogWarning(
                    "Slow request detected: {Method} {Endpoint} took {Duration}ms (Status: {StatusCode})",
                    method, endpoint, durationMs, statusCode);
            }

            // Record metrics if service is available
            if (performanceMonitoring != null)
            {
                try
                {
                    await performanceMonitoring.RecordEndpointMetricAsync(
                        endpoint, method, statusCode, durationMs, memoryUsed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record performance metrics");
                }
            }

            // Add performance headers if the response hasn't started yet
            try
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.Headers["X-Response-Time-Ms"] = durationMs.ToString();
                    context.Response.Headers["X-Memory-Used-Bytes"] = memoryUsed.ToString();
                }
                else
                {
                    _logger.LogWarning("Response already started; cannot append performance headers for {Endpoint}", endpoint);
                }
            }
            catch (Exception ex)
            {
                // Guard against middleware ordering issues or header write race conditions
                _logger.LogDebug(ex, "Unable to append performance headers for {Endpoint}", endpoint);
            }
        }
    }
}

public static class PerformanceMonitoringMiddlewareExtensions
{
    public static IApplicationBuilder UsePerformanceMonitoring(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PerformanceMonitoringMiddleware>();
    }
}
