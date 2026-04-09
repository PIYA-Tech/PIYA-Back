using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.OpenApi.Models;
using PIYA_API.Configuration;
using PIYA_API.Data;
using Serilog;

namespace PIYA_API.Extensions;

/// <summary>
/// Infrastructure concerns: database, caching, CORS, DataProtection, SignalR, Swagger.
/// </summary>
public static class InfrastructureExtensions
{
    /// <summary>Register configuration option objects and validate critical settings.</summary>
    public static IServiceCollection AddPiyaConfiguration(
        this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ExternalApisOptions>(
            config.GetSection(ExternalApisOptions.SectionName));
        services.Configure<FeaturesOptions>(
            config.GetSection(FeaturesOptions.SectionName));
        services.Configure<RateLimitingOptions>(
            config.GetSection(RateLimitingOptions.SectionName));
        services.Configure<CachingOptions>(
            config.GetSection(CachingOptions.SectionName));

        services.AddOptions<SecurityOptions>()
            .Bind(config.GetSection(SecurityOptions.SectionName))
            .Validate(options =>
            {
                if (string.IsNullOrWhiteSpace(options.QrSigningKey) || options.QrSigningKey.Length < 32)
                    return false;
                if (options.QrSigningKey.Contains("CHANGE") || options.QrSigningKey.Contains("REPLACE"))
                    return false;
                return true;
            }, "Security:QrSigningKey must be configured, at least 32 characters, and changed from default. " +
               "Generate with: openssl rand -base64 64")
            .ValidateOnStart();

        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required. " +
                "Set it via appsettings.json or the CONNECTIONSTRINGS__DEFAULTCONNECTION environment variable.");
        }

        return services;
    }

    /// <summary>Register Redis or in-memory distributed cache.</summary>
    public static IServiceCollection AddPiyaCaching(
        this IServiceCollection services, IConfiguration config)
    {
        var cacheProvider = config["Caching:Provider"];
        if (cacheProvider == "Redis")
        {
            var redisConnectionString = config["Caching:RedisConnectionString"];
            if (!string.IsNullOrEmpty(redisConnectionString) &&
                !redisConnectionString.Contains("REPLACE") &&
                !redisConnectionString.Contains("localhost:6379"))
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConnectionString;
                    options.InstanceName = "PIYA_";
                });
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
        services.AddDbContextPool<PharmacyApiDbContext>(options =>
        {
            options.UseNpgsql(config.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null));

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

    /// <summary>Register Swagger/OpenAPI (development only).</summary>
    public static IServiceCollection AddPiyaSwagger(
        this IServiceCollection services, IWebHostEnvironment env)
    {
        if (!env.IsDevelopment()) return services;

        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "PIYA API", Version = "v1" });

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
                        Reference = new OpenApiReference
                            { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });

            c.OperationFilter<PIYA_API.Swagger.FileUploadOperationFilter>();
        });

        return services;
    }
}
