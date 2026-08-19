using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.OpenApi.Models;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Service.Class;
using Serilog;

namespace PIYA_API.Extensions;

/// <summary>
/// Infrastructure concerns: database, caching, CORS, DataProtection, SignalR, Swagger.
/// </summary>
public static class InfrastructureExtensions
{
    /// <summary>Register configuration option objects and validate critical settings.</summary>
    public static IServiceCollection AddPiyaConfiguration(
        this IServiceCollection services,
        IConfiguration config,
        IWebHostEnvironment environment)
    {
        services.Configure<ExternalApisOptions>(
            config.GetSection(ExternalApisOptions.SectionName));
        services.Configure<FeaturesOptions>(
            config.GetSection(FeaturesOptions.SectionName));
        services.Configure<RateLimitingOptions>(
            config.GetSection(RateLimitingOptions.SectionName));
        services.Configure<CachingOptions>(
            config.GetSection(CachingOptions.SectionName));

        services.AddOptions<EmailServiceOptions>()
            .Bind(config.GetSection(EmailServiceOptions.SectionName))
            .Validate(
                options =>
                    !options.Enabled ||
                    (!string.IsNullOrWhiteSpace(options.SmtpHost) &&
                     options.SmtpPort is > 0 and <= 65535 &&
                     IsConfiguredSecret(options.SmtpUsername) &&
                     IsConfiguredSecret(options.SmtpPassword) &&
                     System.Net.Mail.MailAddress.TryCreate(
                         options.FromEmail,
                         out _)),
                "When ExternalApis:EmailService:Enabled is true, SmtpHost, a valid SmtpPort, " +
                "SmtpUsername, SmtpPassword, and a valid FromEmail are required and cannot be placeholders.")
            .ValidateOnStart();

        services.AddOptions<SmsServiceOptions>()
            .Bind(config.GetSection(SmsServiceOptions.SectionName))
            .Validate(
                options =>
                    !options.Enabled ||
                    (IsConfiguredSecret(options.AccountSid) &&
                     IsConfiguredSecret(options.AuthToken) &&
                     !string.IsNullOrWhiteSpace(options.FromPhoneNumber)),
                "When ExternalApis:SmsService:Enabled is true, AccountSid, AuthToken, " +
                "and FromPhoneNumber are required and cannot be placeholders.")
            .ValidateOnStart();

        services.AddOptions<SecurityOptions>()
            .Bind(config.GetSection(SecurityOptions.SectionName))
            .Validate(options =>
            {
                if (string.IsNullOrWhiteSpace(options.QrSigningKey) || options.QrSigningKey.Length < 32)
                    return false;
                if (options.QrSigningKey.Contains("CHANGE", StringComparison.OrdinalIgnoreCase) ||
                    options.QrSigningKey.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
                    return false;
                return true;
            }, "Security:QrSigningKey must be configured, at least 32 characters, and changed from default. " +
               "Generate with: openssl rand -base64 64")
            .Validate(
                options => options.QrTokenCleanupDays is >= 1 and <= 3650,
                "Security:QrTokenCleanupDays must be between 1 and 3650.")
            .Validate(
                options => options.RefreshTokenConcurrencyGraceSeconds is >= 0 and <= 120,
                "Security:RefreshTokenConcurrencyGraceSeconds must be between 0 and 120.")
            .Validate(
                options =>
                {
                    if (string.IsNullOrWhiteSpace(options.PrescriptionSigningKey))
                        return !environment.IsProduction();

                    return options.PrescriptionSigningKey.Length >= 32 &&
                           !options.PrescriptionSigningKey.Contains(
                               "CHANGE",
                               StringComparison.OrdinalIgnoreCase) &&
                           !options.PrescriptionSigningKey.Contains(
                               "REPLACE",
                               StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(
                               options.PrescriptionSigningKey,
                               options.QrSigningKey,
                               StringComparison.Ordinal);
                },
                "Security:PrescriptionSigningKey must be configured in production, at least 32 characters, " +
                "changed from placeholders, and different from Security:QrSigningKey.")
            .ValidateOnStart();

        var frontendOptions = services.AddOptions<FrontendOptions>()
            .Bind(config.GetSection(FrontendOptions.SectionName))
            .Validate(
                options =>
                    string.IsNullOrWhiteSpace(options.BaseUrl) ||
                    IsAbsoluteHttpUrl(options.BaseUrl),
                "Frontend:BaseUrl must be an absolute HTTP(S) URL.");

        if (environment.IsProduction())
        {
            frontendOptions
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.BaseUrl),
                    "Frontend:BaseUrl is required in production. Set Frontend__BaseUrl.")
                .Validate(
                    options =>
                        Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                        uri.Scheme == Uri.UriSchemeHttps,
                    "Frontend:BaseUrl must use HTTPS in production.");
        }

        frontendOptions.ValidateOnStart();

        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required. " +
                "Set it via appsettings.json or the CONNECTIONSTRINGS__DEFAULTCONNECTION environment variable.");
        }

        return services;
    }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsConfiguredSecret(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Register Redis or in-memory distributed cache.</summary>
    public static IServiceCollection AddPiyaCaching(
        this IServiceCollection services, IConfiguration config)
    {
        var cacheProvider = config["Caching:Provider"];
        if (cacheProvider == "Redis")
        {
            var redisConnectionString = config["Caching:RedisConnectionString"];
            if (!string.IsNullOrWhiteSpace(redisConnectionString) &&
                !redisConnectionString.Contains("REPLACE", StringComparison.OrdinalIgnoreCase) &&
                !redisConnectionString.Contains("CHANGE", StringComparison.OrdinalIgnoreCase))
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConnectionString;
                    options.InstanceName = "PIYA_";
                });
                services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
                    _ => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString));
            }
            else
            {
                Log.Warning("Redis not configured or using placeholder. Using in-memory cache.");
                services.AddDistributedMemoryCache();
            }
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        return services;
    }

    /// <summary>Persist DataProtection keys to the file system.</summary>
    public static IServiceCollection AddPiyaDataProtection(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(
                config["DataProtection:KeyPath"] ?? "/app/keys"))
            .SetApplicationName("PIYA_API");

        return services;
    }

    /// <summary>Register EF Core with Npgsql connection pooling.</summary>
    public static IServiceCollection AddPiyaDatabase(
        this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        var databasePerformance = new DatabasePerformanceInterceptor();
        services.AddSingleton(databasePerformance);
        services.AddDbContextPool<PharmacyApiDbContext>(options =>
        {
            options.UseNpgsql(config.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null));
            options.AddInterceptors(databasePerformance);

            var ignorePendingModelChanges =
                env.IsEnvironment("LoadTest") ||
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

        return services;
    }

    /// <summary>Configure production and development CORS policies.</summary>
    public static IServiceCollection AddPiyaCors(
        this IServiceCollection services, IConfiguration config)
    {
        var allowedOrigins =
            config.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? config.GetSection("Security:AllowedOrigins").Get<string[]>()
            ?? throw new InvalidOperationException(
                "Cors:AllowedOrigins (or legacy Security:AllowedOrigins) configuration is required. " +
                "Set it via appsettings.json or the CORS__ALLOWEDORIGINS environment variable as a JSON array.");

        services.AddCors(options =>
        {
            options.AddPolicy("PIYAPolicy", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials()
                      .WithExposedHeaders("X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset");
            });

            options.AddPolicy("Development", policy =>
            {
                policy.WithOrigins(
                        "https://piya.life",
                        "http://localhost:4200",
                        "http://localhost:5173",
                        "http://localhost:8080",
                        "https://localhost:5173")
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials()
                      .WithExposedHeaders("X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset");
            });
        });

        return services;
    }

    /// <summary>Register SignalR with optional Redis backplane.</summary>
    public static IServiceCollection AddPiyaSignalR(
        this IServiceCollection services, IConfiguration config)
    {
        var signalRRedisConn = config.GetConnectionString("Redis");
        var signalRBuilder = services.AddSignalR();
        if (!string.IsNullOrWhiteSpace(signalRRedisConn))
        {
            signalRBuilder.AddStackExchangeRedis(signalRRedisConn, options =>
            {
                options.Configuration.ChannelPrefix =
                    StackExchange.Redis.RedisChannel.Literal("PIYA");
            });
        }

        return services;
    }

    /// <summary>Register Swagger/OpenAPI for development and offline contract generation.</summary>
    public static IServiceCollection AddPiyaSwagger(
        this IServiceCollection services, IWebHostEnvironment env)
    {
        if (!env.IsDevelopment() && !env.IsEnvironment("OpenApi")) return services;

        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "PIYA API", Version = "v1" });
            c.AddServer(new OpenApiServer
            {
                Url = "https://api.piya.life",
                Description = "Production"
            });
            c.CustomOperationIds(apiDescription =>
            {
                var controller = apiDescription.ActionDescriptor.RouteValues["controller"];
                var action = apiDescription.ActionDescriptor.RouteValues["action"];
                return string.IsNullOrWhiteSpace(controller) || string.IsNullOrWhiteSpace(action)
                    ? null
                    : $"{controller}_{action}";
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header. Enter 'Bearer {token}'",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                            { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });

            c.OperationFilter<PIYA_API.Swagger.FileUploadOperationFilter>();
            c.OperationFilter<PIYA_API.Swagger.AuthorizationOperationFilter>();
        });

        return services;
    }
}
