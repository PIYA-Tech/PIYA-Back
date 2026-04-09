using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/doctor")]
[Authorize(Roles = "Doctor,SuperAdmin")]
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
    /// Get current doctor's profile.
    /// Returns 204 No Content when the profile has not been created yet
    /// (allows the frontend to distinguish "no profile" from server errors without a 404 console warning).
    /// </summary>
    [HttpGet("profile")]
    public async Task<ActionResult<DoctorProfileResponseDto>> GetMyProfile()
    {
        try
        {
            var userId = GetUserId();
            var profile = await _doctorProfileService.GetByUserIdAsync(userId);

            if (profile == null)
                return NoContent(); // 204 — profile not yet created

            return Ok(DoctorProfileResponseDto.FromEntity(profile));
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
                HospitalIds = [],          // hospitals are assigned by admins only
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
            // HospitalIds are managed by admins only — not updated here
            
            profile.UpdatedAt = DateTime.UtcNow;

            var updated = await _doctorProfileService.UpdateProfileAsync(profile);
            return Ok(DoctorProfileResponseDto.FromEntity(updated));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating doctor profile");
            return StatusCode(500, new { error = "Failed to update profile" });
        }
    }

    #endregion

    #region Appointment Access

    /// <summary>
    /// Get the current doctor's appointments.
    /// Optional query params:
    /// - status: Scheduled, Confirmed, InProgress, Completed, Cancelled, NoShow, Rescheduled
    /// - scheduledDate=today for today's slice
    /// </summary>
    [HttpGet("appointments")]
    public async Task<ActionResult<List<Appointment>>> GetMyAppointments(
        [FromQuery] string? status = null,
        [FromQuery] string? scheduledDate = null)
    {
        try
        {
            var userId = GetUserId();
            DateTime? date = string.Equals(scheduledDate, "today", StringComparison.OrdinalIgnoreCase)
                ? DateTime.UtcNow.Date
                : null;

            AppointmentStatus? parsedStatus = null;
            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<AppointmentStatus>(status, true, out var s))
            {
                parsedStatus = s;
            }

            var appointments = await _appointmentService.GetDoctorAppointmentsAsync(userId, date, parsedStatus);

            return Ok(appointments.Select(AppointmentResponseDto.FromEntity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor appointments");
            return StatusCode(500, new { error = "Failed to retrieve appointments" });
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
    /// Get doctor's appointments for a specific date
    /// </summary>
    [HttpGet("appointments/date/{date}")]
    public async Task<ActionResult<List<Appointment>>> GetAppointmentsByDate(DateTime date)
    {
        try
        {
            var userId = GetUserId();
            var appointments = await _appointmentService.GetDoctorAppointmentsAsync(userId, date);
            
            return Ok(appointments.Select(AppointmentResponseDto.FromEntity));
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
            return Ok(AppointmentResponseDto.FromEntity(updated));
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
            return Ok(AppointmentResponseDto.FromEntity(updated));
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
    public async Task<ActionResult<Appointment>> CancelAppointment(Guid id, [FromBody] CancelAppointmentRequest? request = null)
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

            var reason = request?.Reason ?? "Cancelled by doctor";
            var updated = await _appointmentService.CancelAppointmentAsync(id, userId, reason);
            return Ok(AppointmentResponseDto.FromEntity(updated));
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

    /// <summary>
    /// Reschedule appointment (Doctor only) — sends email notification to patient
    /// </summary>
    [HttpPost("appointments/{id}/reschedule")]
    public async Task<ActionResult<Appointment>> RescheduleAppointment(Guid id, [FromBody] RescheduleAppointmentRequest request)
    {
        try
        {
            var userId = GetUserId();
            var appointment = await _appointmentService.GetByIdAsync(id);

            if (appointment == null)
                return NotFound(new { error = "Appointment not found" });

            if (appointment.DoctorId != userId)
                return Forbid();

            var rescheduled = await _appointmentService.RescheduleAppointmentAsync(id, request.NewScheduledAt);
            return Ok(rescheduled);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rescheduling appointment");
            return StatusCode(500, new { error = "Failed to reschedule appointment" });
        }
    }

    /// <summary>
    /// Get unique patients this doctor has seen (derived from appointment history).
    /// Returns deduplicated patient records with last-visit date and appointment count.
    /// </summary>
    [HttpGet("patients")]
    public async Task<ActionResult> GetMyPatients()
    {
        try
        {
            var userId = GetUserId();
            var appointments = await _appointmentService.GetDoctorAppointmentsAsync(userId);

            var patients = appointments
                .Where(a => a.Patient != null)
                .GroupBy(a => a.PatientId)
                .Select(g =>
                {
                    var patient = g.First().Patient!;
                    return new
                    {
                        patient.Id,
                        patient.FirstName,
                        patient.LastName,
                        patient.Email,
                        patient.PhoneNumber,
                        patient.DateOfBirth,
                        AppointmentCount = g.Count(),
                        LastVisit = g.Max(a => a.ScheduledAt),
                    };
                })
                .OrderByDescending(p => p.LastVisit)
                .ToList();

            return Ok(patients);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor's patients");
            return StatusCode(500, new { error = "Failed to retrieve patients" });
        }
    }

    #endregion

    #region Prescriptions

    /// <summary>
    /// Get all prescriptions issued by the authenticated doctor
    /// </summary>
    [HttpGet("prescriptions")]
    public async Task<ActionResult<List<Prescription>>> GetMyPrescriptions()
    {
        try
        {
            var userId = GetUserId();
            var prescriptions = await _prescriptionService.GetDoctorPrescriptionsAsync(userId);
            return Ok(prescriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor prescriptions");
            return StatusCode(500, new { error = "Failed to retrieve prescriptions" });
        }
    }

    /// <summary>
    /// Create prescription for patient — must be tied to a completed appointment
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

            // Require a completed appointment
            if (!request.AppointmentId.HasValue)
            {
                return BadRequest(new { error = "A completed appointment must be selected before creating a prescription." });
            }

            var appointment = await _appointmentService.GetByIdAsync(request.AppointmentId.Value);
            if (appointment == null || appointment.DoctorId != userId)
            {
                return BadRequest(new { error = "Appointment not found or does not belong to you." });
            }
            // Allow prescriptions for completed appointments OR for confirmed referral stub appointments.
            bool isReferralStub = appointment.Status == AppointmentStatus.Confirmed && appointment.ReferralId.HasValue;
            if (appointment.Status != AppointmentStatus.Completed && !isReferralStub)
            {
                return BadRequest(new { error = "Prescriptions can only be created for completed appointments or active referral appointments." });
            }
            if (appointment.PatientId != request.PatientId)
            {
                return BadRequest(new { error = "Patient does not match the selected appointment." });
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

            // Populate items BEFORE save so EF Core persists them in the same transaction
            if (request.Items != null && request.Items.Any())
            {
                foreach (var item in request.Items)
                {
                    prescription.Items.Add(new PrescriptionItem
                    {
                        Id = Guid.NewGuid(),
                        PrescriptionId = prescription.Id,
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

            var created = await _prescriptionService.CreatePrescriptionAsync(prescription);
            
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

            return Ok(PrescriptionResponseDto.FromEntity(prescription));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving prescription");
            return StatusCode(500, new { error = "Failed to retrieve prescription" });
        }
    }

    /// <summary>
    /// Get prescriptions for specific patient — all prescriptions visible to this doctor.
    /// Access is granted when the calling doctor has at least one non-cancelled appointment
    /// with the patient.
    /// </summary>
    [HttpGet("patients/{patientId}/prescriptions")]
    public async Task<ActionResult<List<Prescription>>> GetPatientPrescriptions(Guid patientId)
    {
        try
        {
            var userId = GetUserId();

            // Single DB EXISTS query — no full appointment list loaded into memory
            if (!await _appointmentService.HasDoctorPatientRelationshipAsync(userId, patientId))
                return Forbid();

            var prescriptions = await _prescriptionService.GetPatientPrescriptionsAsync(patientId);
            return Ok(prescriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving patient prescriptions");
            return StatusCode(500, new { error = "Failed to retrieve prescriptions" });
        }
    }

    /// <summary>
    /// Get full patient records for a specific patient: all prescriptions + appointment history
    /// with this doctor. Access is granted when the calling doctor has at least one
    /// non-cancelled appointment with the patient.
    /// </summary>
    [HttpGet("patients/{patientId}/records")]
    public async Task<ActionResult> GetPatientRecords(Guid patientId)
    {
        try
        {
            var userId = GetUserId();

            // Single DB EXISTS query for the access gate
            if (!await _appointmentService.HasDoctorPatientRelationshipAsync(userId, patientId))
                return Forbid();

            // Fetch only this doctor's appointments with this patient (not all doctor appointments)
            var patientAppointments = await _appointmentService.GetDoctorAppointmentsAsync(userId);
            var filteredAppointments = patientAppointments
                .Where(a => a.PatientId == patientId)
                .OrderByDescending(a => a.ScheduledAt)
                .ToList();

            var prescriptions = await _prescriptionService.GetPatientPrescriptionsAsync(patientId);
            var patientInfo = filteredAppointments.FirstOrDefault()?.Patient;

            var result = new
            {
                Patient = patientInfo == null ? null : new
                {
                    patientInfo.Id,
                    patientInfo.FirstName,
                    patientInfo.LastName,
                    patientInfo.Email,
                    patientInfo.PhoneNumber,
                    patientInfo.DateOfBirth,
                },
                Prescriptions = prescriptions,
                AppointmentHistory = filteredAppointments.Select(a => new
                {
                    a.Id,
                    a.ScheduledAt,
                    a.Status,
                    a.Reason,
                    a.AppointmentNotes,
                    a.DurationMinutes,
                    Hospital = a.Hospital == null ? null : new { a.Hospital.Id, a.Hospital.Name },
                }),
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving patient records for {PatientId}", patientId);
            return StatusCode(500, new { error = "Failed to retrieve patient records" });
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
            return Ok(PrescriptionResponseDto.FromEntity(updated));
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
            var now = DateTime.UtcNow;

            // Single DB round-trip for all appointment counts
            var apptCounts = await _appointmentService.GetDoctorAppointmentCountsAsync(userId, now);

            // DB-level counts — no full list loaded into memory
            var activePrescriptionCount = await _prescriptionService.CountDoctorPrescriptionsAsync(userId, status: PrescriptionStatus.Active);
            var last30DaysPrescriptionCount = await _prescriptionService.CountDoctorPrescriptionsAsync(userId, issuedFrom: now.AddDays(-30));

            var stats = new DoctorDashboardStats
            {
                TodayAppointmentsCount = apptCounts.TodayCount,
                UpcomingAppointmentsCount = apptCounts.UpcomingCount,
                ActivePrescriptionsCount = activePrescriptionCount,
                Last30DaysPrescriptionsCount = last30DaysPrescriptionCount,
                NextAppointment = apptCounts.NextAppointment
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
    // HospitalIds intentionally omitted — assigned by admins via PUT /api/hospital/doctors/{id}/hospitals
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
    // HospitalIds intentionally omitted — assigned by admins via PUT /api/hospital/doctors/{id}/hospitals
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
    public string? Reason { get; set; }
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
