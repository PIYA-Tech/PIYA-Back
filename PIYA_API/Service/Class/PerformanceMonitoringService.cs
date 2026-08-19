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
    DatabasePerformanceInterceptor databasePerformance,
    ICacheService? cacheService = null) : IPerformanceMonitoringService
{
    private readonly ILogger<PerformanceMonitoringService> _logger = logger;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<EndpointMetric>> _endpointMetrics = new();
    private readonly ICacheService? _cacheService = cacheService;
    private readonly DatabasePerformanceInterceptor _databasePerformance = databasePerformance;
    private readonly object _cpuGate = new();
    private TimeSpan _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
    private DateTime _lastCpuSampleAt = DateTime.UtcNow;

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
                _ => new ConcurrentQueue<EndpointMetric>([metric]),
                (metricKey, queue) =>
                {
                    queue.Enqueue(metric);
                    while (queue.Count > 10000) queue.TryDequeue(out _);
                    return queue;
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
        return await Task.FromResult(_databasePerformance.Snapshot(top));
    }

    public async Task<CacheStatistics> GetCacheStatisticsAsync()
    {
        // Get stats from cache service if available
        if (_cacheService != null)
        {
            var stats = _cacheService.GetStatistics();
            return await Task.FromResult(new CacheStatistics
            {
                TotalRequests = stats.TotalRequests,
                CacheHits = stats.Hits,
                CacheMisses = stats.Misses,
                HitRate = stats.TotalRequests == 0 ? 0 : stats.Hits * 100d / stats.TotalRequests,
                TotalKeys = 0,
                ExpiredKeys = 0,
                MemoryUsedBytes = 0,
                AverageGetTimeMs = stats.AverageGetTimeMs
            });
        }

        return new CacheStatistics();
    }

    public async Task<SystemResourceMetrics> GetSystemResourcesAsync()
    {
        var process = Process.GetCurrentProcess();
        double cpuUsage;
        lock (_cpuGate)
        {
            var sampledAt = DateTime.UtcNow;
            var cpuTime = process.TotalProcessorTime;
            var wallTime = sampledAt - _lastCpuSampleAt;
            cpuUsage = wallTime <= TimeSpan.Zero
                ? 0
                : (cpuTime - _lastCpuTime).TotalMilliseconds /
                  (wallTime.TotalMilliseconds * Environment.ProcessorCount) * 100;
            _lastCpuTime = cpuTime;
            _lastCpuSampleAt = sampledAt;
        }
        var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) ?? "/");
        var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        
        return await Task.FromResult(new SystemResourceMetrics
        {
            CpuUsagePercent = Math.Clamp(cpuUsage, 0, 100),
            MemoryUsedBytes = process.WorkingSet64,
            MemoryTotalBytes = totalMemory,
            MemoryUsagePercent = totalMemory <= 0 ? 0 : (double)process.WorkingSet64 / totalMemory * 100,
            DiskUsedBytes = drive.TotalSize - drive.AvailableFreeSpace,
            DiskTotalBytes = drive.TotalSize,
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
            while (kvp.Value.TryPeek(out var metric) && metric.Timestamp < cutoffDate)
            {
                if (kvp.Value.TryDequeue(out _)) totalRemoved++;
            }
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
