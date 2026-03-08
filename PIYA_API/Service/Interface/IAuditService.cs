using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for audit logging of healthcare transactions and security events
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Log an action to the audit trail
    /// </summary>
    Task LogAsync(AuditLog auditLog);
    
    /// <summary>
    /// Log a simple action
    /// </summary>
    Task LogActionAsync(string action, Guid? userId = null, string? description = null);
    
    /// <summary>
    /// Log an entity-related action
    /// </summary>
    Task LogEntityActionAsync(string action, string entityType, string entityId, Guid? userId = null, string? description = null);
    
    /// <summary>
    /// Log a security event (login, logout, failed attempts)
    /// </summary>
    Task LogSecurityEventAsync(string action, Guid? userId, string? ipAddress, string? userAgent, bool isSuccess, string? errorMessage = null);
    
    /// <summary>
    /// Log an HTTP request
    /// </summary>
    Task LogHttpRequestAsync(string method, string endpoint, int statusCode, Guid? userId, string? ipAddress, string? userAgent);
    
    /// <summary>
    /// Get audit logs for a specific user
    /// </summary>
    Task<List<AuditLog>> GetUserLogsAsync(Guid userId, int pageNumber = 1, int pageSize = 50);
    
    /// <summary>
    /// Get audit logs by action type
    /// </summary>
    Task<List<AuditLog>> GetLogsByActionAsync(string action, int pageNumber = 1, int pageSize = 50);
    
    /// <summary>
    /// Get audit logs within a date range
    /// </summary>
    Task<List<AuditLog>> GetLogsInDateRangeAsync(DateTime startDate, DateTime endDate, int pageNumber = 1, int pageSize = 50);
    
    /// <summary>
    /// Get failed security events
    /// </summary>
    Task<List<AuditLog>> GetFailedSecurityEventsAsync(int pageNumber = 1, int pageSize = 50);

    /// <summary>
    /// Get all audit logs (admin)
    /// </summary>
    Task<List<AuditLog>> GetAllLogsAsync(int pageNumber = 1, int pageSize = 50);
}
