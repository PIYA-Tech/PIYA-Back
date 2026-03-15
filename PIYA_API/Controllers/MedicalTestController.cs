using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/medicaltest")]
[Authorize]
public class MedicalTestController(
    IMedicalTestService medicalTestService,
    ILogger<MedicalTestController> logger) : ControllerBase
{
    private readonly IMedicalTestService _medicalTestService = medicalTestService;
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
            return test is null ? NotFound(new { error = "Test not found" }) : Ok(test);
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
            var tests = await _medicalTestService.GetByReferralAsync(referralId);
            return Ok(tests);
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
            var tests = await _medicalTestService.GetByAppointmentAsync(appointmentId);
            return Ok(tests);
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
    [Authorize(Roles = "Doctor,SuperAdmin")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTestStatusRequest req)
    {
        try
        {
            var test = await _medicalTestService.GetByIdAsync(id);
            if (test is null) return NotFound(new { error = "Test not found" });

            var callerId = GetUserId();
            var role = GetRole();

            // Enforce ownership: only the performing doctor (or admin) may update
            if (role is not ("Admin" or "SuperAdmin") && test.PerformedByDoctorId != callerId)
                return Forbid();

            var updated = await _medicalTestService.UpdateStatusAsync(id, req.Status, req.Findings);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating test status {Id}", id);
            return StatusCode(500, new { error = "Failed to update test" });
        }
    }

    /// <summary>
    /// All tests for a patient (across all referrals and emergency/walk-in tests).
    /// Patients can only query their own history; doctors and admins may query any patient.
    /// </summary>
    [HttpGet("patient/{patientId:guid}")]
    public async Task<IActionResult> GetByPatient(Guid patientId)
    {
        try
        {
            var callerId = GetUserId();
            var role = GetRole();

            if (role is not ("Doctor" or "Admin" or "SuperAdmin") && callerId != patientId)
                return Forbid();

            var tests = await _medicalTestService.GetByPatientAsync(patientId);
            return Ok(tests);
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
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
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
    [Authorize(Roles = "Doctor,SuperAdmin")]
    public async Task<IActionResult> AttachDocument(Guid id, [FromBody] AttachDocumentRequest req)
    {
        try
        {
            var updated = await _medicalTestService.AttachDocumentAsync(id, req.DocumentId);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
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
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record UpdateTestStatusRequest(MedicalTestStatus Status, string? Findings);
public record AttachDocumentRequest(Guid DocumentId);
public record CreateStandaloneTestRequest(
    Guid PatientId,
    MedicalTestType TestType,
    string? Notes,
    Guid? AppointmentId);
