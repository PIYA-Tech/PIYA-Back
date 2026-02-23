using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class PermissionService(
    PharmacyApiDbContext context,
    IAuditService auditService,
    ILogger<PermissionService> logger) : IPermissionService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly ILogger<PermissionService> _logger = logger;

    #region Permission Management

    public async Task<UserPermission> GrantPermissionAsync(Guid userId, string permission, Guid grantedByUserId, string? resourceId = null, DateTime? expiresAt = null)
    {
        // Check if permission already exists
        var existing = await _context.UserPermissions
            .FirstOrDefaultAsync(p => p.UserId == userId && 
                                     p.Permission == permission && 
                                     p.ResourceId == resourceId && 
                                     p.IsActive);

        if (existing != null)
        {
            _logger.LogInformation("Permission {Permission} already granted to user {UserId}", permission, userId);
            return existing;
        }

        var userPermission = new UserPermission
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Permission = permission,
            ResourceId = resourceId,
            GrantedByUserId = grantedByUserId,
            GrantedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.UserPermissions.Add(userPermission);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "PERMISSION_GRANTED",
            "UserPermission",
            userPermission.Id.ToString(),
            grantedByUserId,
            $"Granted {permission} to user {userId}"
        );

        _logger.LogInformation("Permission {Permission} granted to user {UserId} by {GrantedBy}",
            permission, userId, grantedByUserId);

        return userPermission;
    }

    public async Task<bool> RevokePermissionAsync(Guid permissionId)
    {
        var permission = await _context.UserPermissions.FindAsync(permissionId);
        if (permission == null || !permission.IsActive)
        {
            return false;
        }

        permission.IsActive = false;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Permission {PermissionId} revoked", permissionId);

        return true;
    }

    public async Task<int> RevokeAllUserPermissionsAsync(Guid userId)
    {
        var permissions = await _context.UserPermissions
            .Where(p => p.UserId == userId && p.IsActive)
            .ToListAsync();

        foreach (var permission in permissions)
        {
            permission.IsActive = false;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Revoked {Count} permissions for user {UserId}", permissions.Count, userId);

        return permissions.Count;
    }

    public async Task<int> RevokeUserPermissionByTypeAsync(Guid userId, string permission)
    {
        var permissions = await _context.UserPermissions
            .Where(p => p.UserId == userId && p.Permission == permission && p.IsActive)
            .ToListAsync();

        foreach (var perm in permissions)
        {
            perm.IsActive = false;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Revoked {Count} {Permission} permissions for user {UserId}",
            permissions.Count, permission, userId);

        return permissions.Count;
    }

    #endregion

    #region Permission Queries

    public async Task<List<UserPermission>> GetUserPermissionsAsync(Guid userId, bool activeOnly = true)
    {
        var query = _context.UserPermissions
            .Include(p => p.User)
            .Include(p => p.GrantedBy)
            .Where(p => p.UserId == userId);

        if (activeOnly)
        {
            query = query.Where(p => p.IsActive && (p.ExpiresAt == null || p.ExpiresAt > DateTime.UtcNow));
        }

        return await query.OrderBy(p => p.Permission).ToListAsync();
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, string? resourceId = null)
    {
        // Check explicit permission
        var hasExplicit = await _context.UserPermissions
            .AnyAsync(p => p.UserId == userId &&
                          p.Permission == permission &&
                          (resourceId == null || p.ResourceId == resourceId || p.ResourceId == "All") &&
                          p.IsActive &&
                          (p.ExpiresAt == null || p.ExpiresAt > DateTime.UtcNow));

        if (hasExplicit)
        {
            return true;
        }

        // Check role-based permissions
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return false;
        }

        var rolePermissions = GetRoleDefaultPermissions(user.Role);
        return rolePermissions.Contains(permission);
    }

    public async Task<bool> HasAnyPermissionAsync(Guid userId, params string[] permissions)
    {
        foreach (var permission in permissions)
        {
            if (await HasPermissionAsync(userId, permission))
            {
                return true;
            }
        }
        return false;
    }

    public async Task<bool> HasAllPermissionsAsync(Guid userId, params string[] permissions)
    {
        foreach (var permission in permissions)
        {
            if (!await HasPermissionAsync(userId, permission))
            {
                return false;
            }
        }
        return true;
    }

    public async Task<List<User>> GetUsersWithPermissionAsync(string permission)
    {
        var userIds = await _context.UserPermissions
            .Where(p => p.Permission == permission && 
                       p.IsActive && 
                       (p.ExpiresAt == null || p.ExpiresAt > DateTime.UtcNow))
            .Select(p => p.UserId)
            .Distinct()
            .ToListAsync();

        return await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync();
    }

    #endregion

    #region Role-Based Permissions

    public async Task<List<UserPermission>> GrantRolePermissionsAsync(Guid userId, UserRole role, Guid grantedByUserId)
    {
        var permissions = GetRoleDefaultPermissions(role);
        var grantedPermissions = new List<UserPermission>();

        foreach (var permission in permissions)
        {
            var granted = await GrantPermissionAsync(userId, permission, grantedByUserId);
            grantedPermissions.Add(granted);
        }

        _logger.LogInformation("Granted {Count} role permissions for {Role} to user {UserId}",
            grantedPermissions.Count, role, userId);

        return grantedPermissions;
    }

    public List<string> GetRoleDefaultPermissions(UserRole role)
    {
        return role switch
        {
            UserRole.Patient => [],
            
            UserRole.Doctor =>
            [
                Permissions.PrescriptionCreate,
                Permissions.PrescriptionApprove,
                Permissions.DoctorUpdate // Own profile
            ],
            
            UserRole.Pharmacist =>
            [
                Permissions.InventoryUpdate,
                Permissions.PrescriptionFulfill
            ],
            
            UserRole.PharmacyManager =>
            [
                Permissions.PharmacyManage,
                Permissions.StaffManage,
                Permissions.InventoryManage,
                Permissions.PrescriptionFulfill,
                Permissions.PrescriptionViewAll
            ],
            
            UserRole.Admin =>
            [
                Permissions.SystemAdmin,
                Permissions.DashboardAccess,
                Permissions.DashboardViewAnalytics,
                Permissions.DashboardManageUsers,
                Permissions.DashboardViewAuditLogs,
                Permissions.PharmacyViewAll,
                Permissions.StaffViewAll,
                Permissions.InventoryViewAll,
                Permissions.DoctorViewAll,
                Permissions.PatientViewAll,
                Permissions.AuditLogView
            ],
            
            UserRole.SuperAdmin =>
            [
                Permissions.SystemAdmin,
                Permissions.SystemConfigUpdate,
                Permissions.DashboardAccess,
                Permissions.DashboardViewAnalytics,
                Permissions.DashboardManageUsers,
                Permissions.DashboardViewAuditLogs,
                Permissions.DashboardSystemSettings,
                Permissions.PharmacyCreate,
                Permissions.PharmacyUpdate,
                Permissions.PharmacyDelete,
                Permissions.PharmacyManage,
                Permissions.StaffManage,
                Permissions.DoctorVerify,
                Permissions.DoctorSuspend,
                Permissions.AuditLogExport
            ],
            
            _ => []
        };
    }

    public async Task<bool> HasPermissionOrRoleAsync(Guid userId, string permission, UserRole requiredRole)
    {
        // Check explicit permission first
        if (await HasPermissionAsync(userId, permission))
        {
            return true;
        }

        // Check if user has required role or higher
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return false;
        }

        return user.Role >= requiredRole;
    }

    #endregion

    #region Admin Dashboard Access

    public async Task<bool> CanAccessAdminDashboardAsync(Guid userId)
    {
        return await HasPermissionOrRoleAsync(userId, Permissions.DashboardAccess, UserRole.Admin);
    }

    public async Task<bool> CanManageUsersAsync(Guid userId)
    {
        return await HasPermissionOrRoleAsync(userId, Permissions.DashboardManageUsers, UserRole.Admin);
    }

    public async Task<bool> CanViewAuditLogsAsync(Guid userId)
    {
        return await HasPermissionOrRoleAsync(userId, Permissions.DashboardViewAuditLogs, UserRole.Admin);
    }

    public async Task<bool> CanModifySystemSettingsAsync(Guid userId)
    {
        return await HasPermissionOrRoleAsync(userId, Permissions.DashboardSystemSettings, UserRole.SuperAdmin);
    }

    #endregion
}
