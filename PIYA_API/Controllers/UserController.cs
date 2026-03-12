using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UserController(IUserService userService, ILogger<UserController> logger) : ControllerBase
{
    private readonly IUserService _userService = userService;
    private readonly ILogger<UserController> _logger = logger;

    private bool IsAdminOrSuperAdmin() =>
        User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/user — list all users (Admin / SuperAdmin)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Get all users — Admin and SuperAdmin only.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            // Fetch all known roles and union the results
            var roles = Enum.GetValues<UserRole>();
            var all = new List<User>();
            foreach (var role in roles)
                all.AddRange(await _userService.GetUsersByRoleAsync(role));

            var distinct = all.DistinctBy(u => u.Id).OrderBy(u => u.CreatedAt);

            return Ok(distinct.Select(u => new UserResponse
            {
                Id          = u.Id,
                Username    = u.Username,
                Email       = u.Email,
                FirstName   = u.FirstName,
                MiddleName  = u.MiddleName,
                LastName    = u.LastName,
                PhoneNumber = u.PhoneNumber,
                DateOfBirth = u.DateOfBirth,
                Role        = u.Role.ToString(),
                IsActive    = u.IsActive,
                IsEmailVerified = u.IsEmailVerified,
                CreatedAt   = u.CreatedAt,
                UpdatedAt   = u.UpdatedAt,
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing all users");
            return StatusCode(500, new { message = "An error occurred while listing users" });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/user — create user with any role (Admin / SuperAdmin)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a new user with a specified role — Admin and SuperAdmin only.
    /// Use this to provision Doctors, Pharmacists, other Admins, etc.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        try
        {
            if (!Enum.TryParse<UserRole>(request.Role, true, out var roleEnum))
                return BadRequest(new { message = $"Invalid role '{request.Role}'. Valid values: {string.Join(", ", Enum.GetNames<UserRole>())}" });

            // SuperAdmin creation is restricted to SuperAdmin only
            if (roleEnum == UserRole.SuperAdmin && !User.IsInRole("SuperAdmin"))
                return Forbid();

            DateTime? parsedDob = null;
            if (!string.IsNullOrWhiteSpace(request.DateOfBirth))
            {
                if (!DateTime.TryParse(request.DateOfBirth, out var dob))
                    return BadRequest(new { message = "Invalid DateOfBirth format. Expected ISO 8601 (yyyy-MM-dd)." });
                parsedDob = dob;
            }

            var user = new User
            {
                Username        = request.Username,
                Email           = request.Email,
                FirstName       = request.FirstName,
                MiddleName      = request.MiddleName,
                LastName        = request.LastName,
                PhoneNumber     = request.PhoneNumber,
                DateOfBirth     = parsedDob,
                Role            = roleEnum,
                IsEmailVerified = true,   // admin-created accounts skip email verification
                IsActive        = true,
            };

            var created = await _userService.Create(user, request.Password);

            _logger.LogInformation("User {Username} (role: {Role}) created by {Admin}",
                created.Username, created.Role, User.Identity?.Name);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, new UserResponse
            {
                Id          = created.Id,
                Username    = created.Username,
                Email       = created.Email,
                FirstName   = created.FirstName,
                MiddleName  = created.MiddleName,
                LastName    = created.LastName,
                PhoneNumber = created.PhoneNumber,
                DateOfBirth = created.DateOfBirth,
                Role        = created.Role.ToString(),
                IsActive    = created.IsActive,
                IsEmailVerified = created.IsEmailVerified,
                CreatedAt   = created.CreatedAt,
                UpdatedAt   = created.UpdatedAt,
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user");
            return StatusCode(500, new { message = "An error occurred while creating the user" });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/user/{id}
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var callerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            if (callerId != id && !User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
                return Forbid();

            var user = await _userService.GetById(id);
            
            return Ok(new UserResponse
            {
                Id              = user.Id,
                Username        = user.Username,
                Email           = user.Email,
                FirstName       = user.FirstName,
                MiddleName      = user.MiddleName,
                LastName        = user.LastName,
                PhoneNumber     = user.PhoneNumber,
                DateOfBirth     = user.DateOfBirth,
                Role            = user.Role.ToString(),
                IsActive        = user.IsActive,
                IsEmailVerified = user.IsEmailVerified,
                CreatedAt       = user.CreatedAt,
                UpdatedAt       = user.UpdatedAt,
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving the user" });
        }
    }

    // Fix the inline role checks that previously only tested "Admin"
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            var callerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            if (callerId != id && !IsAdminOrSuperAdmin())
                return Forbid();
            var user = new User
            {
                Id = id,
                Username = request.Username,
                Email = request.Email,
                FirstName = request.FirstName,
                MiddleName = request.MiddleName,
                LastName = request.LastName,
                PhoneNumber = request.PhoneNumber,
                DateOfBirth = request.DateOfBirth,
            };

            await _userService.Update(user, request.Password);

            return Ok(new { message = "User updated successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while updating the user" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var callerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            if (callerId != id && !IsAdminOrSuperAdmin())
                return Forbid();
            await _userService.Delete(id);
            return Ok(new { message = "User deleted successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the user" });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PATCH /api/user/{id}/active — activate or deactivate a user
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Set a user's active status. Admin/SuperAdmin only.
    /// Body: { "isActive": true|false }
    /// </summary>
    [HttpPatch("{id}/active")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetActiveRequest request)
    {
        try
        {
            await _userService.SetActiveAsync(id, request.IsActive);
            var state = request.IsActive ? "activated" : "deactivated";
            return Ok(new { message = $"User {state} successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting active status for user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred" });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/user/{id}/purge — permanently remove a user (SuperAdmin only)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Permanently and irreversibly deletes a user and all their owned data.
    /// SuperAdmin only. Cannot be used to delete yourself.
    /// </summary>
    [HttpDelete("{id}/purge")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Purge(Guid id)
    {
        try
        {
            var callerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            if (callerId == id)
                return BadRequest(new { message = "You cannot permanently delete your own account." });

            await _userService.HardDeleteAsync(id);
            return Ok(new { message = "User permanently deleted." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error purging user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the user." });
        }
    }


    [HttpPost("{id}/change-password")]
    public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordRequest request)
    {
        try
        {
            var callerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            if (callerId != id && !IsAdminOrSuperAdmin())
                return Forbid();

            var user = await _userService.GetById(id);
            
            // Verify old password
            var authenticatedUser = await _userService.Authenticate(user.Username, request.OldPassword);
            if (authenticatedUser == null)
            {
                return BadRequest(new { message = "Current password is incorrect" });
            }

            await _userService.Update(user, request.NewPassword);

            return Ok(new { message = "Password changed successfully" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while changing password" });
        }
    }

    /// <summary>
    /// Assign role to user (Admin only)
    /// </summary>
    [HttpPost("{id}/assign-role")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleRequest request)
    {
        try
        {
            var user = await _userService.GetByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            // Validate role
            if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            {
                return BadRequest(new { message = "Invalid role specified" });
            }

            user.Role = role;
            await _userService.UpdateAsync(user);

            return Ok(new { message = $"Role {request.Role} assigned successfully", userId = id, role = role.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning role to user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while assigning role" });
        }
    }

    /// <summary>
    /// Get user's current role
    /// </summary>
    [HttpGet("{id}/role")]
    public async Task<IActionResult> GetUserRole(Guid id)
    {
        try
        {
            var user = await _userService.GetByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(new { userId = id, role = user.Role.ToString() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving role for user {UserId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving role" });
        }
    }

    /// <summary>
    /// Get all users by role (Admin only)
    /// </summary>
    [HttpGet("by-role/{role}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetUsersByRole(string role)
    {
        try
        {
            if (!Enum.TryParse<UserRole>(role, true, out var userRole))
            {
                return BadRequest(new { message = "Invalid role specified" });
            }

            var users = await _userService.GetUsersByRoleAsync(userRole);

            return Ok(users.Select(u => new UserResponse
            {
                Id              = u.Id,
                Username        = u.Username,
                Email           = u.Email,
                FirstName       = u.FirstName,
                MiddleName      = u.MiddleName,
                LastName        = u.LastName,
                PhoneNumber     = u.PhoneNumber,
                DateOfBirth     = u.DateOfBirth,
                Role            = u.Role.ToString(),
                IsActive        = u.IsActive,
                IsEmailVerified = u.IsEmailVerified,
                CreatedAt       = u.CreatedAt,
                UpdatedAt       = u.UpdatedAt,
            }).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users by role {Role}", role);
            return StatusCode(500, new { message = "An error occurred while retrieving users" });
        }
    }
}

public class CreateUserRequest
{
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public required string LastName { get; set; }
    public required string PhoneNumber { get; set; }
    public string? DateOfBirth { get; set; }
    public required string Password { get; set; }
    /// <summary>Patient | Doctor | Pharmacist | PharmacyManager | Admin | SuperAdmin</summary>
    public required string Role { get; set; }
}

public class UpdateUserRequest
{
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public required string LastName { get; set; }
    public required string PhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Password { get; set; }
}

public class ChangePasswordRequest
{
    public required string OldPassword { get; set; }
    public required string NewPassword { get; set; }
}

public class AssignRoleRequest
{
    public required string Role { get; set; } // Patient, Doctor, Pharmacist, Admin, etc.
}

public class UserResponse
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public required string LastName { get; set; }
    public required string PhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Role { get; set; }
    public bool IsActive { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SetActiveRequest
{
    public bool IsActive { get; set; }
}
