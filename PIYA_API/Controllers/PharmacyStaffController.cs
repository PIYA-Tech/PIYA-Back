using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PharmacyStaffController(
    IPharmacyStaffService staffService,
    IPermissionService permissionService,
    ILogger<PharmacyStaffController> logger) : ControllerBase
{
    private readonly IPharmacyStaffService _staffService = staffService;
    private readonly IPermissionService _permissionService = permissionService;
    private readonly ILogger<PharmacyStaffController> _logger = logger;

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    #region Staff Assignment

    /// <summary>
    /// Assign a user to pharmacy staff
    /// </summary>
    [HttpPost("assign")]
    [Authorize(Roles = "Admin,PharmacyManager")]
    public async Task<ActionResult<PharmacyStaff>> AssignStaff([FromBody] AssignStaffRequest request)
    {
        try
        {
            var userId = GetUserId();
            
            // Check if user has permission to assign staff to this pharmacy
            var canAssign = await _permissionService.HasPermissionAsync(userId, Permissions.StaffAssign, request.PharmacyId.ToString());
            if (!canAssign && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var staff = await _staffService.AssignStaffAsync(
                request.PharmacyId,
                request.UserId,
                request.Role,
                userId);

            return CreatedAtAction(nameof(GetStaffAssignment), new { id = staff.Id }, staff);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning staff");
            return StatusCode(500, new { error = "Failed to assign staff" });
        }
    }

    /// <summary>
    /// Remove staff member from pharmacy
    /// </summary>
    [HttpDelete("remove")]
    [Authorize(Roles = "Admin,PharmacyManager")]
    public async Task<ActionResult> RemoveStaff([FromQuery] Guid pharmacyId, [FromQuery] Guid userId)
    {
        try
        {
            var currentUserId = GetUserId();
            
            var canRemove = await _permissionService.HasPermissionAsync(currentUserId, Permissions.StaffRemove, pharmacyId.ToString());
            if (!canRemove && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var removed = await _staffService.RemoveStaffAsync(pharmacyId, userId);
            if (!removed)
            {
                return NotFound(new { error = "Staff assignment not found" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing staff");
            return StatusCode(500, new { error = "Failed to remove staff" });
        }
    }

    /// <summary>
    /// Update staff details
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,PharmacyManager")]
    public async Task<ActionResult<PharmacyStaff>> UpdateStaff(
        Guid id,
        [FromBody] UpdateStaffRequest request)
    {
        try
        {
            var staff = await _staffService.UpdateStaffAsync(
                id,
                request.Role,
                request.WorkSchedule,
                request.Permissions);

            return Ok(staff);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating staff");
            return StatusCode(500, new { error = "Failed to update staff" });
        }
    }

    #endregion

    #region Staff Queries

    /// <summary>
    /// Get all staff for a pharmacy
    /// </summary>
    [HttpGet("pharmacy/{pharmacyId}")]
    [Authorize(Roles = "Admin,PharmacyManager,Pharmacist")]
    public async Task<ActionResult<List<PharmacyStaff>>> GetPharmacyStaff(
        Guid pharmacyId,
        [FromQuery] bool activeOnly = true)
    {
        try
        {
            var staff = await _staffService.GetPharmacyStaffAsync(pharmacyId, activeOnly);
            return Ok(staff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pharmacy staff");
            return StatusCode(500, new { error = "Failed to retrieve staff" });
        }
    }

    /// <summary>
    /// Get pharmacies where user is staff
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<List<PharmacyStaff>>> GetUserPharmacies(
        Guid userId,
        [FromQuery] bool activeOnly = true)
    {
        try
        {
            var currentUserId = GetUserId();
            
            // Users can only see their own pharmacies unless they're admin
            if (userId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var pharmacies = await _staffService.GetUserPharmaciesAsync(userId, activeOnly);
            return Ok(pharmacies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user pharmacies");
            return StatusCode(500, new { error = "Failed to retrieve pharmacies" });
        }
    }

    /// <summary>
    /// Get staff assignment by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PharmacyStaff>> GetStaffAssignment(Guid id)
    {
        try
        {
            var staff = await _staffService.GetStaffAssignmentAsync(id);
            if (staff == null)
            {
                return NotFound();
            }

            return Ok(staff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving staff assignment");
            return StatusCode(500, new { error = "Failed to retrieve staff assignment" });
        }
    }

    /// <summary>
    /// Get pharmacy manager
    /// </summary>
    [HttpGet("pharmacy/{pharmacyId}/manager")]
    public async Task<ActionResult<PharmacyStaff>> GetPharmacyManager(Guid pharmacyId)
    {
        try
        {
            var manager = await _staffService.GetPharmacyManagerAsync(pharmacyId);
            if (manager == null)
            {
                return NotFound(new { error = "No manager assigned to this pharmacy" });
            }

            return Ok(manager);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pharmacy manager");
            return StatusCode(500, new { error = "Failed to retrieve manager" });
        }
    }

    #endregion

    #region Manager Operations

    /// <summary>
    /// Assign manager to pharmacy
    /// </summary>
    [HttpPost("assign-manager")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PharmacyStaff>> AssignManager([FromBody] AssignManagerRequest request)
    {
        try
        {
            var userId = GetUserId();
            var manager = await _staffService.AssignManagerAsync(request.PharmacyId, request.UserId, userId);

            return CreatedAtAction(nameof(GetPharmacyManager), new { pharmacyId = request.PharmacyId }, manager);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning manager");
            return StatusCode(500, new { error = "Failed to assign manager" });
        }
    }

    /// <summary>
    /// Transfer pharmacy management
    /// </summary>
    [HttpPost("transfer-management")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PharmacyStaff>> TransferManagement([FromBody] TransferManagementRequest request)
    {
        try
        {
            var userId = GetUserId();
            var newManager = await _staffService.TransferManagementAsync(
                request.PharmacyId,
                request.NewManagerUserId,
                userId);

            return Ok(newManager);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transferring management");
            return StatusCode(500, new { error = "Failed to transfer management" });
        }
    }

    #endregion
}

#region DTOs

public class AssignStaffRequest
{
    public Guid PharmacyId { get; set; }
    public Guid UserId { get; set; }
    public PharmacyStaffRole Role { get; set; } = PharmacyStaffRole.Staff;
}

public class UpdateStaffRequest
{
    public PharmacyStaffRole? Role { get; set; }
    public string? WorkSchedule { get; set; }
    public List<string>? Permissions { get; set; }
}

public class AssignManagerRequest
{
    public Guid PharmacyId { get; set; }
    public Guid UserId { get; set; }
}

public class TransferManagementRequest
{
    public Guid PharmacyId { get; set; }
    public Guid NewManagerUserId { get; set; }
}

#endregion
