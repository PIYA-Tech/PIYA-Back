using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DoctorController(IDoctorProfileService doctorProfileService, ILogger<DoctorController> logger) : ControllerBase
{
    private readonly IDoctorProfileService _doctorProfileService = doctorProfileService;
    private readonly ILogger<DoctorController> _logger = logger;

    /// <summary>
    /// Strips sensitive license fields from a <see cref="DoctorProfile"/> before returning it
    /// to unauthenticated / public callers.
    /// </summary>
    private static DoctorProfile StripSensitiveFields(DoctorProfile doctor)
    {
        doctor.LicenseNumber = string.Empty;
        doctor.LicenseAuthority = string.Empty;
        return doctor;
    }

    /// <summary>
    /// Search doctors by specialization
    /// </summary>
    [HttpGet("search/specialization/{specialization}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DoctorProfile>>> SearchBySpecialization(MedicalSpecialization specialization)
    {
        try
        {
            var doctors = await _doctorProfileService.SearchBySpecializationAsync(specialization);
            return Ok(doctors.Select(StripSensitiveFields).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching doctors by specialization {Specialization}", specialization);
            return StatusCode(500, new { error = "Failed to search doctors" });
        }
    }

    /// <summary>
    /// Get available doctors (accepting new patients)
    /// </summary>
    [HttpGet("available")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DoctorProfile>>> GetAvailableDoctors([FromQuery] MedicalSpecialization? specialization = null)
    {
        try
        {
            var doctors = await _doctorProfileService.GetAvailableDoctorsAsync(specialization);
            return Ok(doctors.Select(StripSensitiveFields).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving available doctors");
            return StatusCode(500, new { error = "Failed to retrieve doctors" });
        }
    }

    /// <summary>
    /// Get doctor profile by ID (public view)
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<DoctorProfile>> GetById(Guid id)
    {
        try
        {
            var doctor = await _doctorProfileService.GetByIdAsync(id);
            if (doctor == null)
            {
                return NotFound(new { error = "Doctor profile not found" });
            }

            return Ok(StripSensitiveFields(doctor));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor profile {DoctorId}", id);
            return StatusCode(500, new { error = "Failed to retrieve doctor profile" });
        }
    }

    /// <summary>
    /// Get doctors by hospital
    /// </summary>
    [HttpGet("hospital/{hospitalId}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DoctorProfile>>> GetByHospital(Guid hospitalId)
    {
        try
        {
            var doctors = await _doctorProfileService.GetDoctorsByHospitalAsync(hospitalId);
            return Ok(doctors.Select(StripSensitiveFields).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctors for hospital {HospitalId}", hospitalId);
            return StatusCode(500, new { error = "Failed to retrieve doctors" });
        }
    }

    /// <summary>
    /// Check doctor availability at specific date/time
    /// </summary>
    [HttpGet("{id}/availability")]
    [AllowAnonymous]
    public async Task<ActionResult<object>> CheckAvailability(Guid id, [FromQuery] DateTime dateTime)
    {
        try
        {
            var doctor = await _doctorProfileService.GetByIdAsync(id);
            if (doctor == null)
            {
                return NotFound(new { error = "Doctor profile not found" });
            }

            var isAvailable = await _doctorProfileService.IsAvailableAtAsync(doctor.UserId, dateTime);
            return Ok(new 
            { 
                doctorId = id, 
                dateTime, 
                isAvailable,
                availabilityStatus = doctor.CurrentStatus
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking doctor availability for {DoctorId}", id);
            return StatusCode(500, new { error = "Failed to check availability" });
        }
    }

    /// <summary>
    /// Get doctor's working hours
    /// </summary>
    [HttpGet("{id}/working-hours")]
    [AllowAnonymous]
    public async Task<ActionResult<List<WorkingHoursSlot>>> GetWorkingHours(Guid id)
    {
        try
        {
            var doctor = await _doctorProfileService.GetByIdAsync(id);
            if (doctor == null)
            {
                return NotFound(new { error = "Doctor profile not found" });
            }

            var workingHours = await _doctorProfileService.GetWorkingHoursAsync(doctor.UserId);
            if (workingHours == null)
            {
                return Ok(new List<WorkingHoursSlot>()); // Return empty array if not set
            }

            return Ok(workingHours);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving working hours for doctor {DoctorId}", id);
            return StatusCode(500, new { error = "Failed to retrieve working hours" });
        }
    }
}
