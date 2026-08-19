using Microsoft.Extensions.Caching.Distributed;
using PIYA_API.Service.Interface;
using System.Text.Json;
using System.Diagnostics;
using StackExchange.Redis;

namespace PIYA_API.Service.Class;

public class CacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _defaultExpiration;
    private readonly IConnectionMultiplexer? _redis;
    private long _requests;
    private long _hits;
    private long _misses;
    private long _getElapsedTicks;

    public CacheService(
        IDistributedCache cache,
        IConfiguration configuration,
        IConnectionMultiplexer? redis = null)
    {
        _cache = cache;
        _configuration = configuration;
        _redis = redis;
        _defaultExpiration = TimeSpan.FromMinutes(
            int.Parse(_configuration["Caching:DefaultExpirationMinutes"] ?? "60"));
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        var started = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _requests);
        string? cachedData;
        try
        {
            cachedData = await _cache.GetStringAsync(key);
        }
        finally
        {
            Interlocked.Add(ref _getElapsedTicks, Stopwatch.GetTimestamp() - started);
        }
        
        if (string.IsNullOrEmpty(cachedData))
        {
            Interlocked.Increment(ref _misses);
            return null;
        }

        Interlocked.Increment(ref _hits);

        return JsonSerializer.Deserialize<T>(cachedData);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        if (value == null)
        {
            return;
        }

        var serializedData = JsonSerializer.Serialize(value);
        
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? _defaultExpiration
        };

        await _cache.SetStringAsync(key, serializedData, options);
    }

    public async Task RemoveAsync(string key)
    {
        await _cache.RemoveAsync(key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        var value = await _cache.GetStringAsync(key);
        return !string.IsNullOrEmpty(value);
    }

    public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null) where T : class
    {
        // Try to get from cache first
        var cachedValue = await GetAsync<T>(key);
        
        if (cachedValue != null)
        {
            return cachedValue;
        }

        // If not in cache, get from factory
        var value = await factory();
        
        if (value != null)
        {
            await SetAsync(key, value, expiration);
        }

        return value;
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        if (_redis == null)
            throw new NotSupportedException("Pattern invalidation requires the Redis cache provider.");

        var database = _redis.GetDatabase();
        var serverPattern = $"PIYA_{pattern}";
        foreach (var endpoint in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica) continue;
            var batch = new List<RedisKey>(500);
            await foreach (var key in server.KeysAsync(pattern: serverPattern, pageSize: 500))
            {
                batch.Add(key);
                if (batch.Count < 500) continue;
                await database.KeyDeleteAsync(batch.ToArray());
                batch.Clear();
            }
            if (batch.Count > 0) await database.KeyDeleteAsync(batch.ToArray());
        }
    }

    public CacheServiceStatistics GetStatistics()
    {
        var requests = Interlocked.Read(ref _requests);
        var ticks = Interlocked.Read(ref _getElapsedTicks);
        return new CacheServiceStatistics(
            requests,
            Interlocked.Read(ref _hits),
            Interlocked.Read(ref _misses),
            requests == 0 ? 0 : ticks * 1000d / Stopwatch.Frequency / requests);
    }
}
