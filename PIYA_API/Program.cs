using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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

    // Enable legacy timestamp behavior for Npgsql to handle non-UTC DateTimes
    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog for logging
    builder.Host.UseSerilog();

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
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
    builder.Services.Configure<SecurityOptions>(
        builder.Configuration.GetSection(SecurityOptions.SectionName));
    builder.Services.Configure<ExternalApisOptions>(
        builder.Configuration.GetSection(ExternalApisOptions.SectionName));
builder.Services.Configure<FeaturesOptions>(
    builder.Configuration.GetSection(FeaturesOptions.SectionName));
builder.Services.Configure<RateLimitingOptions>(
    builder.Configuration.GetSection(RateLimitingOptions.SectionName));
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
        Console.WriteLine("Warning: Redis not configured. Using in-memory cache.");
        builder.Services.AddDistributedMemoryCache();
    }
}
else
{
    // Use in-memory cache by default
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddDbContext<PharmacyApiDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

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
    
    // Development policy - allow all origins
    options.AddPolicy("Development", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset");
    });
});

// Configure JWT Authentication
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] ?? 
    throw new InvalidOperationException("JWT SecretKey is not configured");
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
            Console.WriteLine($"JWT Auth Failed: {context.Exception.Message}");
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Console.WriteLine("JWT Token Validated Successfully");
            return Task.CompletedTask;
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

// Email & Authentication Enhancement Services
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();

// Google Maps API Service
builder.Services.AddHttpClient<IGoogleMapsService, GoogleMapsService>();

// Webhook Service
builder.Services.AddHttpClient<IWebhookService, WebhookService>();

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
// Security hardening may need scoped services such as IAuditService; register scoped
builder.Services.AddScoped<ISecurityHardeningService, SecurityHardeningService>();

// Configure Swagger with JWT support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PIYA Pharmacy API", Version = "v1" });
    
    // Add JWT Authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.",
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
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
    // Register operation filter to support file uploads in Swagger (IFormFile)
    builder.Services.Configure<Swashbuckle.AspNetCore.SwaggerGen.SwaggerGeneratorOptions>(opts => { });
    builder.Services.AddSwaggerGen(c =>
    {
        c.OperationFilter<PIYA_API.Swagger.FileUploadOperationFilter>();
    });

var app = builder.Build();

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

// app.UseHttpsRedirection(); // Commented out for development
    app.UseAuthentication(); // Add this before UseAuthorization
    app.UseAuthorization();
    
    // Add Global Exception Handling Middleware
    app.UseMiddleware<PIYA_API.Middleware.GlobalExceptionHandlingMiddleware>();
    
    app.MapControllers();

    // Map SignalR Hubs
    app.MapHub<PIYA_API.Hubs.NotificationHub>("/notificationHub");

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
