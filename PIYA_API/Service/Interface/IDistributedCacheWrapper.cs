namespace PIYA_API.Service.Interface;

/// <summary>
/// Thin wrapper around IDistributedCache that exposes only the string operations
/// needed by JwtService, making it trivially mockable in tests and decoupled from
/// the Microsoft.Extensions.Caching.Distributed namespace.
/// </summary>
public interface IDistributedCacheWrapper
{
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);
    Task SetStringAsync(string key, string value, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
