using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PermissionsController(
    IPermissionService permissionService,
    ILogger<PermissionsController> logger) : ControllerBase
{
    private readonly IPermissionService _permissionService = permissionService;
    private readonly ILogger<PermissionsController> _logger = logger;

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    #region Permission Management

    /// <summary>
    /// Grant permission to user
    /// </summary>
    [HttpPost("grant")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<UserPermission>> GrantPermission([FromBody] GrantPermissionRequest request)
    {
        try
        {
            var grantedByUserId = GetUserId();
            var permission = await _permissionService.GrantPermissionAsync(
                request.UserId,
                request.Permission,
                grantedByUserId,
                request.ResourceId,
                request.ExpiresAt);

            return CreatedAtAction(nameof(GetUserPermissions), new { userId = request.UserId }, permission);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error granting permission");
            return StatusCode(500, new { error = "Failed to grant permission" });
        }
    }

    /// <summary>
    /// Revoke permission
    /// </summary>
    [HttpDelete("{permissionId}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult> RevokePermission(Guid permissionId)
    {
        try
        {
            var revoked = await _permissionService.RevokePermissionAsync(permissionId);
            if (!revoked)
            {
                return NotFound(new { error = "Permission not found" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking permission");
            return StatusCode(500, new { error = "Failed to revoke permission" });
        }
    }

    /// <summary>
    /// Revoke all permissions for user
    /// </summary>
    [HttpDelete("user/{userId}/all")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult> RevokeAllUserPermissions(Guid userId)
    {
        try
        {
            var count = await _permissionService.RevokeAllUserPermissionsAsync(userId);
            return Ok(new { revokedCount = count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking all permissions");
            return StatusCode(500, new { error = "Failed to revoke permissions" });
        }
    }

    #endregion

    #region Permission Queries

    /// <summary>
    /// Get all permissions for a user
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<List<UserPermission>>> GetUserPermissions(
        Guid userId,
        [FromQuery] bool activeOnly = true)
    {
        try
        {
            var currentUserId = GetUserId();
            
            // Users can only see their own permissions unless they're admin
            if (userId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var permissions = await _permissionService.GetUserPermissionsAsync(userId, activeOnly);
            return Ok(permissions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user permissions");
            return StatusCode(500, new { error = "Failed to retrieve permissions" });
        }
    }

    /// <summary>
    /// Check if user has specific permission
    /// </summary>
    [HttpGet("check")]
    public async Task<ActionResult<PermissionCheckResponse>> CheckPermission(
        [FromQuery] Guid userId,
        [FromQuery] string permission,
        [FromQuery] string? resourceId = null)
    {
        try
        {
            var hasPermission = await _permissionService.HasPermissionAsync(userId, permission, resourceId);
            return Ok(new PermissionCheckResponse
            {
                UserId = userId,
                Permission = permission,
                ResourceId = resourceId,
                HasPermission = hasPermission
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking permission");
            return StatusCode(500, new { error = "Failed to check permission" });
        }
    }

    /// <summary>
    /// Get all users with specific permission
    /// </summary>
    [HttpGet("permission/{permission}/users")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<List<User>>> GetUsersWithPermission(string permission)
    {
        try
        {
            var users = await _permissionService.GetUsersWithPermissionAsync(permission);
            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users with permission");
            return StatusCode(500, new { error = "Failed to retrieve users" });
        }
    }

    #endregion

    #region Role Permissions

    /// <summary>
    /// Grant role-based permissions to user
    /// </summary>
    [HttpPost("grant-role-permissions")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<List<UserPermission>>> GrantRolePermissions([FromBody] GrantRolePermissionsRequest request)
    {
        try
        {
            var grantedByUserId = GetUserId();
            var permissions = await _permissionService.GrantRolePermissionsAsync(
                request.UserId,
                request.Role,
                grantedByUserId);

            return Ok(permissions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error granting role permissions");
            return StatusCode(500, new { error = "Failed to grant role permissions" });
        }
    }

    /// <summary>
    /// Get default permissions for a role
    /// </summary>
    [HttpGet("role/{role}/defaults")]
    public ActionResult<List<string>> GetRoleDefaultPermissions(UserRole role)
    {
        try
        {
            var permissions = _permissionService.GetRoleDefaultPermissions(role);
            return Ok(permissions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving role permissions");
            return StatusCode(500, new { error = "Failed to retrieve role permissions" });
        }
    }

    #endregion

    #region Admin Dashboard Access

    /// <summary>
    /// Check if user can access admin dashboard
    /// </summary>
    [HttpGet("user/{userId}/can-access-dashboard")]
    [Authorize(Roles = "Admin,PharmacyManager")]
    public async Task<ActionResult<AccessCheckResponse>> CanAccessDashboard(Guid userId)
    {
        try
        {
            var canAccess = await _permissionService.CanAccessAdminDashboardAsync(userId);
            return Ok(new AccessCheckResponse { CanAccess = canAccess });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking dashboard access");
            return StatusCode(500, new { error = "Failed to check access" });
        }
    }

    /// <summary>
    /// Check if user can manage other users
    /// </summary>
    [HttpGet("user/{userId}/can-manage-users")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AccessCheckResponse>> CanManageUsers(Guid userId)
    {
        try
        {
            var canManage = await _permissionService.CanManageUsersAsync(userId);
            return Ok(new AccessCheckResponse { CanAccess = canManage });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking user management access");
            return StatusCode(500, new { error = "Failed to check access" });
        }
    }

    /// <summary>
    /// Check if user can view audit logs
    /// </summary>
    [HttpGet("user/{userId}/can-view-audit-logs")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AccessCheckResponse>> CanViewAuditLogs(Guid userId)
    {
        try
        {
            var canView = await _permissionService.CanViewAuditLogsAsync(userId);
            return Ok(new AccessCheckResponse { CanAccess = canView });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking audit log access");
            return StatusCode(500, new { error = "Failed to check access" });
        }
    }

    /// <summary>
    /// Check if user can modify system settings
    /// </summary>
    [HttpGet("user/{userId}/can-modify-settings")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<AccessCheckResponse>> CanModifySystemSettings(Guid userId)
    {
        try
        {
            var canModify = await _permissionService.CanModifySystemSettingsAsync(userId);
            return Ok(new AccessCheckResponse { CanAccess = canModify });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking system settings access");
            return StatusCode(500, new { error = "Failed to check access" });
        }
    }

    #endregion

    #region Available Permissions List

    /// <summary>
    /// Get list of all available permissions
    /// </summary>
    [HttpGet("available")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public ActionResult<List<string>> GetAvailablePermissions()
    {
        var permissions = typeof(Permissions)
            .GetFields()
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .Select(f => f.GetValue(null)?.ToString() ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        return Ok(permissions);
    }

    #endregion
}

#region DTOs

public class GrantPermissionRequest
{
    public Guid UserId { get; set; }
    public required string Permission { get; set; }
    public string? ResourceId { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class GrantRolePermissionsRequest
{
    public Guid UserId { get; set; }
    public UserRole Role { get; set; }
}

public class PermissionCheckResponse
{
    public Guid UserId { get; set; }
    public required string Permission { get; set; }
    public string? ResourceId { get; set; }
    public bool HasPermission { get; set; }
}

public class AccessCheckResponse
{
    public bool CanAccess { get; set; }
}

#endregion
