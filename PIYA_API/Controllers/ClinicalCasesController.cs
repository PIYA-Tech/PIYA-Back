using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Middleware;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController, Route("api/clinical-cases"), Authorize(Roles = "Doctor")]
public sealed class ClinicalCasesController(PharmacyApiDbContext db, IAuditService audit) : ControllerBase
{
    private Guid Actor => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private bool Demo => ConferenceDoctorScope.IsDoctor(Actor);
    private IQueryable<ClinicalCase> Assigned => db.ClinicalCases.Where(c => c.AttendingDoctorId == Actor &&
        (!Demo || c.IsDemo && c.PatientId == ConferenceDoctorScope.PatientId));

    private async Task<bool> StaffAllowed(Guid hospitalId)
    {
        if (!await db.Users.AnyAsync(u => u.Id == Actor && u.IsActive && u.Role == UserRole.Doctor)) return false;
        if (!await db.Hospitals.AnyAsync(h => h.Id == hospitalId && h.IsActive)) return false;
        var profile = await db.DoctorProfiles.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == Actor);
        if (profile == null || !profile.HospitalIds.Contains(hospitalId)) return false;
        if (Demo) return await db.Users.AnyAsync(u => u.Id == ConferenceDoctorScope.PatientId && u.IsActive &&
            u.Role == UserRole.Patient && u.Username == "conference_patient" && u.Email == "conference.patient@example.invalid");
        if (string.IsNullOrWhiteSpace(profile.LicenseNumber) || profile.LicenseExpiryDate <= DateTime.UtcNow) return false;
        // Explicit hospital-scoped privilege. A scan or general Doctor role never grants admission rights.
        var resource = hospitalId.ToString();
        return await db.UserPermissions.AnyAsync(p => p.UserId == Actor && p.Permission == "ClinicalCase.Admit" &&
            p.ResourceId == resource && p.IsActive && (p.ExpiresAt == null || p.ExpiresAt > DateTime.UtcNow));
    }

    [HttpGet("facilities")]
    public async Task<IActionResult> Facilities()
    {
        var profile = await db.DoctorProfiles.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == Actor);
        var result = new List<object>();
        foreach (var id in profile?.HospitalIds ?? [])
            if (await StaffAllowed(id)) {
                var h = await db.Hospitals.AsNoTracking().SingleAsync(h => h.Id == id);
                result.Add(new { h.Id, h.Name });
            }
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> List(string? status = null, int page = 1, int pageSize = 100)
    {
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100 || status is not (null or "Active" or "Discharged"))
            return BadRequest(new { error = "Use Active or Discharged, a positive page and pageSize between 1 and 100." });
        var allowed = new List<Guid>();
        foreach (var hospital in await Assigned.Select(c => c.HospitalId).Distinct().ToListAsync())
            if (await StaffAllowed(hospital)) allowed.Add(hospital);
        // Authorise before pagination: inaccessible rows must not hide accessible episodes.
        var query = Assigned.AsNoTracking().Where(c => allowed.Contains(c.HospitalId));
        if (status != null) query = query.Where(c => c.Status == status);
        var rows = await query.OrderByDescending(c => c.AdmittedAt).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var result = new List<object>();
        foreach (var c in rows) if (await StaffAllowed(c.HospitalId)) result.Add(await Summary(c));
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var c = await Assigned.AsNoTracking().Include(c => c.Events).SingleOrDefaultAsync(c => c.Id == id);
        if (c == null || !await StaffAllowed(c.HospitalId)) return NotFound();
        await audit.LogEntityActionAsync("ReadClinicalCase", "ClinicalCase", id.ToString(), Actor, "Assigned care episode read");
        return Ok(new { Case = await Summary(c), Events = c.Events.OrderBy(e => e.RecordedAt),
            Monitoring = c.IsDemo ? "SimulationOnly" : "NotConnected" });
    }

    [HttpPost("admit")]
    public async Task<IActionResult> Admit(AdmitCaseRequest request)
    {
        if (!request.AcceptResponsibility || string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.Department))
            return BadRequest(new { error = "Confirm responsibility, department and admission reason." });
        if (!await StaffAllowed(request.HospitalId)) return Forbid();
        var grant = await db.EmergencyAccessGrants.AsNoTracking().SingleOrDefaultAsync(g => g.Id == request.GrantId &&
            g.RequesterId == Actor && g.RevokedAt == null && g.ExpiresAt > DateTime.UtcNow &&
            (!Demo || g.PatientId == ConferenceDoctorScope.PatientId));
        if (grant == null || !await db.Users.AnyAsync(u => u.Id == grant.PatientId && u.IsActive && u.Role == UserRole.Patient) ||
            !await db.EmergencyHealthProfiles.AnyAsync(p => p.PatientId == grant.PatientId && p.IsSharingEnabled && p.ShareTokenExpiresAt > DateTime.UtcNow))
            return BadRequest(new { error = "Fresh authorised emergency access is required before admission." });
        var existing = await db.ClinicalCases.AsNoTracking().FirstOrDefaultAsync(c => c.AdmissionGrantId == grant.Id ||
            c.PatientId == grant.PatientId && c.HospitalId == request.HospitalId && c.Status == "Active");
        if (existing != null) return Conflict(new { error = "An admission already exists. Refresh your assigned cases." });
        var c = new ClinicalCase { PatientId = grant.PatientId, AttendingDoctorId = Actor, HospitalId = request.HospitalId,
            AdmissionGrantId = grant.Id, Department = request.Department.Trim(), Bed = request.Bed.Trim(),
            AdmissionReason = request.Reason.Trim(), IsDemo = Demo };
        c.Events.Add(Event(c, "Admission", "Attending responsibility explicitly accepted. " + c.AdmissionReason));
        db.ClinicalCases.Add(c);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { error = "Admission could not be saved. Refresh before trying again." }); }
        return Ok(await Summary(c));
    }

    [HttpPost("admit-from-visit")]
    public Task<IActionResult> AdmitFromVisit(AdmitVisitRequest request) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() => AdmitFromVisitCore(request));

    private async Task<IActionResult> AdmitFromVisitCore(AdmitVisitRequest request)
    {
        if (!request.AcceptResponsibility || string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.Department))
            return BadRequest(new { error = "Confirm responsibility, department and admission reason." });
        // Keep the source visit stable while authorising and creating the episode.
        if (db.Database.IsRelational()) db.ChangeTracker.Clear();
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
        var visit = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == request.AppointmentId &&
            a.DoctorId == Actor && (!Demo || a.PatientId == ConferenceDoctorScope.PatientId));
        if (visit == null) return NotFound();
        if (!await StaffAllowed(visit.HospitalId)) return Forbid();
        if (visit.Status is not (AppointmentStatus.InProgress or AppointmentStatus.Completed) || visit.ScheduledAt > DateTime.UtcNow)
            return BadRequest(new { error = "Start the consultation before admitting. Cancelled, missed, rescheduled and future visits cannot be used." });
        if (!await db.Users.AnyAsync(u => u.Id == visit.PatientId && u.IsActive && u.Role == UserRole.Patient))
            return BadRequest(new { error = "An active patient account is required." });
        if (await db.ClinicalCases.AnyAsync(c => c.AdmissionAppointmentId == visit.Id ||
            c.PatientId == visit.PatientId && c.HospitalId == visit.HospitalId && c.Status == "Active"))
            return Conflict(new { error = "An admission already exists. Refresh your assigned cases." });
        var c = new ClinicalCase { PatientId = visit.PatientId, AttendingDoctorId = Actor, HospitalId = visit.HospitalId,
            AdmissionAppointmentId = visit.Id, Department = request.Department.Trim(), Bed = request.Bed?.Trim() ?? "",
            AdmissionReason = request.Reason.Trim(), IsDemo = Demo };
        c.Events.Add(Event(c, "Admission", "Admitted from consultation " + visit.Id + ". Attending responsibility explicitly accepted. " + c.AdmissionReason));
        db.ClinicalCases.Add(c);
        try {
            await db.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();
        }
        catch (DbUpdateException) { return Conflict(new { error = "Admission could not be saved. Refresh before trying again." }); }
        catch (Npgsql.PostgresException e) when (e.SqlState is "40001" or "40P01") {
            return Conflict(new { error = "The visit changed during admission. Refresh before trying again." });
        }
        return Ok(await Summary(c));
    }

    [HttpPost("{id:guid}/events")]
    public async Task<IActionResult> AddEvent(Guid id, CaseActionRequest request)
    {
        var c = await Assigned.Include(c => c.Events).SingleOrDefaultAsync(c => c.Id == id);
        if (c == null || !await StaffAllowed(c.HospitalId)) return NotFound();
        if (c.Status != "Active" || c.Version != request.Version) return Conflict(new { error = "The case changed or is closed. Refresh before continuing." });
        if (!new[] { "Note", "Correction", "DemoReading", "DemoAlert", "Acknowledge", "Resolve", "Discharge" }.Contains(request.Action))
            return BadRequest(new { error = "Unsupported case action." });
        if (request.Action.StartsWith("Demo") && !c.IsDemo) return Forbid();
        if (new[] { "Note", "Correction", "Resolve", "Discharge" }.Contains(request.Action) && string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "A note or outcome is required." });
        if (request.Action == "Correction" && !c.Events.Any(e => e.Id == request.RelatedEventId && e.Kind == "Note" && e.AuthorId == Actor))
            return BadRequest(new { error = "Select your original note in this case. Corrections append to the chart; they never replace it." });
        if (request.Action is "Acknowledge" or "Resolve") {
            var alert = c.Events.SingleOrDefault(e => e.Id == request.RelatedEventId && e.Kind == "DemoAlert");
            if (alert == null) return BadRequest(new { error = "Alert not found in this case." });
            if (c.Events.Any(e => e.RelatedEventId == alert.Id && (e.Kind == request.Action || e.Kind == "Resolve")))
                return Conflict(new { error = "This alert has already been handled." });
            if (request.Action == "Resolve" && !c.Events.Any(e => e.RelatedEventId == alert.Id && e.Kind == "Acknowledge"))
                return BadRequest(new { error = "Acknowledge the alert first." });
        }
        if (request.Action == "Discharge" && c.Events.Any(e => e.Kind == "DemoAlert" && !c.Events.Any(r => r.Kind == "Resolve" && r.RelatedEventId == e.Id)))
            return Conflict(new { error = "Resolve open demonstration alerts before discharge." });
        var entry = Event(c, request.Action, request.Text.Trim());
        entry.RelatedEventId = request.RelatedEventId;
        if (request.Action is "DemoReading" or "DemoAlert") {
            entry.Source = "PIYA conference simulator — not a medical device";
            entry.HeartRate = request.Action == "DemoAlert" ? 130 : 76 + c.Events.Count % 7;
            entry.OxygenSaturation = request.Action == "DemoAlert" ? 88 : 98;
            entry.Text = request.Action == "DemoAlert" ? "SIMULATED alert scenario. Not a clinical threshold or live alarm." : "SIMULATED vital-sign sample.";
        }
        c.Events.Add(entry); db.ClinicalCaseEvents.Add(entry); c.Version++;
        if (request.Action == "Discharge") { c.Status = "Discharged"; c.ClosedAt = DateTime.UtcNow; }
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "The case changed. Refresh before continuing." }); }
        return Ok(new { Case = await Summary(c), Events = c.Events.OrderBy(e => e.RecordedAt), Monitoring = c.IsDemo ? "SimulationOnly" : "NotConnected" });
    }

    private ClinicalCaseEvent Event(ClinicalCase c, string kind, string text) => new() { ClinicalCaseId = c.Id, AuthorId = Actor, Kind = kind, Text = text };
    private async Task<object> Summary(ClinicalCase c)
    {
        var patient = await db.Users.AsNoTracking().SingleAsync(u => u.Id == c.PatientId);
        var doctor = await db.Users.AsNoTracking().SingleAsync(u => u.Id == c.AttendingDoctorId);
        var hospital = await db.Hospitals.AsNoTracking().SingleAsync(h => h.Id == c.HospitalId);
        return new { c.Id, c.PatientId, PatientName = patient.FirstName + " " + patient.LastName, c.AttendingDoctorId,
            AttendingName = doctor.FirstName + " " + doctor.LastName, c.HospitalId, HospitalName = hospital.Name,
            c.Department, c.Bed, c.AdmissionReason, c.Status, c.IsDemo, c.AdmittedAt, c.ClosedAt, c.Version,
            c.AdmissionAppointmentId, AdmissionSource = c.AdmissionAppointmentId.HasValue ? "Visit" : "EmergencyQR" };
    }
}

public sealed record AdmitCaseRequest(Guid GrantId, Guid HospitalId, [MaxLength(100)] string Department,
    [MaxLength(80)] string Bed, [MaxLength(1000)] string Reason, bool AcceptResponsibility);
public sealed record CaseActionRequest(int Version, [MaxLength(40)] string Action, [MaxLength(4000)] string Text, Guid? RelatedEventId = null);
public sealed record AdmitVisitRequest(Guid AppointmentId, [MaxLength(100)] string Department,
    [MaxLength(80)] string? Bed, [MaxLength(1000)] string Reason, bool AcceptResponsibility);
