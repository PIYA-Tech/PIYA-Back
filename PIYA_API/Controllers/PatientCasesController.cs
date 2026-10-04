using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

// Deliberately separate from the staff chart. Never serialise ClinicalCase.Events here.
[ApiController, Route("api/patient/care-episodes"), Authorize(Roles = "Patient")]
public sealed class PatientCasesController(PharmacyApiDbContext db, IAuditService audit) : ControllerBase
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    [HttpGet]
    public async Task<IActionResult> List(int page = 1, int pageSize = 20)
    {
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100) return BadRequest();
        if (!await db.Users.AnyAsync(u => u.Id == Actor && u.Role == UserRole.Patient && u.IsActive)) return Forbid();
        var rows = await (from c in db.ClinicalCases.AsNoTracking()
                          join h in db.Hospitals on c.HospitalId equals h.Id
                          join d in db.Users on c.AttendingDoctorId equals d.Id
                          where c.PatientId == Actor
                          orderby c.AdmittedAt descending, c.Id
                          select new {
                              c.Id, c.Status, c.AdmittedAt, c.ClosedAt, c.Department, c.IsDemo,
                              HospitalName = h.Name, AttendingName = d.FirstName + " " + d.LastName
                          }).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        await audit.LogEntityActionAsync("ReadOwnCareEpisodes", "User", Actor.ToString(), Actor, "Patient viewed own admission summaries");
        return Ok(rows);
    }
}
