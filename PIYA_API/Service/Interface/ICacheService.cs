namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for distributed caching
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Get a value from cache
    /// </summary>
    Task<T?> GetAsync<T>(string key) where T : class;
    
    /// <summary>
    /// Set a value in cache
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class;
    
    /// <summary>
    /// Remove a value from cache
    /// </summary>
    Task RemoveAsync(string key);
    
    /// <summary>
    /// Check if a key exists in cache
    /// </summary>
    Task<bool> ExistsAsync(string key);
    
    /// <summary>
    /// Get or set a value (with factory pattern)
    /// </summary>
    Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null) where T : class;
    
    /// <summary>
    /// Remove multiple keys matching a pattern
    /// </summary>
    Task RemoveByPatternAsync(string pattern);

    CacheServiceStatistics GetStatistics();
}

public readonly record struct CacheServiceStatistics(
    long TotalRequests,
    long Hits,
    long Misses,
    double AverageGetTimeMs);
