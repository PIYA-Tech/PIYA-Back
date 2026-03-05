using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Local filesystem storage implementation of IFileStorageService.
/// Used in Development and CI — never in Production.
///
/// Pre-signed URL simulation: returns a short-lived token via an in-memory dictionary
/// that the FileUploadController can redeem for a stream.
///
/// Configuration key: Storage:Local:BasePath (default: ./uploads)
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorageService> _logger;

    // token → (objectKey, expiry)  — simple in-process presigned URL simulation
    private static readonly Dictionary<string, (string Key, DateTimeOffset Expiry)> _tokens = [];
    private static readonly Lock _lock = new();

    public LocalFileStorageService(IConfiguration configuration, ILogger<LocalFileStorageService> logger)
    {
        _logger = logger;
        _basePath = configuration["Storage:Local:BasePath"] ?? "./uploads";

        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);
    }

    /// <inheritdoc/>
    public async Task<string> UploadAsync(Stream stream, string objectKey, string contentType)
    {
        // Flatten the object key path to a safe local filename
        var localPath = GetLocalPath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

        stream.Position = 0;
        await using var file = File.Create(localPath);
        await stream.CopyToAsync(file);

        _logger.LogInformation("[LocalStorage] Stored {Key} → {Path}", objectKey, localPath);
        return objectKey;
    }

    /// <inheritdoc/>
    public Task<string> GetPresignedUrlAsync(string objectKey, int expirySeconds = 300)
    {
        var token = Guid.NewGuid().ToString("N");
        var expiry = DateTimeOffset.UtcNow.AddSeconds(expirySeconds);

        lock (_lock)
        {
            // Prune expired tokens
            var stale = _tokens.Where(kv => kv.Value.Expiry < DateTimeOffset.UtcNow).Select(kv => kv.Key).ToList();
            stale.ForEach(k => _tokens.Remove(k));
            _tokens[token] = (objectKey, expiry);
        }

        // Returns a local API URL the FileUploadController redeems
        var url = $"/api/fileupload/local-download/{token}";
        _logger.LogDebug("[LocalStorage] Generated local token {Token} for {Key}", token, objectKey);
        return Task.FromResult(url);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string objectKey)
    {
        var localPath = GetLocalPath(objectKey);
        if (!File.Exists(localPath)) return Task.FromResult(false);
        File.Delete(localPath);
        _logger.LogInformation("[LocalStorage] Deleted {Key}", objectKey);
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string objectKey)
        => Task.FromResult(File.Exists(GetLocalPath(objectKey)));

    // ── Internal helpers ──────────────────────────────────────────────────────

    private string GetLocalPath(string objectKey)
    {
        // Replace / with OS separator and sanitise
        var relative = objectKey.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_basePath, relative));
    }

    /// <summary>
    /// Redeems a local presigned token and returns the resolved objectKey.
    /// The caller is responsible for opening the file via <see cref="GetLocalPath"/>.
    /// Called by FileUploadController.LocalDownload in Development.
    /// Returns null if the token is unknown or expired.
    /// </summary>
    public (Stream stream, string objectKey)? RedeemToken(string token)
    {
        lock (_lock)
        {
            if (!_tokens.TryGetValue(token, out var entry)) return null;
            if (entry.Expiry < DateTimeOffset.UtcNow) { _tokens.Remove(token); return null; }
            _tokens.Remove(token); // single-use

            var localPath = GetLocalPath(entry.Key);
            if (!File.Exists(localPath)) return null;
            return (File.OpenRead(localPath), entry.Key);
        }
    }
}
