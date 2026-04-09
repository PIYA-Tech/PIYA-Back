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
        services.AddScoped<IPharmacistLicenseService, PharmacistLicenseService>();
        services.AddScoped<IHospitalService, HospitalService>();

        // Referral & Medical Tests
        services.AddScoped<IReferralService, ReferralService>();
        services.AddScoped<IMedicalTestService, MedicalTestService>();

        // Email & Auth enhancements
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<ISmsService, SmsService>();
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();

        // External HTTP clients
        services.AddHttpClient<IGoogleMapsService, GoogleMapsService>();
        services.AddHttpClient<IWebhookService, WebhookService>();
        services.AddHttpClient<IHmsIntegrationService, HmsIntegrationService>();
        services.AddHttpClient<IEhrIntegrationService, EhrIntegrationService>();

        // File storage (S3 or local)
        var storageProvider = config["Storage:Provider"] ?? "Local";
        if (storageProvider.Equals("S3", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IFileStorageService, S3FileStorageService>();
        else
            services.AddSingleton<IFileStorageService, LocalFileStorageService>();

        services.AddScoped<IFileUploadService, FileUploadService>();
        services.AddScoped<IPdfExportService, PdfExportService>();
        services.AddScoped<ICacheService, CacheService>();
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
        services.AddScoped<IPerformanceMonitoringService, PerformanceMonitoringService>();
        services.AddSingleton<ISecurityHardeningService, SecurityHardeningService>();

        // Background hosted services
        services.AddHostedService<PIYA_API.Middleware.RateLimitCleanupService>();
        services.AddHostedService<PIYA_API.Middleware.RevokedTokenCleanupService>();
        services.AddHostedService<PIYA_API.Middleware.PrescriptionExpiryService>();

        return services;
    }
}
