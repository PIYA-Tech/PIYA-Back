using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/medicaltest")]
[Authorize]
public class MedicalTestController(
    IMedicalTestService medicalTestService,
    IReferralService referralService,
    IAppointmentService appointmentService,
    ILogger<MedicalTestController> logger) : ControllerBase
{
    private readonly IMedicalTestService _medicalTestService = medicalTestService;
    private readonly IReferralService _referralService = referralService;
    private readonly IAppointmentService _appointmentService = appointmentService;
    private readonly ILogger<MedicalTestController> _logger = logger;

    /// <summary>
    /// Get a medical test by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var test = await _medicalTestService.GetByIdAsync(id);
            if (test is null) return NotFound(new { error = "Test not found" });
            if (!await CanReadTestAsync(test)) return Forbid();
            return Ok(MedicalTestResponseDto.FromEntity(test));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching medical test {Id}", id);
            return StatusCode(500, new { error = "Failed to fetch test" });
        }
    }

    /// <summary>
    /// Get all tests for a referral
    /// </summary>
    [HttpGet("referral/{referralId:guid}")]
    public async Task<IActionResult> GetByReferral(Guid referralId)
    {
        try
        {
            var referral = await _referralService.GetByIdAsync(referralId);
            if (referral is null) return NotFound(new { error = "Referral not found" });
            if (!IsReferralParty(referral, GetUserId(), GetRole())) return Forbid();

            var tests = await _medicalTestService.GetByReferralAsync(referralId);
            return Ok(tests.Select(MedicalTestResponseDto.FromEntity).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tests for referral {ReferralId}", referralId);
            return StatusCode(500, new { error = "Failed to fetch tests" });
        }
    }

    /// <summary>
    /// Get all tests for an appointment
    /// </summary>
    [HttpGet("appointment/{appointmentId:guid}")]
    public async Task<IActionResult> GetByAppointment(Guid appointmentId)
    {
        try
        {
            var appointment = await _appointmentService.GetByIdAsync(appointmentId);
            if (appointment is null) return NotFound(new { error = "Appointment not found" });
            if (!IsAdministrator(GetRole()) &&
                appointment.PatientId != GetUserId() &&
                appointment.DoctorId != GetUserId())
            {
                return Forbid();
            }

            var tests = await _medicalTestService.GetByAppointmentAsync(appointmentId);
            return Ok(tests.Select(MedicalTestResponseDto.FromEntity).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tests for appointment {AppointmentId}", appointmentId);
            return StatusCode(500, new { error = "Failed to fetch tests" });
        }
    }

    /// <summary>
    /// Update test status and optionally add findings.
    /// Only the doctor who was assigned to perform the test (PerformedByDoctorId)
    /// or an Admin may update it — prevents any other doctor from modifying the record.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Roles = "Doctor,Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTestStatusRequest req)
    {
        try
        {
            var test = await _medicalTestService.GetByIdAsync(id);
            if (test is null) return NotFound(new { error = "Test not found" });

            var callerId = GetUserId();
            var role = GetRole();

            // A Doctor role alone is not object-level authority. Once a performer has
            // claimed a test only that doctor may mutate it; before that, an explicitly
            // linked ordering/referral/appointment doctor may claim it.
            if (!IsAdministrator(role) &&
                (test.PerformedByDoctorId.HasValue
                    ? test.PerformedByDoctorId != callerId
                    : !IsDoctorParty(test, callerId)))
            {
                return Forbid();
            }

            Guid? performerId = role == "Doctor" ? callerId : null;
            var updated = await _medicalTestService.UpdateStatusAsync(id, req.Status, req.Findings, performerId);
            return Ok(MedicalTestResponseDto.FromEntity(updated));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating test status {Id}", id);
            return StatusCode(500, new { error = "Failed to update test" });
        }
    }

    /// <summary>
    /// All tests for a patient (across all referrals and emergency/walk-in tests).
    /// Patients can only query their own history. Doctors require an established,
    /// non-cancelled care relationship; administrators retain audited support access.
    /// </summary>
    [HttpGet("patient/{patientId:guid}")]
    public async Task<IActionResult> GetByPatient(Guid patientId)
    {
        try
        {
            var callerId = GetUserId();
            var role = GetRole();

            if (callerId != patientId && !IsAdministrator(role))
            {
                if (role != "Doctor" ||
                    !await _appointmentService.HasDoctorPatientRelationshipAsync(callerId, patientId))
                {
                    return Forbid();
                }
            }

            var tests = await _medicalTestService.GetByPatientAsync(patientId);
            return Ok(tests.Select(MedicalTestResponseDto.FromEntity).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tests for patient {PatientId}", patientId);
            return StatusCode(500, new { error = "Failed to fetch patient tests" });
        }
    }

    /// <summary>
    /// Order a standalone (walk-in / ambulance) test for a patient without a referral.
    /// Doctor only.
    /// </summary>
    [HttpPost("standalone")]
    [Authorize(Roles = "Doctor,SuperAdmin")]
    public async Task<IActionResult> CreateStandalone([FromBody] CreateStandaloneTestRequest req)
    {
        try
        {
            var doctorId = GetUserId();
            var role = GetRole();

            if (!IsAdministrator(role) &&
                !await _appointmentService.HasDoctorPatientRelationshipAsync(doctorId, req.PatientId))
            {
                return Forbid();
            }

            if (req.AppointmentId.HasValue)
            {
                var appointment = await _appointmentService.GetByIdAsync(req.AppointmentId.Value);
                if (appointment is null)
                    return BadRequest(new { error = "Appointment not found" });
                if (appointment.PatientId != req.PatientId)
                    return BadRequest(new { error = "Appointment does not belong to the patient" });
                if (!IsAdministrator(role) && appointment.DoctorId != doctorId)
                    return Forbid();
            }

            var test = new MedicalTest
            {
                PatientId = req.PatientId,
                OrderedByDoctorId = doctorId,
                AppointmentId = req.AppointmentId,
                TestType = req.TestType,
                Notes = req.Notes,
                IsEmergency = true
            };
            var created = await _medicalTestService.CreateStandaloneAsync(test);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, MedicalTestResponseDto.FromEntity(created));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating standalone test");
            return StatusCode(500, new { error = "Failed to create test" });
        }
    }

    /// <summary>
    /// Attach an uploaded document to a test result (Doctor only)
    /// </summary>
    [HttpPost("{id:guid}/documents")]
    [Authorize(Roles = "Doctor,Admin,SuperAdmin")]
    public async Task<IActionResult> AttachDocument(Guid id, [FromBody] AttachDocumentRequest req)
    {
        try
        {
            var test = await _medicalTestService.GetByIdAsync(id);
            if (test is null) return NotFound(new { error = "Test not found" });

            var callerId = GetUserId();
            var role = GetRole();
            if (!IsAdministrator(role) &&
                (test.PerformedByDoctorId.HasValue
                    ? test.PerformedByDoctorId != callerId
                    : !IsDoctorParty(test, callerId)))
            {
                return Forbid();
            }

            var updated = await _medicalTestService.AttachDocumentAsync(
                id, req.DocumentId, callerId, IsAdministrator(role));
            return Ok(MedicalTestResponseDto.FromEntity(updated));
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error attaching document to test {Id}", id);
            return StatusCode(500, new { error = "Failed to attach document" });
        }
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID claim missing."));

    private string? GetRole() => User.FindFirst(ClaimTypes.Role)?.Value;

    private static bool IsAdministrator(string? role) => role is "Admin" or "SuperAdmin";

    private static bool IsReferralParty(Referral referral, Guid callerId, string? role) =>
        IsAdministrator(role) ||
        referral.PatientId == callerId ||
        referral.ReferringDoctorId == callerId ||
        referral.ReferredToDoctorId == callerId;

    private static bool IsDoctorParty(MedicalTest test, Guid callerId) =>
        test.OrderedByDoctorId == callerId ||
        test.PerformedByDoctorId == callerId ||
        test.Appointment?.DoctorId == callerId ||
        test.Referral?.ReferringDoctorId == callerId ||
        test.Referral?.ReferredToDoctorId == callerId;

    private async Task<bool> CanReadTestAsync(MedicalTest test)
    {
        var callerId = GetUserId();
        var role = GetRole();
        if (IsAdministrator(role) || test.PatientId == callerId || IsDoctorParty(test, callerId))
            return true;

        return role == "Doctor" &&
            await _appointmentService.HasDoctorPatientRelationshipAsync(callerId, test.PatientId);
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record UpdateTestStatusRequest(MedicalTestStatus Status, string? Findings);
public record AttachDocumentRequest(Guid DocumentId);
public record CreateStandaloneTestRequest(
    Guid PatientId,
    MedicalTestType TestType,
    string? Notes,
    Guid? AppointmentId);
