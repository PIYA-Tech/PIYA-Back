using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Extensions;
using PIYA_API.Model;

namespace PIYA_API.Controllers;

/// <summary>
/// Example controller demonstrating pagination, filtering, sorting, and API versioning
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class UsersListController(PharmacyApiDbContext context, ILogger<UsersListController> logger) : ControllerBase
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly ILogger<UsersListController> _logger = logger;

    /// <summary>
    /// Get all users with pagination, filtering, and sorting
    /// </summary>
    /// <param name="queryParams">Query parameters (page, pageSize, search, sortBy, sortDescending)</param>
    /// <returns>Paginated list of users</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<UserDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<UserDto>>>> GetUsers(
        [FromQuery] QueryParams queryParams)
    {
        _logger.LogInformation("Fetching users - Page: {Page}, Size: {Size}, Search: {Search}", 
            queryParams.PageNumber, queryParams.PageSize, queryParams.SearchTerm);

        var query = _context.Users
            .AsQueryable();

        // Apply search across multiple fields
        query = query.ApplyQueryParams(
            queryParams,
            u => u.Username,
            u => u.Email,
            u => u.FirstName,
            u => u.LastName
        );

        // Project to DTO and paginate
        var usersQuery = query.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            PhoneNumber = u.PhoneNumber,
            Role = u.Role.ToString(),
            IsEmailVerified = u.IsEmailVerified,
            IsTwoFactorEnabled = u.TwoFactorAuth != null && u.TwoFactorAuth.IsEnabled
        });

        var pagedResult = await usersQuery.ToPagedResponseAsync(
            queryParams.PageNumber,
            queryParams.PageSize);

        return Ok(ApiResponse<PagedResponse<UserDto>>.SuccessResponse(
            pagedResult,
            $"Retrieved {pagedResult.Items.Count} users"));
    }

    /// <summary>
    /// Get user by ID
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetUserById(Guid id)
    {
        var user = await _context.Users
            .Include(u => u.TwoFactorAuth)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found", id);
            return NotFound(ApiResponse<UserDto>.ErrorResponse("User not found"));
        }

        var userDto = new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            Role = user.Role.ToString(),
            IsEmailVerified = user.IsEmailVerified,
            IsTwoFactorEnabled = user.TwoFactorAuth?.IsEnabled ?? false
        };

        return Ok(ApiResponse<UserDto>.SuccessResponse(userDto));
    }
}

/// <summary>
/// User data transfer object
/// </summary>
public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsEmailVerified { get; set; }
    public bool IsTwoFactorEnabled { get; set; }
}
