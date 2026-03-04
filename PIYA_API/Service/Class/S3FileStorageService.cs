using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Object storage backed by Amazon S3 (or any S3-compatible service: MinIO, DigitalOcean Spaces,
/// Cloudflare R2, Backblaze B2).
///
/// Configuration keys — all must arrive via environment variables in production:
///   Storage__S3__BucketName
///   Storage__S3__Region          (e.g. "us-east-1" or "auto" for Cloudflare R2)
///   Storage__S3__AccessKeyId
///   Storage__S3__SecretAccessKey
///   Storage__S3__ServiceUrl      (optional — for non-AWS endpoints, e.g. MinIO)
///   Storage__S3__ForcePathStyle  (optional — true for MinIO/local)
/// </summary>
public class S3FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;
    private readonly ILogger<S3FileStorageService> _logger;

    public S3FileStorageService(IConfiguration configuration, ILogger<S3FileStorageService> logger)
    {
        _logger = logger;

        var bucketName  = configuration["Storage:S3:BucketName"]      ?? throw new InvalidOperationException("Storage:S3:BucketName is required.");
        var accessKeyId = configuration["Storage:S3:AccessKeyId"]      ?? throw new InvalidOperationException("Storage:S3:AccessKeyId is required.");
        var secretKey   = configuration["Storage:S3:SecretAccessKey"]  ?? throw new InvalidOperationException("Storage:S3:SecretAccessKey is required.");
        var regionStr   = configuration["Storage:S3:Region"]           ?? "us-east-1";
        var serviceUrl  = configuration["Storage:S3:ServiceUrl"];       // null for AWS
        var forcePathStyle = configuration.GetValue<bool>("Storage:S3:ForcePathStyle");

        _bucketName = bucketName;

        var credentials = new BasicAWSCredentials(accessKeyId, secretKey);

        var config = new AmazonS3Config
        {
            ForcePathStyle = forcePathStyle,
        };

        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            // Non-AWS endpoint (MinIO / DO Spaces / R2)
            config.ServiceURL = serviceUrl;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(regionStr);
        }

        _s3 = new AmazonS3Client(credentials, config);
    }

    /// <inheritdoc/>
    public async Task<string> UploadAsync(Stream stream, string objectKey, string contentType)
    {
        stream.Position = 0;

        var request = new PutObjectRequest
        {
            BucketName  = _bucketName,
            Key         = objectKey,
            InputStream = stream,
            ContentType = contentType,
            // Deny all public access — objects are accessed via pre-signed URLs only.
            CannedACL   = S3CannedACL.Private,
        };

        // Server-side encryption at rest (AES-256 managed by S3)
        request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;

        try
        {
            await _s3.PutObjectAsync(request);
            _logger.LogInformation("Uploaded {Key} to S3 bucket {Bucket}", objectKey, _bucketName);
            return objectKey;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 upload failed for key {Key}", objectKey);
            throw new InvalidOperationException($"Failed to upload file to storage: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public Task<string> GetPresignedUrlAsync(string objectKey, int expirySeconds = 300)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key        = objectKey,
            Verb       = HttpVerb.GET,
            Expires    = DateTime.UtcNow.AddSeconds(expirySeconds),
            Protocol   = Protocol.HTTPS,
        };

        // GetPreSignedURL is synchronous in the SDK; wrap for consistent async contract.
        var url = _s3.GetPreSignedURL(request);
        _logger.LogDebug("Generated pre-signed URL for {Key} (expires {Seconds}s)", objectKey, expirySeconds);
        return Task.FromResult(url);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string objectKey)
    {
        try
        {
            await _s3.DeleteObjectAsync(_bucketName, objectKey);
            _logger.LogInformation("Deleted {Key} from S3 bucket {Bucket}", objectKey, _bucketName);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 delete failed for key {Key}", objectKey);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string objectKey)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_bucketName, objectKey);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
