using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class PharmacyStaffService(
    PharmacyApiDbContext context,
    IAuditService auditService,
    ILogger<PharmacyStaffService> logger) : IPharmacyStaffService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly ILogger<PharmacyStaffService> _logger = logger;

    #region Staff Assignment

    public async Task<PharmacyStaff> AssignStaffAsync(Guid pharmacyId, Guid userId, PharmacyStaffRole role, Guid assignedByUserId)
    {
        // Verify pharmacy exists
        var pharmacy = await _context.Pharmacies.FindAsync(pharmacyId) ?? throw new InvalidOperationException($"Pharmacy with ID {pharmacyId} not found");

        // Verify user exists and has appropriate role
        var user = await _context.Users.FindAsync(userId) ?? throw new InvalidOperationException($"User with ID {userId} not found");
        
        if (user.Role != UserRole.Pharmacist && user.Role != UserRole.PharmacyManager &&
            user.Role != UserRole.PharmacyNetworkOwner && user.Role != UserRole.Admin)
        {
            throw new InvalidOperationException($"User must have Pharmacist, PharmacyManager, PharmacyNetworkOwner, or Admin role to be assigned as pharmacy staff");
        }

        // Check if already assigned
        var now = DateTime.UtcNow;
        var existingAssignment = await _context.PharmacyStaff
            .FirstOrDefaultAsync(ps =>
                ps.PharmacyId == pharmacyId &&
                ps.UserId == userId &&
                ps.IsActive &&
                (!ps.AssignmentEndsAt.HasValue || ps.AssignmentEndsAt.Value > now));

        if (existingAssignment != null)
        {
            throw new InvalidOperationException($"User is already assigned to this pharmacy");
        }

        // Create staff assignment
        var staffAssignment = new PharmacyStaff
        {
            Id = Guid.NewGuid(),
            PharmacyId = pharmacyId,
            UserId = userId,
            Role = role,
            IsActive = true,
            AssignedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.PharmacyStaff.Add(staffAssignment);
        await _context.SaveChangesAsync();

        // Audit log
        await _auditService.LogEntityActionAsync(
            "STAFF_ASSIGNED",
            "PharmacyStaff",
            staffAssignment.Id.ToString(),
            assignedByUserId,
            $"User {user.Username} assigned as {role} to pharmacy {pharmacy.Name}"
        );

        _logger.LogInformation("User {UserId} assigned as {Role} to pharmacy {PharmacyId} by {AssignedBy}",
            userId, role, pharmacyId, assignedByUserId);

        return staffAssignment;
    }

    public async Task<bool> RemoveStaffAsync(Guid pharmacyId, Guid userId)
    {
        var staffAssignment = await _context.PharmacyStaff
            .FirstOrDefaultAsync(ps => ps.PharmacyId == pharmacyId && ps.UserId == userId && ps.IsActive);

        if (staffAssignment == null)
        {
            return false;
        }

        staffAssignment.IsActive = false;
        staffAssignment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} removed from pharmacy {PharmacyId}", userId, pharmacyId);

        return true;
    }

    public async Task<PharmacyStaff> UpdateStaffAsync(Guid staffId, PharmacyStaffRole? newRole = null, string? workSchedule = null, List<string>? permissions = null)
    {
        var staffAssignment = await _context.PharmacyStaff.FindAsync(staffId) ?? throw new InvalidOperationException($"Staff assignment with ID {staffId} not found");
        
        if (newRole.HasValue)
        {
            staffAssignment.Role = newRole.Value;
        }

        if (workSchedule != null)
        {
            staffAssignment.WorkSchedule = workSchedule;
        }

        if (permissions != null)
        {
            staffAssignment.Permissions = permissions;
        }

        staffAssignment.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Staff assignment {StaffId} updated", staffId);

        return staffAssignment;
    }

    #endregion

    #region Staff Queries

    public async Task<List<PharmacyStaff>> GetPharmacyStaffAsync(Guid pharmacyId, bool activeOnly = true)
    {
        var query = _context.PharmacyStaff
            .Include(ps => ps.User)
            .Include(ps => ps.Pharmacy)
            .Where(ps => ps.PharmacyId == pharmacyId);

        if (activeOnly)
        {
            var now = DateTime.UtcNow;
            query = query.Where(ps =>
                ps.IsActive &&
                (!ps.AssignmentEndsAt.HasValue || ps.AssignmentEndsAt.Value > now));
        }

        return await query.OrderBy(ps => ps.Role).ThenBy(ps => ps.AssignedAt).ToListAsync();
    }

    public async Task<List<PharmacyStaff>> GetUserPharmaciesAsync(Guid userId, bool activeOnly = true)
    {
        var query = _context.PharmacyStaff
            .Include(ps => ps.User)
            .Include(ps => ps.Pharmacy)
            .Where(ps => ps.UserId == userId);

        if (activeOnly)
        {
            var now = DateTime.UtcNow;
            query = query.Where(ps =>
                ps.IsActive &&
                (!ps.AssignmentEndsAt.HasValue || ps.AssignmentEndsAt.Value > now));
        }

        return await query.OrderBy(ps => ps.Pharmacy.Name).ToListAsync();
    }

    public async Task<PharmacyStaff?> GetStaffAssignmentAsync(Guid staffId)
    {
        return await _context.PharmacyStaff
            .Include(ps => ps.User)
            .Include(ps => ps.Pharmacy)
            .FirstOrDefaultAsync(ps => ps.Id == staffId);
    }

    public async Task<bool> IsStaffAtPharmacyAsync(Guid pharmacyId, Guid userId)
    {
        var now = DateTime.UtcNow;
        return await _context.PharmacyStaff
            .AnyAsync(ps =>
                ps.PharmacyId == pharmacyId &&
                ps.UserId == userId &&
                ps.IsActive &&
                (!ps.AssignmentEndsAt.HasValue || ps.AssignmentEndsAt.Value > now));
    }

    public async Task<bool> IsManagerAtPharmacyAsync(Guid pharmacyId, Guid userId)
    {
        var now = DateTime.UtcNow;
        return await _context.PharmacyStaff
            .AnyAsync(ps =>
                ps.PharmacyId == pharmacyId &&
                ps.UserId == userId &&
                ps.Role == PharmacyStaffRole.Manager &&
                ps.IsActive &&
                (!ps.AssignmentEndsAt.HasValue || ps.AssignmentEndsAt.Value > now));
    }

    public async Task<PharmacyStaff?> GetPharmacyManagerAsync(Guid pharmacyId)
    {
        var now = DateTime.UtcNow;
        return await _context.PharmacyStaff
            .Include(ps => ps.User)
            .Include(ps => ps.Pharmacy)
            .FirstOrDefaultAsync(ps =>
                ps.PharmacyId == pharmacyId &&
                ps.Role == PharmacyStaffRole.Manager &&
                ps.IsActive &&
                (!ps.AssignmentEndsAt.HasValue || ps.AssignmentEndsAt.Value > now));
    }

    #endregion

    #region Manager Operations

    public async Task<PharmacyStaff> AssignManagerAsync(Guid pharmacyId, Guid userId, Guid assignedByUserId)
    {
        // Check if there's already a manager
        var existingManager = await GetPharmacyManagerAsync(pharmacyId);
        if (existingManager != null)
        {
            throw new InvalidOperationException($"Pharmacy already has a manager. Use TransferManagementAsync to change managers.");
        }

        // Assign as manager
        return await AssignStaffAsync(pharmacyId, userId, PharmacyStaffRole.Manager, assignedByUserId);
    }

    public async Task<PharmacyStaff> TransferManagementAsync(Guid pharmacyId, Guid newManagerUserId, Guid performedByUserId)
    {
        // Deactivate current manager
        var currentManager = await GetPharmacyManagerAsync(pharmacyId);
        if (currentManager != null)
        {
            currentManager.IsActive = false;
            currentManager.UpdatedAt = DateTime.UtcNow;
        }

        // Assign new manager
        var newManager = await AssignStaffAsync(pharmacyId, newManagerUserId, PharmacyStaffRole.Manager, performedByUserId);

        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "MANAGER_TRANSFERRED",
            "PharmacyStaff",
            pharmacyId.ToString(),
            performedByUserId,
            $"Management transferred to user {newManagerUserId}"
        );

        _logger.LogInformation("Management of pharmacy {PharmacyId} transferred from {OldManager} to {NewManager}",
            pharmacyId, currentManager?.UserId, newManagerUserId);

        return newManager;
    }

    #endregion
}
