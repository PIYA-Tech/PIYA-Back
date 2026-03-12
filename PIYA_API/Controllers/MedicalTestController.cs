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
    /// Update test status and optionally add findings (Doctor only)
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Roles = "Doctor,SuperAdmin")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTestStatusRequest req)
    {
        try
        {
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
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record UpdateTestStatusRequest(MedicalTestStatus Status, string? Findings);
public record AttachDocumentRequest(Guid DocumentId);
