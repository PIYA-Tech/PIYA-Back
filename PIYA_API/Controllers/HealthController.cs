using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using PIYA_API.Data;
using System.Diagnostics;
using System.Reflection;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(
    PharmacyApiDbContext context,
    IConfiguration configuration,
    IDistributedCache cache,
    ILogger<HealthController> logger) : ControllerBase
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IConfiguration _configuration = configuration;
    private readonly IDistributedCache _cache = cache;
    private readonly ILogger<HealthController> _logger = logger;

    /// <summary>
    /// Basic health check - returns 200 OK if service is running
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow,
            service = "PIYA Health API"
        });
    }

    /// <summary>
    /// Detailed health check with component status (Admin only)
    /// </summary>
    [HttpGet("detailed")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetDetailed()
    {
        var healthChecks = new Dictionary<string, object>();
        var overallStatus = "Healthy";
        var startTime = DateTime.UtcNow;

        // 1. Database Health
        try
        {
            var dbStart = Stopwatch.StartNew();
            await _context.Database.ExecuteSqlRawAsync("SELECT 1");
            dbStart.Stop();

            healthChecks["database"] = new
            {
                status = "Healthy",
                responseTime = $"{dbStart.ElapsedMilliseconds}ms",
                provider = "PostgreSQL"
            };
        }
        catch (Exception ex)
        {
            overallStatus = "Unhealthy";
            healthChecks["database"] = new
            {
                status = "Unhealthy",
                error = ex.Message
            };
            _logger.LogError(ex, "Database health check failed");
        }

        // 2. Application Info
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "Unknown";
        var buildDate = new FileInfo(assembly.Location).LastWriteTime;

        healthChecks["application"] = new
        {
            status = "Healthy",
            version,
            buildDate,
            environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production",
            dotnetVersion = Environment.Version.ToString()
        };

        // 3. Memory Usage
        var process = Process.GetCurrentProcess();
        var memoryUsedMB = process.WorkingSet64 / 1024 / 1024;
        var memoryStatus = memoryUsedMB > 500 ? "Warning" : "Healthy";

        healthChecks["memory"] = new
        {
            status = memoryStatus,
            usedMB = memoryUsedMB,
            totalMB = GC.GetTotalMemory(false) / 1024 / 1024
        };

        if (memoryStatus == "Warning" && overallStatus == "Healthy")
        {
            overallStatus = "Degraded";
        }

        // 4. Database Statistics
        try
        {
            var stats = new
            {
                users = await _context.Users.CountAsync(),
                appointments = await _context.Appointments.CountAsync(),
                prescriptions = await _context.Prescriptions.CountAsync(),
                pharmacies = await _context.Pharmacies.CountAsync(),
                medications = await _context.Medications.CountAsync()
            };

            healthChecks["database_statistics"] = new
            {
                status = "Healthy",
                counts = stats
            };
        }
        catch (Exception ex)
        {
            healthChecks["database_statistics"] = new
            {
                status = "Failed",
                error = ex.Message
            };
        }

        // 5. Redis / Distributed Cache Health
        try
        {
            var cacheStart = Stopwatch.StartNew();
            var testKey = "__health_check__";
            await _cache.SetStringAsync(testKey, "ok",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5) });
            var readBack = await _cache.GetStringAsync(testKey);
            cacheStart.Stop();

            var cacheProvider = _configuration["Caching:Provider"] ?? "Memory";
            healthChecks["cache"] = new
            {
                status = readBack == "ok" ? "Healthy" : "Degraded",
                responseTime = $"{cacheStart.ElapsedMilliseconds}ms",
                provider = cacheProvider
            };
        }
        catch (Exception ex)
        {
            if (overallStatus == "Healthy") overallStatus = "Degraded";
            healthChecks["cache"] = new
            {
                status = "Unhealthy",
                error = ex.Message,
                provider = _configuration["Caching:Provider"] ?? "Memory"
            };
            _logger.LogError(ex, "Cache health check failed");
        }

        var totalTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

        return Ok(new
        {
            status = overallStatus,
            timestamp = DateTime.UtcNow,
            service = "PIYA Health API",
            totalCheckTime = $"{totalTime}ms",
            checks = healthChecks
        });
    }

    /// <summary>
    /// Readiness probe - returns 200 when service is ready to accept traffic
    /// </summary>
    [HttpGet("ready")]
    [AllowAnonymous]
    public async Task<IActionResult> GetReadiness()
    {
        var checks = new Dictionary<string, string>();
        var ready = true;

        // Check database connectivity and verify that the deployed schema matches
        // the application. A successful connection alone is not enough: endpoints
        // can still fail at runtime when a migration has not been applied.
        try
        {
            var canConnect = await _context.Database.CanConnectAsync();
            if (!canConnect)
            {
                checks["database"] = "failed";
                ready = false;
            }
            else
            {
                checks["database"] = "ok";

                var pendingMigrations = (await _context.Database.GetPendingMigrationsAsync()).ToArray();
                if (pendingMigrations.Length == 0)
                {
                    checks["migrations"] = "ok";
                }
                else
                {
                    checks["migrations"] = $"pending:{pendingMigrations.Length}";
                    ready = false;
                    _logger.LogError(
                        "Readiness: {MigrationCount} database migration(s) are pending: {PendingMigrations}",
                        pendingMigrations.Length,
                        string.Join(", ", pendingMigrations));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readiness: database check failed");
            checks["database"] = "failed";
            ready = false;
        }

        // Check distributed cache connectivity
        try
        {
            await _cache.SetStringAsync("__ready__", "1",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5) });
            checks["cache"] = "ok";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readiness: cache check failed");
            checks["cache"] = "failed";
            ready = false;
        }

        if (ready)
        {
            return Ok(new
            {
                status = "Ready",
                timestamp = DateTime.UtcNow,
                checks
            });
        }

        return StatusCode(503, new
        {
            status = "Not Ready",
            timestamp = DateTime.UtcNow,
            checks
        });
    }

    /// <summary>
    /// Liveness probe - returns 200 if process is alive
    /// </summary>
    [HttpGet("live")]
    [AllowAnonymous]
    public IActionResult GetLiveness()
    {
        return Ok(new
        {
            status = "Alive",
            timestamp = DateTime.UtcNow,
            uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"dd\.hh\:mm\:ss")
        });
    }

    /// <summary>
    /// Version information
    /// </summary>
    [HttpGet("version")]
    [AllowAnonymous]
    public IActionResult GetVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var buildDate = new FileInfo(assembly.Location).LastWriteTime;

        return Ok(new
        {
            version = version?.ToString() ?? "Unknown",
            buildDate,
            environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production",
            framework = "ASP.NET Core 9.0",
            database = "PostgreSQL"
        });
    }
}
