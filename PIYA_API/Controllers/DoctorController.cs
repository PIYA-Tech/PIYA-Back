using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
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
    /// Search doctors by specialization
    /// </summary>
    [HttpGet("search/specialization/{specialization}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<PublicDoctorProfileResponseDto>>> SearchBySpecialization(MedicalSpecialization specialization)
    {
        try
        {
            var doctors = await _doctorProfileService.SearchBySpecializationAsync(specialization);
            return Ok(doctors.Select(PublicDoctorProfileResponseDto.FromEntity).ToList());
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
    public async Task<ActionResult<List<PublicDoctorProfileResponseDto>>> GetAvailableDoctors([FromQuery] MedicalSpecialization? specialization = null)
    {
        try
        {
            var doctors = await _doctorProfileService.GetAvailableDoctorsAsync(specialization);
            return Ok(doctors.Select(PublicDoctorProfileResponseDto.FromEntity).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving available doctors");
            return StatusCode(500, new { error = "Failed to retrieve doctors" });
        }
    }

    /// <summary>
    /// Get doctor profile by DoctorProfile ID (public view). For appointment
    /// DoctorId values use GET /api/doctor/by-user/{doctorUserId}.
    /// </summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicDoctorProfileResponseDto>> GetById(Guid id)
    {
        try
        {
            var doctor = await _doctorProfileService.GetByIdAsync(id);
            if (doctor == null)
            {
                return NotFound(new { error = "Doctor profile not found" });
            }

            return Ok(PublicDoctorProfileResponseDto.FromEntity(doctor));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor profile {DoctorId}", id);
            return StatusCode(500, new { error = "Failed to retrieve doctor profile" });
        }
    }

    /// <summary>
    /// Get a public doctor profile by the doctor User id used in appointment,
    /// referral and prescription DoctorId fields.
    /// </summary>
    [HttpGet("by-user/{doctorUserId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicDoctorProfileResponseDto>> GetByUserId(Guid doctorUserId)
    {
        try
        {
            var doctor = await _doctorProfileService.GetByUserIdAsync(doctorUserId);
            if (doctor == null)
                return NotFound(new { error = "Doctor profile not found" });

            return Ok(PublicDoctorProfileResponseDto.FromEntity(doctor));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor profile for user {DoctorUserId}", doctorUserId);
            return StatusCode(500, new { error = "Failed to retrieve doctor profile" });
        }
    }

    /// <summary>
    /// Get doctors by hospital
    /// </summary>
    [HttpGet("hospital/{hospitalId}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<PublicDoctorProfileResponseDto>>> GetByHospital(Guid hospitalId)
    {
        try
        {
            var doctors = await _doctorProfileService.GetDoctorsByHospitalAsync(hospitalId);
            return Ok(doctors.Select(PublicDoctorProfileResponseDto.FromEntity).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctors for hospital {HospitalId}", hospitalId);
            return StatusCode(500, new { error = "Failed to retrieve doctors" });
        }
    }

    /// <summary>
    /// Check doctor availability at a specific date/time by DoctorProfile ID.
    /// </summary>
    [HttpGet("{id:guid}/availability")]
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
                doctorId = doctor.UserId,
                doctorUserId = doctor.UserId,
                profileId = doctor.Id,
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
    /// Get a doctor's working hours by DoctorProfile ID.
    /// </summary>
    [HttpGet("{id:guid}/working-hours")]
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
