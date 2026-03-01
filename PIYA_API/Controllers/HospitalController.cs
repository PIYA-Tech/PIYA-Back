using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HospitalController(IHospitalService hospitalService, ILogger<HospitalController> logger) : ControllerBase
{
    private readonly IHospitalService _hospitalService = hospitalService;
    private readonly ILogger<HospitalController> _logger = logger;

    /// <summary>
    /// Get all hospitals
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<Hospital>>> GetAll()
    {
        try
        {
            var hospitals = await _hospitalService.GetAllAsync();
            return Ok(hospitals);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving hospitals");
            return StatusCode(500, new { error = "Failed to retrieve hospitals" });
        }
    }

    /// <summary>
    /// Get hospital by ID
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<Hospital>> GetById(Guid id)
    {
        try
        {
            var hospital = await _hospitalService.GetByIdAsync(id);
            if (hospital == null)
            {
                return NotFound(new { error = "Hospital not found" });
            }

            return Ok(hospital);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving hospital {HospitalId}", id);
            return StatusCode(500, new { error = "Failed to retrieve hospital" });
        }
    }

    /// <summary>
    /// Get hospitals by city
    /// </summary>
    [HttpGet("city/{city}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<Hospital>>> GetByCity(string city)
    {
        try
        {
            var hospitals = await _hospitalService.GetByCityAsync(city);
            return Ok(hospitals);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving hospitals in {City}", city);
            return StatusCode(500, new { error = "Failed to retrieve hospitals" });
        }
    }

    /// <summary>
    /// Get hospitals by department
    /// </summary>
    [HttpGet("department/{department}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<Hospital>>> GetByDepartment(string department)
    {
        try
        {
            var hospitals = await _hospitalService.GetByDepartmentAsync(department);
            return Ok(hospitals);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving hospitals with department {Department}", department);
            return StatusCode(500, new { error = "Failed to retrieve hospitals" });
        }
    }

    /// <summary>
    /// Get active hospitals only
    /// </summary>
    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<ActionResult<List<Hospital>>> GetActive()
    {
        try
        {
            var hospitals = await _hospitalService.GetActiveHospitalsAsync();
            return Ok(hospitals);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active hospitals");
            return StatusCode(500, new { error = "Failed to retrieve hospitals" });
        }
    }

    /// <summary>
    /// Create new hospital (Admin only)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<Hospital>> Create([FromBody] Hospital hospital)
    {
        try
        {
            var created = await _hospitalService.CreateAsync(hospital);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating hospital");
            return StatusCode(500, new { error = "Failed to create hospital" });
        }
    }

    /// <summary>
    /// Update hospital (Admin only)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<Hospital>> Update(Guid id, [FromBody] Hospital hospital)
    {
        try
        {
            hospital.Id = id;
            var updated = await _hospitalService.UpdateAsync(hospital);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Hospital not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating hospital {HospitalId}", id);
            return StatusCode(500, new { error = "Failed to update hospital" });
        }
    }

    /// <summary>
    /// Delete hospital (Admin only)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Delete(Guid id)
    {
        try
        {
            await _hospitalService.DeleteAsync(id);
            return Ok(new { message = "Hospital deleted successfully" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Hospital not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting hospital {HospitalId}", id);
            return StatusCode(500, new { error = "Failed to delete hospital" });
        }
    }

    /// <summary>
    /// Deactivate hospital (Admin only)
    /// </summary>
    [HttpPost("{id}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Deactivate(Guid id)
    {
        try
        {
            await _hospitalService.DeactivateAsync(id);
            return Ok(new { message = "Hospital deactivated successfully" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Hospital not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating hospital {HospitalId}", id);
            return StatusCode(500, new { error = "Failed to deactivate hospital" });
        }
    }

    /// <summary>
    /// Activate hospital (Admin only)
    /// </summary>
    [HttpPost("{id}/activate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> Activate(Guid id)
    {
        try
        {
            await _hospitalService.ActivateAsync(id);
            return Ok(new { message = "Hospital activated successfully" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Hospital not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating hospital {HospitalId}", id);
            return StatusCode(500, new { error = "Failed to activate hospital" });
        }
    }
}
