using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/patient-lab-results")]
[Authorize]
public sealed class PatientLabResultsController(IStructuredLabResultService results) : ControllerBase
{
    private readonly IStructuredLabResultService _results = results;

    [HttpGet("mine")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<IReadOnlyList<StructuredLabReportResponse>>> GetMine()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _results.GetPatientReportsAsync(userId));
    }

    [HttpGet("mine/{medicalTestId:guid}")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<StructuredLabReportResponse>> GetMineById(Guid medicalTestId)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var report = await _results.GetPatientReportAsync(userId, medicalTestId);
        return report is null ? NotFound(new { error = "Medical test not found." }) : Ok(report);
    }

    [HttpPut("{medicalTestId:guid}/analytes")]
    [Authorize(Roles = "Doctor,SuperAdmin")]
    public async Task<ActionResult<StructuredLabReportResponse>> ReplaceAnalytes(
        Guid medicalTestId, [FromBody] ReplaceLabAnalytesRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            return Ok(await _results.ReplaceAnalytesAsync(
                medicalTestId, userId, User.IsInRole("SuperAdmin"), request));
        }
        catch (PatientHealthNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
