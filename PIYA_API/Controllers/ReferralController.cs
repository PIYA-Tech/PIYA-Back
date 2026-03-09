using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/referral")]
[Authorize]
public class ReferralController(
    IReferralService referralService,
    IPdfExportService pdfExportService,
    ILogger<ReferralController> logger) : ControllerBase
{
    private readonly IReferralService _referralService = referralService;
    private readonly IPdfExportService _pdfExportService = pdfExportService;
    private readonly ILogger<ReferralController> _logger = logger;

    // ── Create ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Doctor creates a referral from an existing appointment
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Create([FromBody] CreateReferralRequest req)
    {
        try
        {
            var doctorId = GetUserId();

            var referral = new Referral
            {
                ReferringDoctorId = doctorId,
                PatientId = req.PatientId,
                SourceAppointmentId = req.SourceAppointmentId,
                ReferredToSpecialty = req.ReferredToSpecialty,
                ReferredToDoctorId = req.ReferredToDoctorId,
                Urgency = req.Urgency,
                Reason = req.Reason,
                ClinicalNotes = req.ClinicalNotes,
                IsExternal = req.IsExternal,
                ExternalProviderName = req.ExternalProviderName,
                ExternalProviderContact = req.ExternalProviderContact
            };

            var created = await _referralService.CreateAsync(referral, req.OrderedTests ?? []);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating referral");
            return StatusCode(500, new { error = "Failed to create referral" });
        }
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Get a single referral by ID (parties to the referral + Admin)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var referral = await _referralService.GetByIdAsync(id);
            if (referral is null) return NotFound(new { error = "Referral not found" });

            var userId = GetUserId();
            var role = GetRole();

            if (role is not ("Admin" or "SuperAdmin") &&
                referral.PatientId != userId &&
                referral.ReferringDoctorId != userId &&
                referral.ReferredToDoctorId != userId)
            {
                return Forbid();
            }

            return Ok(referral);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching referral {Id}", id);
            return StatusCode(500, new { error = "Failed to fetch referral" });
        }
    }

    /// <summary>
    /// Patient: list my referrals
    /// </summary>
    [HttpGet("my-referrals")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> GetMyReferrals()
    {
        try
        {
            var list = await _referralService.GetByPatientAsync(GetUserId());
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching patient referrals");
            return StatusCode(500, new { error = "Failed to fetch referrals" });
        }
    }

    /// <summary>
    /// Doctor: list referrals I sent
    /// </summary>
    [HttpGet("sent")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> GetSent()
    {
        try
        {
            var list = await _referralService.GetByReferringDoctorAsync(GetUserId());
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching sent referrals");
            return StatusCode(500, new { error = "Failed to fetch sent referrals" });
        }
    }

    /// <summary>
    /// Doctor: list referrals sent to me
    /// </summary>
    [HttpGet("received")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> GetReceived()
    {
        try
        {
            var list = await _referralService.GetByReferredDoctorAsync(GetUserId());
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching received referrals");
            return StatusCode(500, new { error = "Failed to fetch received referrals" });
        }
    }

    /// <summary>
    /// List doctors available for a referral's specialty (for patient to choose from)
    /// </summary>
    [HttpGet("{id:guid}/available-doctors")]
    public async Task<IActionResult> GetAvailableDoctors(Guid id)
    {
        try
        {
            var doctors = await _referralService.GetAvailableDoctorsAsync(id);
            return Ok(doctors);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available doctors for referral {Id}", id);
            return StatusCode(500, new { error = "Failed to fetch available doctors" });
        }
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Patient assigns a specific doctor to a pending referral
    /// </summary>
    [HttpPost("{id:guid}/assign-doctor")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> AssignDoctor(Guid id, [FromBody] AssignDoctorRequest req)
    {
        try
        {
            var referral = await _referralService.GetByIdAsync(id);
            if (referral is null) return NotFound(new { error = "Referral not found" });
            if (referral.PatientId != GetUserId()) return Forbid();

            var updated = await _referralService.AssignDoctorAsync(id, req.DoctorId);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning doctor to referral {Id}", id);
            return StatusCode(500, new { error = "Failed to assign doctor" });
        }
    }

    /// <summary>
    /// Doctor accepts a referral
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Accept(Guid id)
    {
        try
        {
            var doctorId = GetUserId();
            var referral = await _referralService.GetByIdAsync(id);
            if (referral is null) return NotFound(new { error = "Referral not found" });
            if (referral.ReferredToDoctorId != doctorId) return Forbid();

            var updated = await _referralService.AcceptAsync(id, doctorId);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting referral {Id}", id);
            return StatusCode(500, new { error = "Failed to accept referral" });
        }
    }

    /// <summary>
    /// Doctor declines a referral
    /// </summary>
    [HttpPost("{id:guid}/decline")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Decline(Guid id, [FromBody] DeclineReferralRequest req)
    {
        try
        {
            var doctorId = GetUserId();
            var referral = await _referralService.GetByIdAsync(id);
            if (referral is null) return NotFound(new { error = "Referral not found" });
            if (referral.ReferredToDoctorId != doctorId) return Forbid();

            var updated = await _referralService.DeclineAsync(id, req.Reason);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error declining referral {Id}", id);
            return StatusCode(500, new { error = "Failed to decline referral" });
        }
    }

    /// <summary>
    /// Doctor marks referral complete with result notes
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteReferralRequest req)
    {
        try
        {
            var doctorId = GetUserId();
            var referral = await _referralService.GetByIdAsync(id);
            if (referral is null) return NotFound(new { error = "Referral not found" });
            if (referral.ReferredToDoctorId != doctorId) return Forbid();

            var updated = await _referralService.CompleteAsync(id, req.ResultNotes);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing referral {Id}", id);
            return StatusCode(500, new { error = "Failed to complete referral" });
        }
    }

    /// <summary>
    /// Cancel a referral (referring doctor or patient)
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var referral = await _referralService.GetByIdAsync(id);
            if (referral is null) return NotFound(new { error = "Referral not found" });
            if (referral.PatientId != userId && referral.ReferringDoctorId != userId) return Forbid();

            var updated = await _referralService.CancelAsync(id);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling referral {Id}", id);
            return StatusCode(500, new { error = "Failed to cancel referral" });
        }
    }

    // ── PDF Export ───────────────────────────────────────────────────────────

    /// <summary>
    /// Download a referral letter as PDF
    /// </summary>
    [HttpGet("{id:guid}/export-pdf")]
    public async Task<IActionResult> ExportPdf(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var role = GetRole();
            var referral = await _referralService.GetByIdAsync(id);
            if (referral is null) return NotFound(new { error = "Referral not found" });

            if (role is not ("Admin" or "SuperAdmin") &&
                referral.PatientId != userId &&
                referral.ReferringDoctorId != userId &&
                referral.ReferredToDoctorId != userId)
            {
                return Forbid();
            }

            var pdf = await _pdfExportService.GenerateReferralLetterPdfAsync(id);
            return File(pdf, "application/pdf", $"referral-{id:N}.pdf");
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting referral PDF {Id}", id);
            return StatusCode(500, new { error = "Failed to export PDF" });
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID claim missing."));

    private string? GetRole() => User.FindFirst(ClaimTypes.Role)?.Value;
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record CreateReferralRequest(
    Guid PatientId,
    Guid SourceAppointmentId,
    MedicalSpecialization ReferredToSpecialty,
    Guid? ReferredToDoctorId,
    ReferralUrgency Urgency,
    string Reason,
    string? ClinicalNotes,
    bool IsExternal,
    string? ExternalProviderName,
    string? ExternalProviderContact,
    List<MedicalTestType>? OrderedTests);

public record AssignDoctorRequest(Guid DoctorId);

public record DeclineReferralRequest(string? Reason);

public record CompleteReferralRequest(string ResultNotes);
