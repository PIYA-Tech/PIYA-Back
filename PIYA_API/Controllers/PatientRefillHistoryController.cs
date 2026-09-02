using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/patient-medications/refill-requests")]
[Authorize]
public sealed class PatientRefillHistoryController(
    PharmacyApiDbContext db,
    IPharmacyStaffService pharmacyStaff) : ControllerBase
{
    private readonly PharmacyApiDbContext _db = db;
    private readonly IPharmacyStaffService _pharmacyStaff = pharmacyStaff;

    [HttpGet("{refillRequestId:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<PatientRefillStatusEventResponse>>> GetEvents(
        Guid refillRequestId)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();
        var refill = await _db.Set<PatientRefillRequest>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == refillRequestId);
        if (refill is null) return NotFound(new { error = "Refill request not found." });

        var isAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        var isProfessional = User.IsInRole("Pharmacist") || User.IsInRole("PharmacyManager") || isAdmin;
        if (User.IsInRole("Patient") && refill.PatientId != userId)
            return NotFound(new { error = "Refill request not found." });
        if (!User.IsInRole("Patient") &&
            (!isProfessional || (!isAdmin &&
                !await _pharmacyStaff.IsStaffAtPharmacyAsync(refill.PharmacyId, userId))))
            return Forbid();

        var events = await _db.Set<PatientRefillStatusEvent>().AsNoTracking()
            .Where(item => item.RefillRequestId == refill.Id)
            .OrderBy(item => item.OccurredAt)
            .ToListAsync();
        return Ok(events.Select(PatientRefillStatusEventResponse.From).ToList());
    }
}
