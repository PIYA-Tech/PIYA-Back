using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/patient-medication-pickups")]
[Authorize]
public sealed class PatientMedicationPickupsController(IPatientPickupService pickups) : ControllerBase
{
    private readonly IPatientPickupService _pickups = pickups;

    [HttpGet("mine")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<IReadOnlyList<PharmacyPickupResponse>>> GetMine()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _pickups.GetPatientHistoryAsync(userId));
    }

    [HttpGet("pharmacy/{pharmacyId:guid}")]
    [Authorize(Roles = "Pharmacist,PharmacyManager,Admin,SuperAdmin")]
    public async Task<ActionResult<IReadOnlyList<PharmacyPickupResponse>>> GetPharmacy(Guid pharmacyId)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            return Ok(await _pickups.GetPharmacyHistoryAsync(pharmacyId, userId, IsAdmin));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("refill-requests/{refillRequestId:guid}/collect")]
    [Authorize(Roles = "Pharmacist,PharmacyManager,Admin,SuperAdmin")]
    public async Task<ActionResult<PharmacyPickupResponse>> Collect(
        Guid refillRequestId, [FromBody] CollectPharmacyPickupRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            return Ok(await _pickups.CollectAsync(
                refillRequestId, userId, IsAdmin, request.QuantityCollected));
        }
        catch (PatientHealthNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (PatientHealthConflictException exception) { return Conflict(new { error = exception.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    private bool IsAdmin => User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
    private bool TryGetUserId(out Guid userId) => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
