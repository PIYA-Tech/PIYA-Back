namespace PIYA_API.Configuration;

/// <summary>
/// Public frontend configuration used to construct user-facing links.
/// </summary>
public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    /// <summary>
    /// Absolute public frontend URL. Defaults to PIYA's canonical production
    /// origin and may be overridden for other environments.
    /// </summary>
    public string BaseUrl { get; set; } = "https://piya.life";
}

/// <summary>
/// Private S3-compatible object storage configuration.
/// </summary>
public sealed class S3StorageOptions
{
    public const string SectionName = "Storage:S3";

    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string? ServiceUrl { get; set; }
    public bool ForcePathStyle { get; set; }
}

public sealed class MalwareScanningOptions
{
    public const string SectionName = "FileUpload:MalwareScanning";
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class FirebaseOptions
{
    public const string SectionName = "Firebase";
    public bool Enabled { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string CredentialsPath { get; set; } = string.Empty;
}

/// <summary>
/// Security configuration options
/// </summary>
public class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// HMAC-SHA256 signing key for QR tokens (CRITICAL: Must be 32+ characters in production)
    /// </summary>
    public string QrSigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Dedicated HMAC-SHA256 key for prescription signatures. Production must
    /// configure this independently from <see cref="QrSigningKey"/>.
    /// </summary>
    public string PrescriptionSigningKey { get; set; } = string.Empty;

    /// <summary>
    /// QR token validity period in minutes (default: 5 minutes)
    /// </summary>
    public int QrTokenExpiryMinutes { get; set; } = 5;

    /// <summary>
    /// Days to retain expired QR tokens before cleanup (default: 7 days)
    /// </summary>
    public int QrTokenCleanupDays { get; set; } = 7;

    /// <summary>
    /// BCrypt work factor for password hashing (10-12 recommended)
    /// </summary>
    public int PasswordHashWorkFactor { get; set; } = 11;

    /// <summary>
    /// Maximum failed login attempts before lockout
    /// </summary>
    public int MaxLoginAttempts { get; set; } = 5;

    /// <summary>
    /// Account lockout duration in minutes
    /// </summary>
    public int LockoutDurationMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum number of concurrent active sessions (refresh tokens) per user.
    /// When exceeded the oldest session is evicted. 0 = unlimited (legacy behaviour).
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 3;

    /// <summary>
    /// A short interval in which a duplicate request for an already-rotated token
    /// is rejected without revoking the family. This prevents simultaneous browser
    /// refreshes from logging out a legitimate user while still returning no token.
    /// </summary>
    public int RefreshTokenConcurrencyGraceSeconds { get; set; } = 30;

    /// <summary>
    /// Require HTTPS for all endpoints (production only)
    /// </summary>
    public bool RequireHttps { get; set; } = false;

    /// <summary>
    /// Enable CORS
    /// </summary>
    public bool EnableCors { get; set; } = true;

    /// <summary>
    /// Allowed CORS origins
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    // Note: startup validation is performed in Program.cs via
    // AddOptions<SecurityOptions>().Validate(...).ValidateOnStart()
}

/// <summary>
/// External API configuration options
/// </summary>
public class ExternalApisOptions
{
    public const string SectionName = "ExternalApis";

    public AzerbaijanPharmaceuticalRegistryOptions AzerbaijanPharmaceuticalRegistry { get; set; } = new();
    public MedicationDatabaseOptions MedicationDatabase { get; set; } = new();
    public GoogleMapsOptions GoogleMaps { get; set; } = new();
    public EmailServiceOptions EmailService { get; set; } = new();
    public SmsServiceOptions SmsService { get; set; } = new();
}

/// <summary>
/// Azerbaijan Pharmaceutical Registry API configuration
/// </summary>
public class AzerbaijanPharmaceuticalRegistryOptions
{
    /// <summary>
    /// Base URL for OpenData.az API
    /// </summary>
    public string BaseUrl { get; set; } = "https://opendata.az/api/v1";

    /// <summary>
    /// Medications endpoint
    /// </summary>
    public string Endpoint { get; set; } = "/medications";

    /// <summary>
    /// API key (if required)
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// HTTP request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of retry attempts on failure
    /// </summary>
    public int RetryAttempts { get; set; } = 3;

    /// <summary>
    /// Cache duration for medication data in minutes
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 60;

    /// <summary>
    /// Enable API integration
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Full API URL
    /// </summary>
    public string FullUrl => $"{BaseUrl.TrimEnd('/')}{Endpoint}";
}

/// <summary>
/// Medication database synchronization configuration
/// </summary>
public class MedicationDatabaseOptions
{
    /// <summary>
    /// Data provider (Azerbaijan, WHO, OpenFDA)
    /// </summary>
    public string Provider { get; set; } = "Azerbaijan";

    /// <summary>
    /// Enable automatic synchronization
    /// </summary>
    public bool SyncEnabled { get; set; } = true;

    /// <summary>
    /// Synchronization interval in hours
    /// </summary>
    public int SyncIntervalHours { get; set; } = 24;

    /// <summary>
    /// Last synchronization timestamp
    /// </summary>
    public DateTime? LastSyncTimestamp { get; set; }
}

/// <summary>
/// Google Maps API configuration
/// </summary>
public class GoogleMapsOptions
{
    /// <summary>
    /// Google Maps API key
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Enable Google Maps integration
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Geocoding API endpoint
    /// </summary>
    public string GeocodeEndpoint { get; set; } = "https://maps.googleapis.com/maps/api/geocode/json";

    /// <summary>
    /// Distance Matrix API endpoint
    /// </summary>
    public string DistanceMatrixEndpoint { get; set; } = "https://maps.googleapis.com/maps/api/distancematrix/json";

    /// <summary>
    /// Validates Google Maps configuration
    /// </summary>
    public void Validate()
    {
        if (Enabled && string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("ExternalApis:GoogleMaps:ApiKey is required when Enabled is true.");
        }
    }
}

/// <summary>
/// Email service configuration (SMTP)
/// </summary>
public class EmailServiceOptions
{
    public const string SectionName = "ExternalApis:EmailService";

    /// <summary>
    /// Email provider (SMTP, SendGrid, etc.)
    /// </summary>
    public string Provider { get; set; } = "SMTP";

    /// <summary>
    /// SMTP server host
    /// </summary>
    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>
    /// SMTP server port
    /// </summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// SMTP username
    /// </summary>
    public string SmtpUsername { get; set; } = string.Empty;

    /// <summary>
    /// SMTP password
    /// </summary>
    public string SmtpPassword { get; set; } = string.Empty;

    /// <summary>
    /// Sender email address
    /// </summary>
    public string FromEmail { get; set; } = "noreply@piya.az";

    /// <summary>
    /// Sender display name
    /// </summary>
    public string FromName { get; set; } = "PIYA Healthcare";

    /// <summary>
    /// Enable SSL/TLS
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// Enable email service
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Validates email configuration
    /// </summary>
    public void Validate()
    {
        if (Enabled)
        {
            if (string.IsNullOrWhiteSpace(SmtpHost))
            {
                throw new InvalidOperationException("ExternalApis:EmailService:SmtpHost is required when Enabled is true.");
            }

            if (string.IsNullOrWhiteSpace(SmtpUsername))
            {
                throw new InvalidOperationException("ExternalApis:EmailService:SmtpUsername is required when Enabled is true.");
            }

            if (string.IsNullOrWhiteSpace(SmtpPassword))
            {
                throw new InvalidOperationException("ExternalApis:EmailService:SmtpPassword is required when Enabled is true.");
            }
        }
    }
}

/// <summary>
/// SMS service configuration (Twilio)
/// </summary>
public class SmsServiceOptions
{
    public const string SectionName = "ExternalApis:SmsService";

    /// <summary>
    /// SMS provider (Twilio, etc.)
    /// </summary>
    public string Provider { get; set; } = "Twilio";

    /// <summary>
    /// Twilio Account SID
    /// </summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>
    /// Twilio Auth Token
    /// </summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// Twilio phone number (sender)
    /// </summary>
    public string FromPhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Enable SMS service
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Validates SMS configuration
    /// </summary>
    public void Validate()
    {
        if (Enabled)
        {
            if (string.IsNullOrWhiteSpace(AccountSid))
            {
                throw new InvalidOperationException("ExternalApis:SmsService:AccountSid is required when Enabled is true.");
            }

            if (string.IsNullOrWhiteSpace(AuthToken))
            {
                throw new InvalidOperationException("ExternalApis:SmsService:AuthToken is required when Enabled is true.");
            }

            if (string.IsNullOrWhiteSpace(FromPhoneNumber))
            {
                throw new InvalidOperationException("ExternalApis:SmsService:FromPhoneNumber is required when Enabled is true.");
            }
        }
    }
}

/// <summary>
/// Feature flags configuration
/// </summary>
public class FeaturesOptions
{
    public const string SectionName = "Features";

    public bool EnableTwoFactorAuth { get; set; } = true;
    public bool EnableQrCodeSystem { get; set; } = true;
    public bool EnableAppointmentSystem { get; set; } = true;
    public bool EnablePrescriptionSystem { get; set; } = true;
    public bool EnableAuditLogging { get; set; } = true;
    public bool EnableRateLimiting { get; set; } = false;
    public bool EnableCaching { get; set; } = false;
}

/// <summary>
/// Rate limiting configuration
/// </summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool EnableGlobal { get; set; } = false;
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 0;
    public Dictionary<string, EndpointRateLimitOptions> Endpoints { get; set; } = [];
}

/// <summary>
/// Per-endpoint rate limiting configuration
/// </summary>
public class EndpointRateLimitOptions
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}

/// <summary>
/// Caching configuration
/// </summary>
public class CachingOptions
{
    public const string SectionName = "Caching";

    /// <summary>
    /// Cache provider (InMemory, Redis)
    /// </summary>
    public string Provider { get; set; } = "InMemory";

    /// <summary>
    /// Redis connection string (if Provider is Redis)
    /// </summary>
    public string RedisConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Default cache expiration in minutes
    /// </summary>
    public int DefaultExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Medication data cache duration in minutes
    /// </summary>
    public int MedicationCacheMinutes { get; set; } = 1440;

    /// <summary>
    /// Pharmacy data cache duration in minutes
    /// </summary>
    public int PharmacyCacheMinutes { get; set; } = 60;

    /// <summary>
    /// Validates caching configuration
    /// </summary>
    public void Validate()
    {
        if (Provider == "Redis" && string.IsNullOrWhiteSpace(RedisConnectionString))
        {
            throw new InvalidOperationException("Caching:RedisConnectionString is required when Provider is Redis.");
        }
    }
}
