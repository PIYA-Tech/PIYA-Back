using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;
namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HospitalController(
    IHospitalService hospitalService,
    IDoctorProfileService doctorProfileService,
    IAppointmentService appointmentService,
    ILogger<HospitalController> logger) : ControllerBase
{
    private readonly IHospitalService _hospitalService = hospitalService;
    private readonly IDoctorProfileService _doctorProfileService = doctorProfileService;
    private readonly IAppointmentService _appointmentService = appointmentService;
    private readonly ILogger<HospitalController> _logger = logger;

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// Get all hospitals
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<HospitalPublicDto>>> GetAll()
    {
        try
        {
            var hospitals = await _hospitalService.GetAllAsync();
            return Ok(hospitals.Select(ToPublicDto).ToList());
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
    public async Task<ActionResult<HospitalPublicDto>> GetById(Guid id)
    {
        try
        {
            var hospital = await _hospitalService.GetByIdAsync(id);
            if (hospital == null)
            {
                return NotFound(new { error = "Hospital not found" });
            }

            return Ok(ToPublicDto(hospital));
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
    public async Task<ActionResult<List<HospitalPublicDto>>> GetByCity(string city)
    {
        try
        {
            var hospitals = await _hospitalService.GetByCityAsync(city);
            return Ok(hospitals.Select(ToPublicDto).ToList());
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
    public async Task<ActionResult<List<HospitalPublicDto>>> GetByDepartment(string department)
    {
        try
        {
            var hospitals = await _hospitalService.GetByDepartmentAsync(department);
            return Ok(hospitals.Select(ToPublicDto).ToList());
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
    public async Task<ActionResult<List<HospitalPublicDto>>> GetActive()
    {
        try
        {
            var hospitals = await _hospitalService.GetActiveHospitalsAsync();
            return Ok(hospitals.Select(ToPublicDto).ToList());
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
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<Hospital>> Create([FromBody] HospitalUpsertDto dto)
    {
        try
        {
            var hospital = DtoToHospital(dto);
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
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<Hospital>> Update(Guid id, [FromBody] HospitalUpsertDto dto)
    {
        try
        {
            var hospital = DtoToHospital(dto);
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static HospitalPublicDto ToPublicDto(Hospital h) => new()
    {
        Id           = h.Id,
        Name         = h.Name,
        Address      = h.Address,
        City         = h.City,
        Country      = h.Country,
        PhoneNumber  = h.PhoneNumber,
        Email        = h.Email,
        Website      = h.Website,
        Departments  = h.Departments,
        IsActive     = h.IsActive,
        OperatingHours = h.OperatingHours,
        DirectorId   = h.DirectorId,
        Coordinates  = h.Coordinates is { } c
            ? new CoordinatesDto { Lat = c.Latitude, Lng = c.Longitude }
            : null,
    };

    private static Hospital DtoToHospital(HospitalUpsertDto dto) => new()
    {
        Name             = dto.Name,
        Address          = dto.Address,
        City             = dto.City,
        Country          = dto.Country,
        PhoneNumber      = dto.PhoneNumber,
        Email            = dto.Email,
        Website          = dto.Website,
        Departments      = dto.Departments ?? [],
        EmergencyContact = dto.EmergencyContact,
        OperatingHours   = dto.OperatingHours,
        Coordinates      = dto.Coordinates is { } c
            ? new Coordinates { Latitude = c.Lat, Longitude = c.Lng }
            : null,
    };

    /// <summary>
    /// Delete hospital (Admin only)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
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
    [Authorize(Roles = "Admin,SuperAdmin")]
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
    [Authorize(Roles = "Admin,SuperAdmin")]
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

    // ── Doctor assignment (Admin only) ────────────────────────────────────────

    /// <summary>
    /// Get all doctors assigned to a hospital
    /// </summary>
    [HttpGet("{id}/doctors")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DoctorProfile>>> GetDoctors(Guid id)
    {
        try
        {
            var doctors = await _doctorProfileService.GetDoctorsByHospitalAsync(id);
            return Ok(doctors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctors for hospital {HospitalId}", id);
            return StatusCode(500, new { error = "Failed to retrieve doctors" });
        }
    }

    /// <summary>
    /// Assign (replace) the full hospital list for a doctor profile (Admin only).
    /// PUT /api/hospital/doctors/{doctorProfileId}/hospitals
    /// Body: { "hospitalIds": ["guid", ...] }
    /// </summary>
    [HttpPut("doctors/{doctorProfileId}/hospitals")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<DoctorProfile>> AssignHospitals(
        Guid doctorProfileId,
        [FromBody] AssignHospitalsRequest request)
    {
        try
        {
            var updated = await _doctorProfileService.AssignHospitalsAsync(doctorProfileId, request.HospitalIds);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning hospitals to doctor {DoctorProfileId}", doctorProfileId);
            return StatusCode(500, new { error = "Failed to assign hospitals" });
        }
    }

    /// <summary>
    /// Remove a single doctor from a specific hospital (Admin only).
    /// DELETE /api/hospital/{hospitalId}/doctors/{doctorProfileId}
    /// </summary>
    [HttpDelete("{hospitalId}/doctors/{doctorProfileId}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult> RemoveDoctorFromHospital(Guid hospitalId, Guid doctorProfileId)
    {
        try
        {
            var profile = await _doctorProfileService.GetByIdAsync(doctorProfileId);
            if (profile == null)
                return NotFound(new { error = "Doctor profile not found" });

            var updated = await _doctorProfileService.AssignHospitalsAsync(
                doctorProfileId,
                profile.HospitalIds.Where(h => h != hospitalId).ToList());

            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing doctor {DoctorProfileId} from hospital {HospitalId}", doctorProfileId, hospitalId);
            return StatusCode(500, new { error = "Failed to remove doctor from hospital" });
        }
    }

    // ── Hospital Director endpoints ───────────────────────────────────────────

    /// <summary>
    /// Get the hospital the calling HospitalDirector is assigned to.
    /// </summary>
    [HttpGet("my-hospital")]
    [Authorize(Roles = "HospitalDirector")]
    public async Task<ActionResult<HospitalPublicDto>> GetMyHospital()
    {
        try
        {
            var hospital = await _hospitalService.GetByDirectorAsync(GetUserId());
            if (hospital == null)
                return NotFound(new { error = "No hospital is assigned to your account yet. Contact an administrator." });
            return Ok(ToPublicDto(hospital));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving hospital for director {DirectorId}", GetUserId());
            return StatusCode(500, new { error = "Failed to retrieve hospital" });
        }
    }

    /// <summary>
    /// Update operational info (hours, contact, departments) for the director's hospital.
    /// Coordinates and activation status cannot be changed by the director.
    /// </summary>
    [HttpPut("my-hospital")]
    [Authorize(Roles = "HospitalDirector")]
    public async Task<ActionResult<HospitalPublicDto>> UpdateMyHospital([FromBody] HospitalUpsertDto dto)
    {
        try
        {
            var hospital = await _hospitalService.GetByDirectorAsync(GetUserId());
            if (hospital == null)
                return NotFound(new { error = "No hospital assigned to your account." });

            var patch = DtoToHospital(dto);
            patch.Id          = hospital.Id;
            patch.Coordinates = hospital.Coordinates; // directors cannot relocate the hospital
            patch.IsActive    = hospital.IsActive;     // directors cannot activate/deactivate

            var result = await _hospitalService.UpdateAsync(patch);
            return Ok(ToPublicDto(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Hospital not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating hospital for director");
            return StatusCode(500, new { error = "Failed to update hospital" });
        }
    }

    /// <summary>
    /// Get all doctors in the director's hospital.
    /// </summary>
    [HttpGet("my-hospital/doctors")]
    [Authorize(Roles = "HospitalDirector")]
    public async Task<ActionResult<List<DoctorProfile>>> GetMyHospitalDoctors()
    {
        try
        {
            var hospital = await _hospitalService.GetByDirectorAsync(GetUserId());
            if (hospital == null)
                return NotFound(new { error = "No hospital assigned to your account." });

            var doctors = await _doctorProfileService.GetDoctorsByHospitalAsync(hospital.Id);
            return Ok(doctors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctors for director's hospital");
            return StatusCode(500, new { error = "Failed to retrieve doctors" });
        }
    }

    /// <summary>
    /// Get all appointments in the director's hospital (optionally filtered by date).
    /// </summary>
    [HttpGet("my-hospital/appointments")]
    [Authorize(Roles = "HospitalDirector")]
    public async Task<ActionResult<List<Appointment>>> GetMyHospitalAppointments([FromQuery] DateTime? date = null)
    {
        try
        {
            var hospital = await _hospitalService.GetByDirectorAsync(GetUserId());
            if (hospital == null)
                return NotFound(new { error = "No hospital assigned to your account." });

            var appointments = await _appointmentService.GetHospitalAppointmentsAsync(hospital.Id, date);
            return Ok(appointments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving appointments for director's hospital");
            return StatusCode(500, new { error = "Failed to retrieve appointments" });
        }
    }

    /// <summary>
    /// Assign a HospitalDirector user to a hospital (Admin/SuperAdmin only).
    /// PUT /api/hospital/{id}/director  Body: { "directorId": "guid" }
    /// </summary>
    [HttpPut("{id}/director")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<HospitalPublicDto>> AssignDirector(Guid id, [FromBody] AssignDirectorRequest request)
    {
        try
        {
            var updated = await _hospitalService.AssignDirectorAsync(id, request.DirectorId);
            return Ok(ToPublicDto(updated));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning director to hospital {HospitalId}", id);
            return StatusCode(500, new { error = "Failed to assign director" });
        }
    }

}

// ── Helper DTOs ───────────────────────────────────────────────────────────────

/// <summary>
/// Body for PUT /api/hospital/{id}/director — pass null DirectorId to unassign.
/// </summary>
public class AssignDirectorRequest
{
    public Guid? DirectorId { get; set; }
}
