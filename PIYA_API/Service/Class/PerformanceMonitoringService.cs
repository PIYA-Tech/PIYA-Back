using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Service.Interface;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace PIYA_API.Service.Class;

/// <summary>
/// In-memory performance monitoring service
/// Note: For production, consider using Application Insights, Prometheus, or similar
/// </summary>
public class PerformanceMonitoringService(
    ILogger<PerformanceMonitoringService> logger,
    ICacheService? cacheService = null) : IPerformanceMonitoringService
{
    private readonly ILogger<PerformanceMonitoringService> _logger = logger;
    private readonly ConcurrentDictionary<string, List<EndpointMetric>> _endpointMetrics = new();
    private readonly ICacheService? _cacheService = cacheService;

    public async Task RecordEndpointMetricAsync(string endpoint, string method, int statusCode, long durationMs, long? memoryUsed = null)
    {
        try
        {
            var key = $"{method}:{endpoint}";
            var metric = new EndpointMetric
            {
                Endpoint = endpoint,
                Method = method,
                StatusCode = statusCode,
                DurationMs = durationMs,
                MemoryUsed = memoryUsed,
                Timestamp = DateTime.UtcNow
            };

            _endpointMetrics.AddOrUpdate(key,
                [metric],
                (_, list) =>
                {
                    list.Add(metric);
                    // Keep only last 10000 metrics per endpoint
                    if (list.Count > 10000)
                    {
                        list.RemoveRange(0, 5000);
                    }
                    return list;
                });

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording endpoint metric");
        }
    }

    public async Task<EndpointPerformanceMetrics> GetEndpointMetricsAsync(string endpoint, DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            var allMetrics = new List<EndpointMetric>();
            
            // Find all metrics for this endpoint across all methods
            foreach (var kvp in _endpointMetrics.Where(k => k.Key.EndsWith(endpoint)))
            {
                allMetrics.AddRange(kvp.Value);
            }

            if (startDate.HasValue)
                allMetrics = allMetrics.Where(m => m.Timestamp >= startDate.Value).ToList();
            
            if (endDate.HasValue)
                allMetrics = allMetrics.Where(m => m.Timestamp <= endDate.Value).ToList();

            if (allMetrics.Count == 0)
            {
                return new EndpointPerformanceMetrics
                {
                    Endpoint = endpoint,
                    Method = "ALL",
                    TotalRequests = 0,
                    SuccessfulRequests = 0,
                    FailedRequests = 0,
                    AverageDurationMs = 0,
                    MinDurationMs = 0,
                    MaxDurationMs = 0,
                    P50DurationMs = 0,
                    P95DurationMs = 0,
                    P99DurationMs = 0,
                    ErrorRate = 0,
                    FirstRequestAt = DateTime.UtcNow,
                    LastRequestAt = DateTime.UtcNow
                };
            }

            var sortedDurations = allMetrics.Select(m => m.DurationMs).OrderBy(d => d).ToList();
            var successCount = allMetrics.Count(m => m.StatusCode >= 200 && m.StatusCode < 300);
            var failCount = allMetrics.Count - successCount;

            return await Task.FromResult(new EndpointPerformanceMetrics
            {
                Endpoint = endpoint,
                Method = allMetrics.First().Method,
                TotalRequests = allMetrics.Count,
                SuccessfulRequests = successCount,
                FailedRequests = failCount,
                AverageDurationMs = allMetrics.Average(m => m.DurationMs),
                MinDurationMs = sortedDurations.Min(),
                MaxDurationMs = sortedDurations.Max(),
                P50DurationMs = GetPercentile(sortedDurations, 0.50),
                P95DurationMs = GetPercentile(sortedDurations, 0.95),
                P99DurationMs = GetPercentile(sortedDurations, 0.99),
                ErrorRate = allMetrics.Count > 0 ? (double)failCount / allMetrics.Count * 100 : 0,
                FirstRequestAt = allMetrics.Min(m => m.Timestamp),
                LastRequestAt = allMetrics.Max(m => m.Timestamp)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting endpoint metrics for {Endpoint}", endpoint);
            throw;
        }
    }

    public async Task<List<EndpointPerformanceMetrics>> GetSlowestEndpointsAsync(int top = 10)
    {
        var results = new List<EndpointPerformanceMetrics>();

        foreach (var kvp in _endpointMetrics)
        {
            var parts = kvp.Key.Split(':');
            if (parts.Length == 2)
            {
                var metrics = await GetEndpointMetricsAsync(parts[1]);
                results.Add(metrics);
            }
        }

        return results
            .OrderByDescending(m => m.AverageDurationMs)
            .Take(top)
            .ToList();
    }

    public async Task<List<DatabaseQueryMetric>> GetDatabaseMetricsAsync(int top = 20)
    {
        // This would require EF Core interceptors or profiling tools
        // For now, return placeholder data
        return await Task.FromResult(new List<DatabaseQueryMetric>());
    }

    public async Task<CacheStatistics> GetCacheStatisticsAsync()
    {
        // Get stats from cache service if available
        if (_cacheService != null)
        {
            // Would need to implement stats tracking in cache service
            return await Task.FromResult(new CacheStatistics
            {
                TotalRequests = 0,
                CacheHits = 0,
                CacheMisses = 0,
                HitRate = 0,
                TotalKeys = 0,
                ExpiredKeys = 0,
                MemoryUsedBytes = 0,
                AverageGetTimeMs = 0
            });
        }

        return new CacheStatistics();
    }

    public async Task<SystemResourceMetrics> GetSystemResourcesAsync()
    {
        var process = Process.GetCurrentProcess();
        
        return await Task.FromResult(new SystemResourceMetrics
        {
            CpuUsagePercent = 0, // Requires periodic sampling
            MemoryUsedBytes = process.WorkingSet64,
            MemoryTotalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            MemoryUsagePercent = (double)process.WorkingSet64 / GC.GetGCMemoryInfo().TotalAvailableMemoryBytes * 100,
            DiskUsedBytes = 0, // Would need DriveInfo
            DiskTotalBytes = 0,
            ActiveConnections = 0, // Would need to track
            ThreadPoolThreads = ThreadPool.ThreadCount,
            MeasuredAt = DateTime.UtcNow
        });
    }

    public async Task<int> CleanupOldMetricsAsync(int olderThanDays = 30)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
        var totalRemoved = 0;

        foreach (var kvp in _endpointMetrics)
        {
            var before = kvp.Value.Count;
            kvp.Value.RemoveAll(m => m.Timestamp < cutoffDate);
            totalRemoved += before - kvp.Value.Count;
        }

        _logger.LogInformation("Cleaned up {Count} old performance metrics", totalRemoved);
        return await Task.FromResult(totalRemoved);
    }

    private static double GetPercentile(List<long> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        
        var index = (int)Math.Ceiling(sortedValues.Count * percentile) - 1;
        index = Math.Max(0, Math.Min(index, sortedValues.Count - 1));
        
        return sortedValues[index];
    }

    private class EndpointMetric
    {
        public required string Endpoint { get; set; }
        public required string Method { get; set; }
        public int StatusCode { get; set; }
        public long DurationMs { get; set; }
        public long? MemoryUsed { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
