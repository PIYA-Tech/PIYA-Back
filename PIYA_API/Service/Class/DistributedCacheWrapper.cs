using Microsoft.Extensions.Caching.Distributed;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Default implementation backed by IDistributedCache (in-memory or Redis).
/// </summary>
public sealed class DistributedCacheWrapper(IDistributedCache inner) : IDistributedCacheWrapper
{
    private readonly IDistributedCache _inner = inner;

    public Task<string?> GetStringAsync(string key, CancellationToken ct = default)
        => _inner.GetStringAsync(key, ct);

    public Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
        => _inner.SetStringAsync(key, value,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);

    public Task RemoveAsync(string key, CancellationToken ct = default)
        => _inner.RemoveAsync(key, ct);
}
