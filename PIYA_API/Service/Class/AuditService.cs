using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class AuditService(PharmacyApiDbContext context, ILogger<AuditService> logger, IOptions<FeaturesOptions> features) : IAuditService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly ILogger<AuditService> _logger = logger;
    private readonly bool _enabled = features?.Value?.EnableAuditLogging ?? true;

    public async Task LogAsync(AuditLog auditLog)
    {
        // Respect feature flag: if audit logging is disabled in configuration, no-op
        if (!_enabled)
            return;

        try
        {
            auditLog.CreatedAt = DateTime.UtcNow;
            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Don't throw exceptions from audit logging - just log them
            _logger.LogError(ex, "Failed to save audit log: {Action}", auditLog.Action);
        }
    }

    public async Task LogActionAsync(string action, Guid? userId = null, string? description = null)
    {
        var auditLog = new AuditLog
        {
            Action = action,
            UserId = userId,
            Description = description,
            IsSuccess = true
        };

        await LogAsync(auditLog);
    }

    public async Task LogEntityActionAsync(string action, string entityType, string entityId, Guid? userId = null, string? description = null)
    {
        var auditLog = new AuditLog
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            UserId = userId,
            Description = description,
            IsSuccess = true
        };

        await LogAsync(auditLog);
    }

    public async Task LogSecurityEventAsync(string action, Guid? userId, string? ipAddress, string? userAgent, bool isSuccess, string? errorMessage = null)
    {
        var auditLog = new AuditLog
        {
            Action = action,
            UserId = userId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            IsSuccess = isSuccess,
            ErrorMessage = errorMessage,
            Description = isSuccess ? $"Security event: {action}" : $"Failed security event: {action}"
        };

        await LogAsync(auditLog);
    }

    public async Task LogHttpRequestAsync(string method, string endpoint, int statusCode, Guid? userId, string? ipAddress, string? userAgent)
    {
        var auditLog = new AuditLog
        {
            Action = "HttpRequest",
            HttpMethod = method,
            Endpoint = endpoint,
            StatusCode = statusCode,
            UserId = userId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            IsSuccess = statusCode >= 200 && statusCode < 400
        };

        await LogAsync(auditLog);
    }

    public async Task<List<AuditLog>> GetUserLogsAsync(Guid userId, int pageNumber = 1, int pageSize = 50)
    {
        return await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<AuditLog>> GetLogsByActionAsync(string action, int pageNumber = 1, int pageSize = 50)
    {
        var logs = await _context.AuditLogs
            .Include(a => a.User)
            .Where(a => a.Action == action)
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync();

        // Strip sensitive fields from loaded User navigation before returning
        foreach (var log in logs.Where(l => l.User != null))
            log.User!.PasswordHash = string.Empty;

        return logs;
    }

    public async Task<List<AuditLog>> GetLogsInDateRangeAsync(DateTime startDate, DateTime endDate, int pageNumber = 1, int pageSize = 50)
    {
        var logs = await _context.AuditLogs
            .Include(a => a.User)
            .Where(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate)
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync();

        foreach (var log in logs.Where(l => l.User != null))
            log.User!.PasswordHash = string.Empty;

        return logs;
    }

    public async Task<List<AuditLog>> GetFailedSecurityEventsAsync(int pageNumber = 1, int pageSize = 50)
    {
        var logs = await _context.AuditLogs
            .Include(a => a.User)
            .Where(a => !a.IsSuccess && (a.Action.Contains("Login") || a.Action.Contains("Auth") || a.Action.Contains("2FA")))
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync();

        foreach (var log in logs.Where(l => l.User != null))
            log.User!.PasswordHash = string.Empty;

        return logs;
    }

    public async Task<List<AuditLog>> GetAllLogsAsync(int pageNumber = 1, int pageSize = 50)
    {
        var logs = await _context.AuditLogs
            .OrderByDescending(a => a.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync();

        return logs;
    }
}
