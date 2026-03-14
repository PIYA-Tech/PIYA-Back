using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AppointmentController(IAppointmentService appointmentService, ILogger<AppointmentController> logger) : ControllerBase
{
    private readonly IAppointmentService _appointmentService = appointmentService;
    private readonly ILogger<AppointmentController> _logger = logger;

    /// <summary>
    /// Book a new appointment
    /// </summary>
    [HttpPost("book")]
    [Authorize(Roles = "Patient,Doctor,Admin,SuperAdmin")]
    public async Task<ActionResult<Appointment>> BookAppointment([FromBody] AppointmentRequest request)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // Validate required HospitalId
            if (request.HospitalId == Guid.Empty)
                return BadRequest(new { error = "HospitalId is required to book an appointment." });

            // Patients can only book for themselves; Admins may override the PatientId;
            // Doctors must explicitly supply a PatientId (they cannot book themselves as patient)
            Guid patientId;
            if (userRole == "Patient")
                patientId = userId;
            else if (userRole == "Admin" && request.PatientId.HasValue)
                patientId = request.PatientId.Value;
            else if (userRole == "Doctor")
            {
                if (!request.PatientId.HasValue)
                    return BadRequest(new { error = "PatientId is required when a Doctor books an appointment." });
                patientId = request.PatientId.Value;
            }
            else
                patientId = request.PatientId ?? userId;

            var appointment = new Appointment
            {
                PatientId = patientId,
                DoctorId = request.DoctorId,
                HospitalId = request.HospitalId,
                ScheduledAt = request.ScheduledAt,
                Reason = request.Reason,
                Status = AppointmentStatus.Scheduled
            };

            var created = await _appointmentService.BookAppointmentAsync(appointment);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            // Conflict with existing appointment
            return Conflict(new { error = ex.Message });
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
    public async Task<ActionResult<Appointment>> GetById(Guid id, CancellationToken ct)
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
            if (userRole != "Admin" && appointment.PatientId != userId && appointment.DoctorId != userId)
            {
                return Forbid();
            }

            return Ok(appointment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving appointment {AppointmentId}", id);
            return StatusCode(500, new { error = "Failed to retrieve appointment" });
        }
    }

    /// <summary>
    /// Get my appointments (patient or doctor)
    /// </summary>
    [HttpGet("my-appointments")]
    public async Task<ActionResult<List<Appointment>>> GetMyAppointments([FromQuery] string? status = null, CancellationToken ct = default)
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

            List<Appointment> appointments;
            if (userRole == "Patient")
            {
                appointments = await _appointmentService.GetPatientAppointmentsAsync(userId, appointmentStatus, ct);
            }
            else if (userRole == "Doctor")
            {
                appointments = await _appointmentService.GetDoctorAppointmentsAsync(userId, status: appointmentStatus, ct: ct);
            }
            else
            {
                return BadRequest(new { error = "Only patients and doctors can view their appointments" });
            }

            return Ok(appointments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user appointments");
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
            var slots = appointments.Select(a => new
            {
                a.Id,
                a.ScheduledAt,
                a.DurationMinutes,
                a.Status
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
            var isAvailable = await _appointmentService.IsDoctorAvailableAsync(doctorId, scheduledAt, durationMinutes);
            return Ok(new { doctorId, scheduledAt, durationMinutes, isAvailable });
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
    public async Task<ActionResult<Appointment>> Cancel(Guid id, [FromBody] CancelAppointmentRequest request)
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
            if (userRole != "Admin" && appointment.PatientId != userId && appointment.DoctorId != userId)
            {
                return Forbid();
            }

            var cancelled = await _appointmentService.CancelAppointmentAsync(id, userId, request.Reason);
            return Ok(cancelled);
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
    public async Task<ActionResult<Appointment>> Reschedule(Guid id, [FromBody] RescheduleAppointmentRequest request)
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
            if (userRole != "Admin" && appointment.PatientId != userId && appointment.DoctorId != userId)
            {
                return Forbid();
            }

            var rescheduled = await _appointmentService.RescheduleAppointmentAsync(id, request.NewScheduledAt);
            return Ok(rescheduled);
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
    public async Task<ActionResult<Appointment>> Complete(Guid id, [FromBody] CompleteAppointmentRequest request)
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
            if (userRole != "Admin" && appointment.DoctorId != userId)
            {
                return Forbid();
            }

            var completed = await _appointmentService.CompleteAppointmentAsync(id, request.Notes);
            return Ok(completed);
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
    public async Task<ActionResult<List<Appointment>>> GetHospitalAppointments(Guid hospitalId, [FromQuery] DateTime? date = null)
    {
        try
        {
            var appointments = await _appointmentService.GetHospitalAppointmentsAsync(hospitalId, date);
            return Ok(appointments);
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
    string Reason
);

public record RescheduleAppointmentRequest(DateTime NewScheduledAt);
