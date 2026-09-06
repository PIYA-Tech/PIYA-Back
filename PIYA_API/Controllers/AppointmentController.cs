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
public class AppointmentController(IAppointmentService appointmentService, IUserService userService, ILogger<AppointmentController> logger) : ControllerBase
{
    private readonly IAppointmentService _appointmentService = appointmentService;
    private readonly IUserService _userService = userService;
    private readonly ILogger<AppointmentController> _logger = logger;

    /// <summary>
    /// Book a new appointment
    /// </summary>
    [HttpPost("book")]
    [Authorize(Roles = "Patient,Doctor,Admin,SuperAdmin")]
    public async Task<ActionResult<AppointmentResponseDto>> BookAppointment([FromBody] AppointmentRequest request)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // Validate required HospitalId
            if (request.HospitalId == Guid.Empty)
                return BadRequest(new { error = "HospitalId is required to book an appointment." });

            // Patients can only book for themselves; Admins/SuperAdmins may override the PatientId;
            // Doctors must explicitly supply a PatientId (they cannot book themselves as patient)

            if (userId == Guid.Empty)
                return BadRequest(new { error = "Authenticated user ID is invalid." });
            
            // Doctors cannot book appointments for themselves as patients
            if (userId == request.DoctorId)
                return BadRequest(new { error = "Doctors cannot book appointments for themselves as patients. Please specify a valid PatientId." });

            // Role-based validation
            if (userRole == "Patient")
            {
                // If PatientId is provided, it must match the authenticated user's ID
                if (request.PatientId != null && request.PatientId != userId)
                {
                    return BadRequest(new { error = "Patients can only book appointments for themselves." });
                }
            }

            Guid patientId;
            if (userRole == "Patient")
            {
                patientId = userId;
            }
            else if (request.PatientId is { } requestedPatientId && requestedPatientId != Guid.Empty)
            {
                patientId = requestedPatientId;
            }
            else if (userRole == "Doctor")
            {
                return BadRequest(new { error = "Doctors must specify the patient they are booking for." });
            }
            else
            {
                patientId = userId;
            }

            var appointment = new Appointment
            {
                PatientId = patientId,
                DoctorId = request.DoctorId,
                HospitalId = request.HospitalId,
                ScheduledAt = request.ScheduledAt,
                DurationMinutes = request.DurationMinutes > 0 ? request.DurationMinutes : 30,
                Reason = request.Reason,
                Status = AppointmentStatus.Scheduled
            };

            var created = await _appointmentService.BookAppointmentAsync(appointment);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, AppointmentResponseDto.FromEntity(created));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Conflict with existing appointment
            return Conflict(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error booking appointment");
            return StatusCode(500, new { error = "Failed to book appointment" });
        }
    }

    /// <summary>
    /// Get appointment by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AppointmentResponseDto>> GetById(Guid id, CancellationToken ct)
    {
        try
        {
            var appointment = await _appointmentService.GetByIdAsync(id, ct);
            if (appointment == null)
            {
                return NotFound(new { error = "Appointment not found" });
            }

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // Verify user has access to this appointment
            if (userRole != "Admin" && userRole != "SuperAdmin" && appointment.PatientId != userId && appointment.DoctorId != userId)
            {
                return Forbid();
            }

            return Ok(AppointmentResponseDto.FromEntity(appointment));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving appointment {AppointmentId}", id);
            return StatusCode(500, new { error = "Failed to retrieve appointment" });
        }
    }

    /// <summary>
    /// Get my appointments — works for every role.
    /// Every authenticated user is also a patient who can book appointments,
    /// so ALL roles get their patient-side appointments.
    /// Additionally, Doctors get their doctor-side appointments merged in
    /// (deduplicated by ID), since a Doctor can simultaneously be a patient
    /// who has booked their own appointments.
    /// </summary>
    [HttpGet("my-appointments")]
    [Authorize]
    public async Task<ActionResult<List<AppointmentResponseDto>>> GetMyAppointments([FromQuery] string? status = null, CancellationToken ct = default)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            AppointmentStatus? appointmentStatus = null;
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentStatus>(status, true, out var parsedStatus))
            {
                appointmentStatus = parsedStatus;
            }

            // Every role has a patient-side view
            var patientAppointments = await _appointmentService.GetPatientAppointmentsAsync(userId, appointmentStatus, ct);

            // Doctors additionally see appointments where they are the treating doctor
            if (userRole == "Doctor")
            {
                var doctorAppointments = await _appointmentService.GetDoctorAppointmentsAsync(userId, null, appointmentStatus, ct);

                // Merge and deduplicate by Id — a single appointment could theoretically
                // appear on both sides if somehow the same user is both patient and doctor
                var merged = patientAppointments
                    .UnionBy(doctorAppointments, a => a.Id)
                    .OrderBy(a => a.ScheduledAt)
                    .ToList();

                return Ok(merged.Select(AppointmentResponseDto.FromEntity).ToList());
            }

            return Ok(patientAppointments.Select(AppointmentResponseDto.FromEntity).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user appointments");
            return StatusCode(500, new { error = "Failed to retrieve appointments" });
        }
    }

    /// <summary>
    /// Get the calling doctor's patient appointments (appointments where they are
    /// the treating doctor). Kept for backwards compatibility — new clients should
    /// use GET /my-appointments which merges both sides for every role.
    /// </summary>
    [HttpGet("doctors-patient-appointments")]
    [Authorize(Roles = "Doctor,Admin,SuperAdmin")]
    public async Task<ActionResult> GetDoctorPatientAppointments([FromQuery] string? status = null)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

            AppointmentStatus? appointmentStatus = null;
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentStatus>(status, true, out var parsedStatus))
            {
                appointmentStatus = parsedStatus;
            }

            var appointments = await _appointmentService.GetDoctorAppointmentsAsync(userId, null, appointmentStatus);

            return Ok(appointments.Select(AppointmentResponseDto.FromEntity).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor appointments");
            return StatusCode(500, new { error = "Failed to retrieve appointments" });
        }
    }

    /// <summary>
    /// Get ALL appointments across the system — Admin/SuperAdmin only.
    /// All query params are optional filters.
    /// </summary>
    [HttpGet("all")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<List<AppointmentResponseDto>>> GetAllAppointments(
        [FromQuery] Guid? hospitalId = null,
        [FromQuery] Guid? doctorId = null,
        [FromQuery] Guid? patientId = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            AppointmentStatus? appointmentStatus = null;
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentStatus>(status, true, out var parsed))
                appointmentStatus = parsed;

            var appointments = await _appointmentService.GetAllAppointmentsAsync(
                hospitalId, doctorId, patientId, appointmentStatus, from, to, ct);
            return Ok(appointments.Select(AppointmentResponseDto.FromEntity).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all appointments (admin)");
            return StatusCode(500, new { error = "Failed to retrieve appointments" });
        }
    }

    /// <summary>
    /// Get doctor's schedule for a specific date (authenticated users only)
    /// </summary>
    [HttpGet("doctor/{doctorId}/schedule")]
    [Authorize]
    public async Task<ActionResult<List<object>>> GetDoctorSchedule(Guid doctorId, [FromQuery] DateTime? date = null)
    {
        try
        {
            var appointments = await _appointmentService.GetDoctorAppointmentsAsync(doctorId, date ?? DateTime.UtcNow);
            // Return only availability-relevant fields — never expose patient details
            // Match IsDoctorAvailableAsync: cancelled/completed/no-show rows are
            // history, not reserved time. Never make a cancelled slot unselectable
            // in a client while the availability endpoint says it can be booked.
            var slots = appointments
                .Where(a => a.Status == AppointmentStatus.Scheduled
                         || a.Status == AppointmentStatus.Confirmed
                         || a.Status == AppointmentStatus.Rescheduled)
                .Select(a => new
            {
                a.ScheduledAt,
                a.DurationMinutes
            }).ToList();
            return Ok(slots);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor schedule for {DoctorId}", doctorId);
            return StatusCode(500, new { error = "Failed to retrieve schedule" });
        }
    }

    /// <summary>
    /// Check if doctor is available at a specific time
    /// </summary>
    [HttpGet("doctor/{doctorId}/availability")]
    [Authorize]
    public async Task<ActionResult<object>> CheckAvailability(Guid doctorId, [FromQuery] DateTime scheduledAt, [FromQuery] int durationMinutes = 30)
    {
        try
        {
            if (scheduledAt < DateTime.UtcNow)
                return BadRequest(new { error = "Cannot check availability for a time in the past." });

            var available = await _appointmentService.IsDoctorAvailableAsync(doctorId, scheduledAt, durationMinutes);

            return Ok(new
            {
                doctorId,
                scheduledAt,
                durationMinutes,
                available,
                message = available
                    ? "Doctor is available at the requested time."
                    : "Doctor already has a conflicting appointment at the requested time."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking doctor availability");
            return StatusCode(500, new { error = "Failed to check availability" });
        }
    }

    /// <summary>
    /// Cancel appointment
    /// </summary>
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<AppointmentResponseDto>> Cancel(Guid id, [FromBody] CancelAppointmentRequest? request = null)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var appointment = await _appointmentService.GetByIdAsync(id);
            
            if (appointment == null)
            {
                return NotFound(new { error = "Appointment not found" });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "Admin" && userRole != "SuperAdmin" && appointment.PatientId != userId && appointment.DoctorId != userId)
            {
                return Forbid();
            }

            var cancelled = await _appointmentService.CancelAppointmentAsync(id, userId, request?.Reason);
            return Ok(AppointmentResponseDto.FromEntity(cancelled));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling appointment {AppointmentId}", id);
            return StatusCode(500, new { error = "Failed to cancel appointment" });
        }
    }

    /// <summary>
    /// Reschedule appointment
    /// </summary>
    [HttpPost("{id}/reschedule")]
    public async Task<ActionResult<AppointmentResponseDto>> Reschedule(Guid id, [FromBody] RescheduleAppointmentRequest request)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var appointment = await _appointmentService.GetByIdAsync(id);
            
            if (appointment == null)
            {
                return NotFound(new { error = "Appointment not found" });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "Admin" && userRole != "SuperAdmin" && appointment.PatientId != userId && appointment.DoctorId != userId)
            {
                return Forbid();
            }

            var rescheduled = await _appointmentService.RescheduleAppointmentAsync(id, request.NewScheduledAt);
            return Ok(AppointmentResponseDto.FromEntity(rescheduled));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rescheduling appointment {AppointmentId}", id);
            return StatusCode(500, new { error = "Failed to reschedule appointment" });
        }
    }

    /// <summary>
    /// Complete appointment (Doctor only)
    /// </summary>
    [HttpPost("{id}/complete")]
    [Authorize(Roles = "Doctor,Admin,SuperAdmin")]
    public async Task<ActionResult<AppointmentResponseDto>> Complete(Guid id, [FromBody] CompleteAppointmentRequest request)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var appointment = await _appointmentService.GetByIdAsync(id);
            
            if (appointment == null)
            {
                return NotFound(new { error = "Appointment not found" });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "Admin" && userRole != "SuperAdmin" && appointment.DoctorId != userId)
            {
                return Forbid();
            }

            var completed = await _appointmentService.CompleteAppointmentAsync(id, request.Notes);
            return Ok(AppointmentResponseDto.FromEntity(completed));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing appointment {AppointmentId}", id);
            return StatusCode(500, new { error = "Failed to complete appointment" });
        }
    }

    /// <summary>
    /// Get hospital appointments (Admin only)
    /// </summary>
    [HttpGet("hospital/{hospitalId}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<List<AppointmentResponseDto>>> GetHospitalAppointments(Guid hospitalId, [FromQuery] DateTime? date = null)
    {
        try
        {
            var appointments = await _appointmentService.GetHospitalAppointmentsAsync(hospitalId, date);
            return Ok(appointments.Select(AppointmentResponseDto.FromEntity).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving hospital appointments for {HospitalId}", hospitalId);
            return StatusCode(500, new { error = "Failed to retrieve appointments" });
        }
    }
}

// DTOs for this controller
public record AppointmentRequest(
    Guid? PatientId,
    Guid DoctorId,
    Guid HospitalId,
    DateTime ScheduledAt,
    string Reason,
    int DurationMinutes = 30
);

public record RescheduleAppointmentRequest(DateTime NewScheduledAt);
