using System.Net;
using PIYA_API.Configuration;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;

namespace PIYA_API.Extensions;

/// <summary>
/// All application-level service registrations (DI).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPiyaApplicationServices(
        this IServiceCollection services, IConfiguration config)
    {
        // Core services
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IDistributedCacheWrapper, DistributedCacheWrapper>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<ICoordinatesService, CoordinatesService>();
        services.AddScoped<IPharmacyService, PharmacyService>();
        services.AddScoped<IPharmacyCompanyService, PharmacyCompanyService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ITwoFactorAuthService, TwoFactorAuthService>();
        services.Configure<VeriffOptions>(config.GetSection(VeriffOptions.Section));
        services.AddHttpClient<IVerificationProviderGateway, VeriffVerificationProviderGateway>(client => {
            client.Timeout = TimeSpan.FromSeconds(25);
            client.MaxResponseContentBufferSize = 1_048_576;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
          .RemoveAllLoggers();

        // Healthcare
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IPrescriptionService, PrescriptionService>();
        services.AddScoped<IMedicationService, MedicationService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IQRService, QRService>();
        services.AddScoped<IDoctorNoteService, DoctorNoteService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<IAzerbaijanPharmaceuticalRegistryService, AzerbaijanPharmaceuticalRegistryService>();
        services.AddScoped<IDoctorProfileService, DoctorProfileService>();
        services.AddScoped<ICareLoopService, CareLoopService>();
        services.AddScoped<ICareCircleAccessService, CareCircleAccessService>();
        services.AddScoped<IPharmacistLicenseService, PharmacistLicenseService>();
        services.AddScoped<IHospitalService, HospitalService>();
        services.AddScoped<IFacilityDirectoryService, FacilityDirectoryService>();
        services.AddScoped<IFacilityDirectorySyncService, FacilityDirectorySyncService>();

        // Referral & Medical Tests
        services.AddScoped<IReferralService, ReferralService>();
        services.AddScoped<IMedicalTestService, MedicalTestService>();
        services.AddScoped<IPatientMedicationService, PatientMedicationService>();
        services.AddScoped<IPatientNotificationInboxService, PatientNotificationInboxService>();
        services.AddScoped<IMedicationReminderProcessor, MedicationReminderProcessor>();
        services.AddScoped<IPatientPickupService, PatientPickupService>();
        services.AddScoped<IStructuredLabResultService, StructuredLabResultService>();

        // Email & Auth enhancements
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<ISmsService, SmsService>();
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();

        // External HTTP clients
        services.AddHttpClient<IGoogleMapsService, GoogleMapsService>();
        services.AddHttpClient<IWebhookService, WebhookService>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectCallback = WebhookDestinationPolicy.ConnectAsync,
            });
        services.AddHttpClient<IHmsIntegrationService, HmsIntegrationService>();
        services.AddHttpClient<IEhrIntegrationService, EhrIntegrationService>();
        services.AddHttpClient("FirebaseCloudMessaging");
        services.AddHttpClient("ApplePushNotifications", client =>
        {
            client.DefaultRequestVersion = HttpVersion.Version20;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHttpClient("FacilityDirectory", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PIYA-Facility-Directory/1.0 (+https://piya.life)");
        });

        services.AddOptions<FirebaseOptions>()
            .Bind(config.GetSection(FirebaseOptions.SectionName))
            .Validate(options =>
                !options.Enabled ||
                (!string.IsNullOrWhiteSpace(options.ProjectId) &&
                 !string.IsNullOrWhiteSpace(options.CredentialsPath)),
                "Firebase:ProjectId and Firebase:CredentialsPath are required when Firebase is enabled.")
            .Validate(options => !options.Enabled || File.Exists(options.CredentialsPath),
                "Firebase:CredentialsPath must reference a readable service-account file when Firebase is enabled.")
            .ValidateOnStart();

        services.AddOptions<ApplePushOptions>()
            .Bind(config.GetSection(ApplePushOptions.SectionName))
            .Validate(options =>
                !options.Enabled ||
                (!string.IsNullOrWhiteSpace(options.TeamId) &&
                 !string.IsNullOrWhiteSpace(options.KeyId) &&
                 !string.IsNullOrWhiteSpace(options.BundleId) &&
                 !string.IsNullOrWhiteSpace(options.CareBundleId) &&
                 !string.IsNullOrWhiteSpace(options.PrivateKeyPath)),
                "ApplePush:TeamId, KeyId, BundleId, CareBundleId, and PrivateKeyPath are required when Apple push is enabled.")
            .Validate(options => !options.Enabled || File.Exists(options.PrivateKeyPath),
                "ApplePush:PrivateKeyPath must reference a readable APNs .p8 private key when enabled.")
            .ValidateOnStart();

        services.AddPiyaFileStorage(config);

        var malwareScanningEnabled = config.GetValue<bool>("FileUpload:MalwareScanning:Enabled");
        if (malwareScanningEnabled)
        {
            services.AddOptions<MalwareScanningOptions>()
                .Bind(config.GetSection(MalwareScanningOptions.SectionName))
                .Validate(options => !string.IsNullOrWhiteSpace(options.Host),
                    "FileUpload:MalwareScanning:Host is required when scanning is enabled.")
                .Validate(options => options.Port is >= 1 and <= 65535,
                    "FileUpload:MalwareScanning:Port must be between 1 and 65535.")
                .Validate(options => options.TimeoutSeconds is >= 1 and <= 120,
                    "FileUpload:MalwareScanning:TimeoutSeconds must be between 1 and 120.")
                .ValidateOnStart();
            services.AddScoped<IFileSecurityScanner, ClamAvFileSecurityScanner>();
        }
        else
        {
            services.AddSingleton<IFileSecurityScanner, DisabledFileSecurityScanner>();
        }

        services.AddScoped<IFileUploadService, FileUploadService>();
        services.AddScoped<IPdfExportService, PdfExportService>();
        services.AddSingleton<ICacheService, CacheService>();
        services.AddScoped<ISignalRNotificationService, SignalRNotificationService>();
        services.AddScoped<IFcmService, FcmService>();

        // Access control & staff
        services.AddScoped<IPharmacyStaffService, PharmacyStaffService>();
        services.AddScoped<IPermissionService, PermissionService>();

        // Ratings, search history & reminders
        services.AddScoped<IPharmacyRatingService, PharmacyRatingService>();
        services.AddScoped<ISearchHistoryService, SearchHistoryService>();
        services.AddScoped<IAppointmentReminderService, AppointmentReminderService>();
        services.AddScoped<IPrescriptionRefillReminderService, PrescriptionRefillReminderService>();

        // Production readiness
        services.AddScoped<IGdprComplianceService, GdprComplianceService>();
        services.AddSingleton<IPerformanceMonitoringService, PerformanceMonitoringService>();
        services.AddSingleton<ISecurityHardeningService, SecurityHardeningService>();

        // Background hosted services
        services.AddHostedService<PIYA_API.Middleware.RateLimitCleanupService>();
        services.AddHostedService<PIYA_API.Middleware.RevokedTokenCleanupService>();
        services.AddHostedService<PIYA_API.Middleware.QrTokenCleanupService>();
        services.AddHostedService<PIYA_API.Middleware.PrescriptionExpiryService>();
        services.AddHostedService<PIYA_API.Middleware.WebhookDeliveryWorker>();
        services.AddHostedService<FacilityDirectorySyncWorker>();

        return services;
    }

    /// <summary>
    /// Register the configured file-storage provider and validate S3 settings
    /// during host startup rather than on the first upload request.
    /// </summary>
    public static IServiceCollection AddPiyaFileStorage(
        this IServiceCollection services,
        IConfiguration config)
    {
        var storageProvider = (config["Storage:Provider"] ?? "Local").Trim();
        if (storageProvider.Equals("S3", StringComparison.OrdinalIgnoreCase))
        {
            services.AddOptions<S3StorageOptions>()
                .Bind(config.GetSection(S3StorageOptions.SectionName))
                .Validate(
                    options => IsConfiguredStorageValue(options.BucketName),
                    "Storage:S3:BucketName is required when Storage:Provider is S3.")
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.Region),
                    "Storage:S3:Region is required when Storage:Provider is S3.")
                .Validate(
                    options => IsConfiguredStorageValue(options.AccessKeyId),
                    "Storage:S3:AccessKeyId is required when Storage:Provider is S3.")
                .Validate(
                    options => IsConfiguredStorageValue(options.SecretAccessKey),
                    "Storage:S3:SecretAccessKey is required when Storage:Provider is S3.")
                .Validate(
                    options =>
                        string.IsNullOrWhiteSpace(options.ServiceUrl) ||
                        IsAbsoluteHttpUrl(options.ServiceUrl),
                    "Storage:S3:ServiceUrl must be an absolute HTTP(S) URL when configured.")
                .ValidateOnStart();

            services.AddSingleton<IFileStorageService, S3FileStorageService>();
            return services;
        }

        if (!storageProvider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported Storage:Provider '{storageProvider}'. Expected 'Local' or 'S3'.");
        }

        services.AddSingleton<IFileStorageService, LocalFileStorageService>();
        return services;
    }

    private static bool IsConfiguredStorageValue(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
