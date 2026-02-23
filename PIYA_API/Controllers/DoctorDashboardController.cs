using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/doctor")]
[Authorize(Roles = "Doctor")]
public class DoctorDashboardController(
    IDoctorProfileService doctorProfileService,
    IAppointmentService appointmentService,
    IPrescriptionService prescriptionService,
    IPermissionService permissionService,
    ILogger<DoctorDashboardController> logger) : ControllerBase
{
    private readonly IDoctorProfileService _doctorProfileService = doctorProfileService;
    private readonly IAppointmentService _appointmentService = appointmentService;
    private readonly IPrescriptionService _prescriptionService = prescriptionService;
    private readonly IPermissionService _permissionService = permissionService;
    private readonly ILogger<DoctorDashboardController> _logger = logger;

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    #region Profile Management

    /// <summary>
    /// Get current doctor's profile
    /// </summary>
    [HttpGet("profile")]
    public async Task<ActionResult<DoctorProfile>> GetMyProfile()
    {
        try
        {
            var userId = GetUserId();
            var profile = await _doctorProfileService.GetByUserIdAsync(userId);
            
            if (profile == null)
            {
                return NotFound(new { error = "Doctor profile not found. Please create your profile first." });
            }

            return Ok(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor profile");
            return StatusCode(500, new { error = "Failed to retrieve profile" });
        }
    }

    /// <summary>
    /// Create doctor profile
    /// </summary>
    [HttpPost("profile")]
    public async Task<ActionResult<DoctorProfile>> CreateProfile([FromBody] CreateDoctorProfileRequest request)
    {
        try
        {
            var userId = GetUserId();
            
            // Check if profile already exists
            var existing = await _doctorProfileService.GetByUserIdAsync(userId);
            if (existing != null)
            {
                return BadRequest(new { error = "Doctor profile already exists" });
            }

            var profile = new DoctorProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LicenseNumber = request.LicenseNumber,
                LicenseAuthority = request.LicenseAuthority,
                LicenseExpiryDate = request.LicenseExpiryDate,
                Specialization = request.Specialization,
                AdditionalSpecializations = request.AdditionalSpecializations ?? [],
                YearsOfExperience = request.YearsOfExperience,
                Certifications = request.Certifications ?? [],
                Education = request.Education ?? [],
                Languages = request.Languages ?? [],
                Biography = request.Biography,
                ConsultationFee = request.ConsultationFee,
                AcceptingNewPatients = request.AcceptingNewPatients,
                HospitalIds = request.HospitalIds ?? [],
                CurrentStatus = DoctorAvailabilityStatus.Offline,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var created = await _doctorProfileService.CreateProfileAsync(profile);
            return CreatedAtAction(nameof(GetMyProfile), created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating doctor profile");
            return StatusCode(500, new { error = "Failed to create profile" });
        }
    }

    /// <summary>
    /// Update doctor profile
    /// </summary>
    [HttpPut("profile")]
    public async Task<ActionResult<DoctorProfile>> UpdateProfile([FromBody] UpdateDoctorProfileRequest request)
    {
        try
        {
            var userId = GetUserId();
            var profile = await _doctorProfileService.GetByUserIdAsync(userId);
            
            if (profile == null)
            {
                return NotFound(new { error = "Doctor profile not found" });
            }

            // Update fields
            if (request.LicenseAuthority != null) profile.LicenseAuthority = request.LicenseAuthority;
            if (request.LicenseExpiryDate.HasValue) profile.LicenseExpiryDate = request.LicenseExpiryDate;
            if (request.AdditionalSpecializations != null) profile.AdditionalSpecializations = request.AdditionalSpecializations;
            if (request.YearsOfExperience.HasValue) profile.YearsOfExperience = request.YearsOfExperience.Value;
            if (request.Certifications != null) profile.Certifications = request.Certifications;
            if (request.Education != null) profile.Education = request.Education;
            if (request.Languages != null) profile.Languages = request.Languages;
            if (request.Biography != null) profile.Biography = request.Biography;
            if (request.ConsultationFee.HasValue) profile.ConsultationFee = request.ConsultationFee;
            if (request.AcceptingNewPatients.HasValue) profile.AcceptingNewPatients = request.AcceptingNewPatients.Value;
            if (request.HospitalIds != null) profile.HospitalIds = request.HospitalIds;
            
            profile.UpdatedAt = DateTime.UtcNow;

            var updated = await _doctorProfileService.UpdateProfileAsync(profile);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating doctor profile");
            return StatusCode(500, new { error = "Failed to update profile" });
        }
    }

    #endregion

    #region Availability Management

    /// <summary>
    /// Set doctor status to online
    /// </summary>
    [HttpPost("availability/online")]
    public async Task<ActionResult> SetOnline()
    {
        try
        {
            var userId = GetUserId();
            var success = await _doctorProfileService.SetOnlineAsync(userId);
            
            if (!success)
            {
                return NotFound(new { error = "Doctor profile not found" });
            }

            return Ok(new { status = "online", message = "Status updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting doctor online");
            return StatusCode(500, new { error = "Failed to update status" });
        }
    }

    /// <summary>
    /// Set doctor status to offline
    /// </summary>
    [HttpPost("availability/offline")]
    public async Task<ActionResult> SetOffline()
    {
        try
        {
            var userId = GetUserId();
            var success = await _doctorProfileService.SetOfflineAsync(userId);
            
            if (!success)
            {
                return NotFound(new { error = "Doctor profile not found" });
            }

            return Ok(new { status = "offline", message = "Status updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting doctor offline");
            return StatusCode(500, new { error = "Failed to update status" });
        }
    }

    /// <summary>
    /// Update availability status
    /// </summary>
    [HttpPut("availability/status")]
    public async Task<ActionResult> UpdateAvailabilityStatus([FromBody] UpdateAvailabilityRequest request)
    {
        try
        {
            var userId = GetUserId();
            var success = await _doctorProfileService.UpdateAvailabilityStatusAsync(userId, request.Status);
            
            if (!success)
            {
                return NotFound(new { error = "Doctor profile not found" });
            }

            return Ok(new { status = request.Status.ToString(), message = "Status updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating availability status");
            return StatusCode(500, new { error = "Failed to update status" });
        }
    }

    /// <summary>
    /// Get working hours
    /// </summary>
    [HttpGet("availability/working-hours")]
    public async Task<ActionResult<List<WorkingHoursSlot>>> GetWorkingHours()
    {
        try
        {
            var userId = GetUserId();
            var workingHours = await _doctorProfileService.GetWorkingHoursAsync(userId);
            
            if (workingHours == null)
            {
                return Ok(new List<WorkingHoursSlot>());
            }

            return Ok(workingHours);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving working hours");
            return StatusCode(500, new { error = "Failed to retrieve working hours" });
        }
    }

    /// <summary>
    /// Update working hours
    /// </summary>
    [HttpPut("availability/working-hours")]
    public async Task<ActionResult> UpdateWorkingHours([FromBody] List<WorkingHoursSlot> workingHours)
    {
        try
        {
            var userId = GetUserId();
            var success = await _doctorProfileService.UpdateWorkingHoursAsync(userId, workingHours);
            
            if (!success)
            {
                return NotFound(new { error = "Doctor profile not found" });
            }

            return Ok(new { message = "Working hours updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating working hours");
            return StatusCode(500, new { error = "Failed to update working hours" });
        }
    }

    #endregion

    #region Appointments

    /// <summary>
    /// Get doctor's upcoming appointments
    /// </summary>
    [HttpGet("appointments/upcoming")]
    public async Task<ActionResult<List<Appointment>>> GetUpcomingAppointments()
    {
        try
        {
            var userId = GetUserId();
            var appointments = await _appointmentService.GetDoctorAppointmentsAsync(userId);
            
            return Ok(appointments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving upcoming appointments");
            return StatusCode(500, new { error = "Failed to retrieve appointments" });
        }
    }

    /// <summary>
    /// Get doctor's appointments for a specific date
    /// </summary>
    [HttpGet("appointments/date/{date}")]
    public async Task<ActionResult<List<Appointment>>> GetAppointmentsByDate(DateTime date)
    {
        try
        {
            var userId = GetUserId();
            var appointments = await _appointmentService.GetDoctorAppointmentsAsync(userId, date);
            
            return Ok(appointments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving appointments by date");
            return StatusCode(500, new { error = "Failed to retrieve appointments" });
        }
    }

    /// <summary>
    /// Get appointment by ID
    /// </summary>
    [HttpGet("appointments/{id}")]
    public async Task<ActionResult<Appointment>> GetAppointment(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var appointment = await _appointmentService.GetByIdAsync(id);
            
            if (appointment == null)
            {
                return NotFound(new { error = "Appointment not found" });
            }

            // Verify this appointment belongs to the doctor
            if (appointment.DoctorId != userId)
            {
                return Forbid();
            }

            return Ok(appointment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving appointment");
            return StatusCode(500, new { error = "Failed to retrieve appointment" });
        }
    }

    /// <summary>
    /// Start appointment (mark as in progress)
    /// </summary>
    [HttpPost("appointments/{id}/start")]
    public async Task<ActionResult<Appointment>> StartAppointment(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var appointment = await _appointmentService.GetByIdAsync(id);
            
            if (appointment == null)
            {
                return NotFound(new { error = "Appointment not found" });
            }

            if (appointment.DoctorId != userId)
            {
                return Forbid();
            }

            var updated = await _appointmentService.UpdateStatusAsync(id, AppointmentStatus.InProgress);
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting appointment");
            return StatusCode(500, new { error = "Failed to start appointment" });
        }
    }

    /// <summary>
    /// Complete appointment
    /// </summary>
    [HttpPost("appointments/{id}/complete")]
    public async Task<ActionResult<Appointment>> CompleteAppointment(Guid id, [FromBody] CompleteAppointmentRequest? request = null)
    {
        try
        {
            var userId = GetUserId();
            var appointment = await _appointmentService.GetByIdAsync(id);
            
            if (appointment == null)
            {
                return NotFound(new { error = "Appointment not found" });
            }

            if (appointment.DoctorId != userId)
            {
                return Forbid();
            }

            var updated = await _appointmentService.CompleteAppointmentAsync(id, request?.Notes);
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing appointment");
            return StatusCode(500, new { error = "Failed to complete appointment" });
        }
    }

    /// <summary>
    /// Cancel appointment
    /// </summary>
    [HttpPost("appointments/{id}/cancel")]
    public async Task<ActionResult<Appointment>> CancelAppointment(Guid id, [FromBody] CancelAppointmentRequest request)
    {
        try
        {
            var userId = GetUserId();
            var appointment = await _appointmentService.GetByIdAsync(id);
            
            if (appointment == null)
            {
                return NotFound(new { error = "Appointment not found" });
            }

            if (appointment.DoctorId != userId)
            {
                return Forbid();
            }

            var updated = await _appointmentService.CancelAppointmentAsync(id, userId, request.Reason);
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling appointment");
            return StatusCode(500, new { error = "Failed to cancel appointment" });
        }
    }

    #endregion

    #region Prescriptions

    /// <summary>
    /// Create prescription for patient
    /// </summary>
    [HttpPost("prescriptions")]
    public async Task<ActionResult<Prescription>> CreatePrescription([FromBody] CreatePrescriptionRequest request)
    {
        try
        {
            var userId = GetUserId();
            
            // Check permission
            var canCreate = await _permissionService.HasPermissionAsync(userId, Permissions.PrescriptionCreate);
            if (!canCreate)
            {
                return Forbid();
            }

            var prescription = new Prescription
            {
                Id = Guid.NewGuid(),
                PatientId = request.PatientId,
                DoctorId = userId,
                AppointmentId = request.AppointmentId,
                Diagnosis = request.Diagnosis,
                Instructions = request.Instructions,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = request.ExpiresAt ?? DateTime.UtcNow.AddDays(30),
                Status = PrescriptionStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Create prescription first
            var created = await _prescriptionService.CreatePrescriptionAsync(prescription);
            
            // Then add items if provided
            if (request.Items != null && request.Items.Any())
            {
                foreach (var item in request.Items)
                {
                    created.Items.Add(new PrescriptionItem
                    {
                        Id = Guid.NewGuid(),
                        PrescriptionId = created.Id,
                        MedicationId = item.MedicationId,
                        Dosage = item.Dosage,
                        Frequency = item.Frequency,
                        Duration = item.Duration,
                        Quantity = item.Quantity,
                        Instructions = item.Instructions,
                        IsFulfilled = false
                    });
                }
            }
            
            return CreatedAtAction(nameof(GetPrescription), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating prescription");
            return StatusCode(500, new { error = "Failed to create prescription" });
        }
    }

    /// <summary>
    /// Get prescription by ID
    /// </summary>
    [HttpGet("prescriptions/{id}")]
    public async Task<ActionResult<Prescription>> GetPrescription(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var prescription = await _prescriptionService.GetByIdAsync(id);
            
            if (prescription == null)
            {
                return NotFound(new { error = "Prescription not found" });
            }

            // Verify this prescription belongs to the doctor
            if (prescription.DoctorId != userId)
            {
                return Forbid();
            }

            return Ok(prescription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving prescription");
            return StatusCode(500, new { error = "Failed to retrieve prescription" });
        }
    }

    /// <summary>
    /// Get doctor's prescriptions
    /// </summary>
    [HttpGet("prescriptions")]
    public async Task<ActionResult<List<Prescription>>> GetMyPrescriptions([FromQuery] PrescriptionStatus? status = null)
    {
        try
        {
            var userId = GetUserId();
            var prescriptions = await _prescriptionService.GetDoctorPrescriptionsAsync(userId);
            
            // Filter by status if provided
            if (status.HasValue)
            {
                prescriptions = prescriptions.Where(p => p.Status == status.Value).ToList();
            }
            
            return Ok(prescriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving prescriptions");
            return StatusCode(500, new { error = "Failed to retrieve prescriptions" });
        }
    }

    /// <summary>
    /// Get prescriptions for specific patient
    /// </summary>
    [HttpGet("patients/{patientId}/prescriptions")]
    public async Task<ActionResult<List<Prescription>>> GetPatientPrescriptions(Guid patientId)
    {
        try
        {
            var userId = GetUserId();
            var prescriptions = await _prescriptionService.GetPatientPrescriptionsAsync(patientId);
            
            // Filter to only show prescriptions created by this doctor
            var doctorPrescriptions = prescriptions.Where(p => p.DoctorId == userId).ToList();
            
            return Ok(doctorPrescriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving patient prescriptions");
            return StatusCode(500, new { error = "Failed to retrieve prescriptions" });
        }
    }

    /// <summary>
    /// Cancel prescription
    /// </summary>
    [HttpPost("prescriptions/{id}/cancel")]
    public async Task<ActionResult<Prescription>> CancelPrescription(Guid id, [FromBody] CancelPrescriptionRequest? request = null)
    {
        try
        {
            var userId = GetUserId();
            var prescription = await _prescriptionService.GetByIdAsync(id);
            
            if (prescription == null)
            {
                return NotFound(new { error = "Prescription not found" });
            }

            if (prescription.DoctorId != userId)
            {
                return Forbid();
            }

            var updated = await _prescriptionService.CancelPrescriptionAsync(id, request?.Reason);
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling prescription");
            return StatusCode(500, new { error = "Failed to cancel prescription" });
        }
    }

    #endregion

    #region Dashboard Statistics

    /// <summary>
    /// Get dashboard statistics
    /// </summary>
    [HttpGet("dashboard/stats")]
    public async Task<ActionResult<DoctorDashboardStats>> GetDashboardStats()
    {
        try
        {
            var userId = GetUserId();
            
            // Get all appointments for the doctor
            var allAppointments = await _appointmentService.GetDoctorAppointmentsAsync(userId, null);
            
            // Filter upcoming appointments (future and scheduled/confirmed)
            var upcomingAppointments = allAppointments
                .Where(a => a.ScheduledAt > DateTime.UtcNow && 
                           (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed))
                .ToList();
            
            // Get today's appointments
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1);
            var todayAppointments = allAppointments
                .Where(a => a.ScheduledAt >= todayStart && a.ScheduledAt < todayEnd)
                .ToList();
            
            // Get all prescriptions and filter
            var allPrescriptions = await _prescriptionService.GetDoctorPrescriptionsAsync(userId);
            var activePrescriptions = allPrescriptions.Where(p => p.Status == PrescriptionStatus.Active).ToList();
            var last30DaysStart = DateTime.UtcNow.AddDays(-30);
            var last30DaysPrescriptions = allPrescriptions
                .Where(p => p.IssuedAt >= last30DaysStart)
                .ToList();

            var stats = new DoctorDashboardStats
            {
                TodayAppointmentsCount = todayAppointments.Count,
                UpcomingAppointmentsCount = upcomingAppointments.Count,
                ActivePrescriptionsCount = activePrescriptions.Count,
                Last30DaysPrescriptionsCount = last30DaysPrescriptions.Count,
                NextAppointment = upcomingAppointments.OrderBy(a => a.ScheduledAt).FirstOrDefault()
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving dashboard stats");
            return StatusCode(500, new { error = "Failed to retrieve statistics" });
        }
    }

    #endregion
}

#region DTOs

public class CreateDoctorProfileRequest
{
    public required string LicenseNumber { get; set; }
    public string? LicenseAuthority { get; set; }
    public DateTime? LicenseExpiryDate { get; set; }
    public MedicalSpecialization Specialization { get; set; }
    public List<MedicalSpecialization>? AdditionalSpecializations { get; set; }
    public int YearsOfExperience { get; set; }
    public List<string>? Certifications { get; set; }
    public List<string>? Education { get; set; }
    public List<string>? Languages { get; set; }
    public string? Biography { get; set; }
    public decimal? ConsultationFee { get; set; }
    public bool AcceptingNewPatients { get; set; } = true;
    public List<Guid>? HospitalIds { get; set; }
}

public class UpdateDoctorProfileRequest
{
    public string? LicenseAuthority { get; set; }
    public DateTime? LicenseExpiryDate { get; set; }
    public List<MedicalSpecialization>? AdditionalSpecializations { get; set; }
    public int? YearsOfExperience { get; set; }
    public List<string>? Certifications { get; set; }
    public List<string>? Education { get; set; }
    public List<string>? Languages { get; set; }
    public string? Biography { get; set; }
    public decimal? ConsultationFee { get; set; }
    public bool? AcceptingNewPatients { get; set; }
    public List<Guid>? HospitalIds { get; set; }
}

public class UpdateAvailabilityRequest
{
    public DoctorAvailabilityStatus Status { get; set; }
}

public class CompleteAppointmentRequest
{
    public string? Notes { get; set; }
}

public class CancelAppointmentRequest
{
    public required string Reason { get; set; }
}

public class CreatePrescriptionRequest
{
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public string? Diagnosis { get; set; }
    public string? Instructions { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public List<PrescriptionItemRequest>? Items { get; set; }
}

public class PrescriptionItemRequest
{
    public Guid MedicationId { get; set; }
    public required string Dosage { get; set; }
    public required string Frequency { get; set; }
    public required string Duration { get; set; }
    public int Quantity { get; set; }
    public string? Instructions { get; set; }
}

public class CancelPrescriptionRequest
{
    public string? Reason { get; set; }
}

public class DoctorDashboardStats
{
    public int TodayAppointmentsCount { get; set; }
    public int UpcomingAppointmentsCount { get; set; }
    public int ActivePrescriptionsCount { get; set; }
    public int Last30DaysPrescriptionsCount { get; set; }
    public Appointment? NextAppointment { get; set; }
}

#endregion
