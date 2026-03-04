namespace PIYA_API.Service.Interface;

/// <summary>
/// Abstraction for binary object storage.
/// Implementations: S3FileStorageService (production), LocalFileStorageService (development).
///
/// Security model:
///   - Files are NEVER served directly.  Callers request a time-limited pre-signed URL.
///   - The database stores only the object key, never a hard-coded URL.
///   - Object keys use a deterministic path: {prefix}/{ownerId}/{documentId}/{sanitisedName}
///     e.g. patients/3fa85f64/docs/3b4c1d22/lab-report.pdf
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Upload a stream to object storage.
    /// </summary>
    /// <param name="stream">File content (seekable).</param>
    /// <param name="objectKey">Full storage key, e.g. "patients/{userId}/docs/{docId}/lab-report.pdf".</param>
    /// <param name="contentType">MIME type of the file.</param>
    /// <returns>The stored object key (same as <paramref name="objectKey"/>).</returns>
    Task<string> UploadAsync(Stream stream, string objectKey, string contentType);

    /// <summary>
    /// Generate a time-limited pre-signed URL for a stored object.
    /// The URL grants read-only access for the duration specified.
    /// </summary>
    /// <param name="objectKey">Stored object key.</param>
    /// <param name="expirySeconds">URL validity in seconds (default: 300 = 5 minutes).</param>
    Task<string> GetPresignedUrlAsync(string objectKey, int expirySeconds = 300);

    /// <summary>
    /// Delete an object from storage.
    /// </summary>
    Task<bool> DeleteAsync(string objectKey);

    /// <summary>
    /// Check whether an object exists in storage.
    /// </summary>
    Task<bool> ExistsAsync(string objectKey);

    /// <summary>
    /// Compute a canonical object key for a document upload.
    /// Format: {prefix}/{ownerId}/{documentId}/{sanitisedFileName}
    /// </summary>
    static string BuildObjectKey(string prefix, Guid ownerId, Guid documentId, string fileName)
    {
        // Sanitise: keep only safe chars, lower-case
        var safe = string.Concat(
            Path.GetFileNameWithoutExtension(fileName)
                .ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
            .TrimStart('-').TrimStart('_');

        if (string.IsNullOrEmpty(safe)) safe = "file";

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return $"{prefix}/{ownerId:N}/docs/{documentId:N}/{safe}{ext}";
    }
}
