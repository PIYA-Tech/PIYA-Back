using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuditController(IAuditService auditService) : ControllerBase
{
    private readonly IAuditService _auditService = auditService;

    /// <summary>
    /// Get audit logs for the current user
    /// </summary>
    [HttpGet("my-logs")]
    public async Task<ActionResult> GetMyLogs(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var logs = await _auditService.GetUserLogsAsync(userId, pageNumber, pageSize);
        
        // Return DTOs to avoid navigation property issues
        var result = logs.Select(log => new
        {
            log.Id,
            log.Action,
            log.EntityType,
            log.EntityId,
            log.Description,
            log.IpAddress,
            log.UserAgent,
            log.HttpMethod,
            log.Endpoint,
            log.StatusCode,
            log.IsSuccess,
            log.ErrorMessage,
            log.Metadata,
            log.CreatedAt
        }).ToList();
        
        return Ok(result);
    }

    /// <summary>
    /// Get audit logs for a specific user (Admin only)
    /// </summary>
    [HttpGet("user/{userId}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult> GetUserLogs(
        Guid userId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        var logs = await _auditService.GetUserLogsAsync(userId, pageNumber, pageSize);
        var result = logs.Select(log => new
        {
            log.Id,
            log.Action,
            log.EntityType,
            log.EntityId,
            log.Description,
            log.IpAddress,
            log.UserAgent,
            log.HttpMethod,
            log.Endpoint,
            log.StatusCode,
            log.IsSuccess,
            log.ErrorMessage,
            log.Metadata,
            log.CreatedAt
        }).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Get audit logs by action type (Admin only)
    /// </summary>
    [HttpGet("action/{action}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<List<AuditLog>>> GetLogsByAction(
        string action,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        var logs = await _auditService.GetLogsByActionAsync(action, pageNumber, pageSize);
        return Ok(logs);
    }

    /// <summary>
    /// Get audit logs within a date range (Admin only)
    /// </summary>
    [HttpGet("date-range")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<List<AuditLog>>> GetLogsInDateRange(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        var logs = await _auditService.GetLogsInDateRangeAsync(startDate, endDate, pageNumber, pageSize);
        return Ok(logs);
    }

    /// <summary>
    /// Get failed security events (Admin only)
    /// </summary>
    [HttpGet("security-failures")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<List<AuditLog>>> GetFailedSecurityEvents(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        var logs = await _auditService.GetFailedSecurityEventsAsync(pageNumber, pageSize);
        return Ok(logs);
    }

    /// <summary>
    /// Get all audit logs, newest first (Admin/SuperAdmin only)
    /// </summary>
    [HttpGet("all")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult> GetAllLogs(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        var logs = await _auditService.GetAllLogsAsync(pageNumber, pageSize);
        var result = logs.Select(log => new
        {
            log.Id,
            log.Action,
            log.EntityType,
            log.EntityId,
            log.Description,
            log.IpAddress,
            log.UserAgent,
            log.HttpMethod,
            log.Endpoint,
            log.StatusCode,
            log.IsSuccess,
            log.ErrorMessage,
            log.Metadata,
            log.UserId,
            log.CreatedAt
        }).ToList();
        return Ok(result);
    }
}
