using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using System.Diagnostics;
using System.Reflection;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(
    PharmacyApiDbContext context,
    IConfiguration configuration,
    ILogger<HealthController> logger) : ControllerBase
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IConfiguration _configuration = configuration;
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
    /// Detailed health check with component status
    /// </summary>
    [HttpGet("detailed")]
    [AllowAnonymous]
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
        try
        {
            // Check database connectivity
            await _context.Database.CanConnectAsync();

            return Ok(new
            {
                status = "Ready",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readiness check failed");
            return StatusCode(503, new
            {
                status = "Not Ready",
                timestamp = DateTime.UtcNow,
                error = "Database connection failed"
            });
        }
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
