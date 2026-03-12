using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Serilog;
using FluentValidation;
using FluentValidation.AspNetCore;
using Asp.Versioning;

// Configure Serilog (do not require appsettings.json at startup)
var bootstrapConfig = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(bootstrapConfig)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/piya-api-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting PIYA Healthcare API");

    // TODO(#13): Npgsql legacy timestamp behavior — keeps DateTimeKind.Unspecified working with PostgreSQL.
    // Migration checklist to remove this switch:
    //   1. Add `.HasConversion<UtcDateTimeConverter>()` (or use NodaTime) on all DateTime columns.
    //   2. Update all DateTime properties in models to be stored/read as UTC only
    //      (use DateTime.UtcNow instead of DateTime.Now everywhere; run
    //       `grep -r "DateTime.Now" --include="*.cs"` to find remaining callsites).
    //   3. Generate a new EF migration — the column types will change from `timestamp` to `timestamptz`.
    //   4. Remove this AppContext.SetSwitch call and the Npgsql.EnableLegacyTimestampBehavior entry
    //      from appsettings.json if it exists there.
    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog for logging
    builder.Host.UseSerilog();

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });
    
    // Add FluentValidation
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();
    
    // Add API Versioning
    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version"));
    });
    
    builder.Services.AddOpenApi();

    // Add HttpClient factory for external API calls
    builder.Services.AddHttpClient();

    // Configure strongly-typed configuration options
    // Note: SecurityOptions is registered below via AddOptions<> with validation.
    builder.Services.Configure<ExternalApisOptions>(
        builder.Configuration.GetSection(ExternalApisOptions.SectionName));
builder.Services.Configure<FeaturesOptions>(
    builder.Configuration.GetSection(FeaturesOptions.SectionName));
builder.Services.Configure<RateLimitingOptions>(
    builder.Configuration.GetSection(RateLimitingOptions.SectionName));
builder.Services.AddHostedService<PIYA_API.Middleware.RateLimitCleanupService>();
builder.Services.AddHostedService<PIYA_API.Middleware.RevokedTokenCleanupService>();
builder.Services.Configure<CachingOptions>(
    builder.Configuration.GetSection(CachingOptions.SectionName));

// Validate critical configuration on startup
builder.Services.AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
    .Validate(options =>
    {
        if (string.IsNullOrWhiteSpace(options.QrSigningKey) || options.QrSigningKey.Length < 32)
        {
            return false;
        }
        if (options.QrSigningKey.Contains("CHANGE") || options.QrSigningKey.Contains("REPLACE"))
        {
            return false;
        }
        return true;
    }, "Security:QrSigningKey must be configured, at least 32 characters, and changed from default. Generate with: openssl rand -base64 64")
    .ValidateOnStart();

// Validate database connection string on startup
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is required. " +
        "Set it via appsettings.json or the CONNECTIONSTRINGS__DEFAULTCONNECTION environment variable.");
}

// Configure Redis Distributed Cache
var cacheProvider = builder.Configuration["Caching:Provider"];
if (cacheProvider == "Redis")
{
    var redisConnectionString = builder.Configuration["Caching:RedisConnectionString"];
    if (!string.IsNullOrEmpty(redisConnectionString) && 
        !redisConnectionString.Contains("REPLACE") && 
        !redisConnectionString.Contains("localhost:6379"))
    {
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "PIYA_";
        });
    }
    else
    {
        // Fallback to in-memory cache if Redis is not properly configured
        Log.Warning("Redis not configured or using placeholder. Using in-memory cache.");
        builder.Services.AddDistributedMemoryCache();
    }
}
else
{
    // Use in-memory cache by default
    builder.Services.AddDistributedMemoryCache();
}

// Use AddDbContextPool for efficient connection reuse across requests.
// The pool keeps PharmacyApiDbContext instances alive and resets their state between uses,
// reducing the overhead of creating a new connection per request and preventing pool exhaustion.
builder.Services.AddDbContextPool<PharmacyApiDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null));

    // In CI/LoadTest we allow DB update even if model drift exists.
    // EF tools may construct the context through runtime service provider.
    var ignorePendingModelChanges =
        builder.Environment.IsEnvironment("LoadTest") ||
        string.Equals(
            Environment.GetEnvironmentVariable("EFCORE_IGNORE_PENDING_MODEL_CHANGES"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    if (ignorePendingModelChanges)
    {
        options.ConfigureWarnings(w =>
            w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
}, poolSize: 128);

// Configure CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000", "http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("PIYAPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .WithExposedHeaders("X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset");
    });
    
    // Development policy — explicit localhost origins required for AllowCredentials()
    options.AddPolicy("Development", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "http://localhost:4200",
                "http://localhost:5173",
                "http://localhost:8080",
                "https://localhost:3000",
                "https://localhost:5173")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .WithExposedHeaders("X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset");
    });
});

// Configure JWT Authentication
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] ?? 
    throw new InvalidOperationException(
        "Jwt:SecretKey is not configured. Set the PIYA__Jwt__SecretKey environment variable. " +
        "Generate with: openssl rand -base64 64");

if (jwtSecretKey.Length < 32)
    throw new InvalidOperationException("Jwt:SecretKey must be at least 32 characters.");

if (jwtSecretKey.Contains("REPLACE") || jwtSecretKey.Contains("CHANGE"))
    throw new InvalidOperationException(
        "Jwt:SecretKey must be changed from placeholder value. " +
        "Generate with: openssl rand -base64 64");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "PIYA_API";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "PIYA_Clients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    
    // Add logging for debugging
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Log.Debug("JWT Auth Failed: {Message}", context.Exception.Message);
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            // Cache-first jti revocation check (Redis hit = no DB round-trip).
            // Falls back to DB on cache miss, then backfills the cache.
            var jti = context.Principal?.FindFirst(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrWhiteSpace(jti))
            {
                var jwtSvc = context.HttpContext.RequestServices
                    .GetRequiredService<IJwtService>();
                var revoked = await jwtSvc.IsJtiRevokedAsync(jti);
                if (revoked)
                {
                    context.Fail("Token has been revoked");
                }
            }
        }
    };
});

// Configure Authorization with Role-Based Policies
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("PatientOnly", policy => policy.RequireRole("Patient"))
    .AddPolicy("DoctorOnly", policy => policy.RequireRole("Doctor"))
    .AddPolicy("PharmacistOnly", policy => policy.RequireRole("Pharmacist"))
    .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"))
    .AddPolicy("DoctorOrAdmin", policy => policy.RequireRole("Doctor", "Admin"))
    .AddPolicy("PharmacistOrAdmin", policy => policy.RequireRole("Pharmacist", "Admin"))
    .AddPolicy("HealthcareProfessional", policy => policy.RequireRole("Doctor", "Pharmacist", "Admin"));

// Register Services
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IJwtService, JwtService>();
// DistributedCacheWrapper bridges IDistributedCache (Redis or in-memory) to JwtService
// for cache-first jti revocation checks. Registered as Scoped because IDistributedCache
// itself is designed for scoped or singleton consumption.
builder.Services.AddScoped<IDistributedCacheWrapper, DistributedCacheWrapper>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddScoped<ICoordinatesService, CoordinatesService>();
builder.Services.AddScoped<IPharmacyService, PharmacyService>();
builder.Services.AddScoped<IPharmacyCompanyService,  PharmacyCompanyService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ITwoFactorAuthService, TwoFactorAuthService>();

// Healthcare Services
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IPrescriptionService, PrescriptionService>();
builder.Services.AddScoped<IMedicationService, MedicationService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IQRService, QRService>();
builder.Services.AddScoped<IDoctorNoteService, DoctorNoteService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<IAzerbaijanPharmaceuticalRegistryService, AzerbaijanPharmaceuticalRegistryService>();
builder.Services.AddScoped<IDoctorProfileService, DoctorProfileService>();
builder.Services.AddScoped<IPharmacistLicenseService, PharmacistLicenseService>();
builder.Services.AddScoped<IHospitalService, HospitalService>();

// Referral & Medical Test Services
builder.Services.AddScoped<IReferralService, ReferralService>();
builder.Services.AddScoped<IMedicalTestService, MedicalTestService>();

// Email & Authentication Enhancement Services
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();

// Google Maps API Service
builder.Services.AddHttpClient<IGoogleMapsService, GoogleMapsService>();

// Webhook Service
builder.Services.AddHttpClient<IWebhookService, WebhookService>();

// File Storage Service (S3 or local depending on Storage:Provider config)
var storageProvider = builder.Configuration["Storage:Provider"] ?? "Local";
if (storageProvider.Equals("S3", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IFileStorageService, S3FileStorageService>();
else
    builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();

// File Upload Service
builder.Services.AddScoped<IFileUploadService, FileUploadService>();

// PDF Export Service
builder.Services.AddScoped<IPdfExportService, PdfExportService>();

// HMS Integration Service
builder.Services.AddHttpClient<IHmsIntegrationService, HmsIntegrationService>();

// EHR Integration Service
builder.Services.AddHttpClient<IEhrIntegrationService, EhrIntegrationService>();

// Cache Service
builder.Services.AddScoped<ICacheService, CacheService>();

// Real-time SignalR Notification Service
builder.Services.AddScoped<ISignalRNotificationService, SignalRNotificationService>();
builder.Services.AddSignalR();

// Push Notification Service (FCM)
// FCM depends on scoped services (e.g. DbContext or cache), so register as scoped
builder.Services.AddScoped<IFcmService, FcmService>();

// Access Control & Staff Management Services
builder.Services.AddScoped<IPharmacyStaffService, PharmacyStaffService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();

// Rating, Search History & Reminder Services
builder.Services.AddScoped<IPharmacyRatingService, PharmacyRatingService>();
builder.Services.AddScoped<ISearchHistoryService, SearchHistoryService>();
builder.Services.AddScoped<IAppointmentReminderService, AppointmentReminderService>();
builder.Services.AddScoped<IPrescriptionRefillReminderService, PrescriptionRefillReminderService>();

// Production Readiness Services
builder.Services.AddScoped<IGdprComplianceService, GdprComplianceService>();
// Performance monitoring depends on scoped services like ICacheService; register scoped
builder.Services.AddScoped<IPerformanceMonitoringService, PerformanceMonitoringService>();
// SecurityHardeningService tracks per-IP failed-login counts in in-memory dictionaries;
// must be Singleton so state persists across requests.
builder.Services.AddSingleton<ISecurityHardeningService, SecurityHardeningService>();

// Configure Swagger with JWT support + file-upload operation filter (single registration)
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PIYA Pharmacy API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Enter 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    // Support IFormFile / multipart file upload endpoints
    c.OperationFilter<PIYA_API.Swagger.FileUploadOperationFilter>();
});

var app = builder.Build();

// Global exception handler MUST be first so it wraps all downstream middleware exceptions.
app.UseMiddleware<PIYA_API.Middleware.GlobalExceptionHandlingMiddleware>();

// Configure CORS
var isDevelopment = app.Environment.IsDevelopment();
app.UseCors(isDevelopment ? "Development" : "PIYAPolicy");

// Add Rate Limiting Middleware
app.UseMiddleware<PIYA_API.Middleware.RateLimitingMiddleware>();

// Add Security Hardening Middleware
app.UseMiddleware<PIYA_API.Middleware.SecurityHardeningMiddleware>();

// Add Performance Monitoring Middleware
app.UseMiddleware<PIYA_API.Middleware.PerformanceMonitoringMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Enable Swagger UI
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pharmacy API V1");
        c.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
    });
}

// Enable HTTPS redirection in production/staging
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

    // Map SignalR Hubs
    app.MapHub<PIYA_API.Hubs.NotificationHub>("/notificationHub");
    app.MapHub<PIYA_API.Hubs.PharmacyHub>("/hubs/pharmacy");
    app.MapHub<PIYA_API.Hubs.InventoryHub>("/hubs/inventory");

    // Seed demo users when explicitly enabled via env-var OR appsettings DemoSeeding:Enabled.
    // Set ENABLE_DEMO_SEEDING=true in CI test steps and local dev; never in production.
    var seedEnabled =
        string.Equals(Environment.GetEnvironmentVariable("ENABLE_DEMO_SEEDING"), "true", StringComparison.OrdinalIgnoreCase)
        || app.Configuration.GetValue<bool>("DemoSeeding:Enabled");
    if (seedEnabled)
        await DataSeeder.SeedAsync(app.Services);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make the implicit Program class public for integration testing
public partial class Program { }
