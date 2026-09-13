using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using PIYA_API.Middleware;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/emergencyaccess")]
[Authorize]
public class EmergencyAccessController(
    PharmacyApiDbContext db,
    IAuditService auditService,
    IFcmService fcmService,
    ILogger<EmergencyAccessController> logger) : ControllerBase
{
    private readonly PharmacyApiDbContext _db = db;
    private readonly IAuditService _auditService = auditService;
    private readonly IFcmService _fcmService = fcmService;
    private readonly ILogger<EmergencyAccessController> _logger = logger;

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("profile")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<EmergencyProfileResponse>> GetProfile()
    {
        var profile = await _db.EmergencyHealthProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PatientId == CurrentUserId);
        return Ok(EmergencyProfileResponse.From(profile));
    }

    [HttpPut("profile")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<EmergencyProfileResponse>> UpdateProfile(
        [FromBody] UpdateEmergencyProfileRequest request)
    {
        if (request.BloodType?.Length > 16 || request.Allergies?.Length > 4000 ||
            request.ChronicConditions?.Length > 4000 || request.CurrentMedications?.Length > 4000 ||
            request.EmergencyContacts?.Length > 4000 || request.AdditionalNotes?.Length > 4000)
            return BadRequest(new { error = "One or more emergency profile fields are too long" });

        var profile = await _db.EmergencyHealthProfiles
            .SingleOrDefaultAsync(item => item.PatientId == CurrentUserId);
        if (profile is null)
        {
            profile = new EmergencyHealthProfile { Id = Guid.NewGuid(), PatientId = CurrentUserId };
            _db.EmergencyHealthProfiles.Add(profile);
        }

        profile.BloodType = Clean(request.BloodType);
        profile.Allergies = Clean(request.Allergies);
        profile.ChronicConditions = Clean(request.ChronicConditions);
        profile.CurrentMedications = Clean(request.CurrentMedications);
        profile.EmergencyContacts = Clean(request.EmergencyContacts);
        profile.AdditionalNotes = Clean(request.AdditionalNotes);
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "UpdateEmergencyProfile", "EmergencyHealthProfile", profile.Id.ToString(),
            CurrentUserId, "Patient updated their emergency health summary");
        return Ok(EmergencyProfileResponse.From(profile));
    }

    /// <summary>
    /// Enables sharing and returns a new plaintext token once. Only its SHA-256
    /// hash is retained. The patient can rotate or revoke it at any time.
    /// </summary>
    [HttpPost("share-token")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<EmergencyShareTokenResponse>> GenerateShareToken()
    {
        var profile = await _db.EmergencyHealthProfiles
            .SingleOrDefaultAsync(item => item.PatientId == CurrentUserId);
        if (profile is null)
            return BadRequest(new { error = "Create your emergency health profile first" });

        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        profile.ShareTokenHash = HashToken(token);
        profile.ShareTokenExpiresAt = DateTime.UtcNow.AddHours(24);
        profile.IsSharingEnabled = true;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "EnableEmergencySharing", "EmergencyHealthProfile", profile.Id.ToString(),
            CurrentUserId, "Patient generated a 24-hour emergency share token");
        return Ok(new EmergencyShareTokenResponse(token, profile.ShareTokenExpiresAt.Value));
    }

    [HttpDelete("share-token")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> DisableSharing()
    {
        var profile = await _db.EmergencyHealthProfiles
            .SingleOrDefaultAsync(item => item.PatientId == CurrentUserId);
        if (profile is null) return NoContent();

        profile.IsSharingEnabled = false;
        profile.ShareTokenHash = null;
        profile.ShareTokenExpiresAt = null;
        profile.UpdatedAt = DateTime.UtcNow;
        var activeGrants = await _db.EmergencyAccessGrants
            .Where(item => item.PatientId == CurrentUserId && item.RevokedAt == null && item.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var grant in activeGrants)
        {
            grant.RevokedAt = DateTime.UtcNow;
            grant.RevokedByUserId = CurrentUserId;
        }
        await _db.SaveChangesAsync();
        await _auditService.LogEntityActionAsync(
            "DisableEmergencySharing", "EmergencyHealthProfile", profile.Id.ToString(),
            CurrentUserId, "Patient disabled emergency sharing and revoked active access windows");
        return NoContent();
    }

    [HttpPost("request")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<EmergencyRecordResponse>> RequestAccess(
        [FromBody] EmergencyAccessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.Reason) ||
            string.IsNullOrWhiteSpace(request.FacilityName))
            return BadRequest(new { error = "Token, clinical reason, and facility name are required" });
        if (request.Reason.Length > 500 || request.FacilityName?.Length > 300)
            return BadRequest(new { error = "Reason or facility name is too long" });

        var requester = await _db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == CurrentUserId);
        if (requester is null || !requester.IsActive || requester.Role != UserRole.Doctor) return Forbid();
        var isDemo = ConferenceDoctorScope.IsDoctor(CurrentUserId);

        var doctorProfile = await _db.DoctorProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == CurrentUserId);
        // The reserved conference identity is NOT a licensed clinician. Its sole
        // exception is scoped below to the fixed, verified fictional patient.
        if (!isDemo && (doctorProfile is null || string.IsNullOrWhiteSpace(doctorProfile.LicenseNumber) ||
            doctorProfile.LicenseExpiryDate is { } expiry && expiry <= DateTime.UtcNow))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "A current verified medical license is required" });

        var normalizedToken = request.Token.Trim().Replace("piya-emergency:", "", StringComparison.OrdinalIgnoreCase);
        var hash = HashToken(normalizedToken);
        var now = DateTime.UtcNow;
        var profiles = _db.EmergencyHealthProfiles.AsQueryable();
        if (isDemo)
            profiles = profiles.Where(item => item.PatientId == ConferenceDoctorScope.PatientId);
        var profile = await profiles
            .SingleOrDefaultAsync(item => item.ShareTokenHash == hash);
        if (profile is null || !profile.IsSharingEnabled || profile.ShareTokenExpiresAt is null || profile.ShareTokenExpiresAt <= now ||
            !await _db.Users.AsNoTracking().AnyAsync(u => u.Id == profile.PatientId && u.IsActive))
        {
            await AuditAccessAsync(CurrentUserId, null, false, "Invalid or expired emergency token");
            return Unauthorized(new { error = "This emergency share token is invalid, expired, or revoked" });
        }

        if (isDemo && !await IsFictionalPatientAsync(profile.PatientId))
        {
            await AuditAccessAsync(CurrentUserId, null, false, "Conference emergency demo: fictional patient identity mismatch");
            return Forbid();
        }

        var grant = new EmergencyAccessGrant
        {
            Id = Guid.NewGuid(),
            PatientId = profile.PatientId,
            RequesterId = CurrentUserId,
            Reason = isDemo ? "[FICTIONAL CONFERENCE DEMO] " + request.Reason.Trim() : request.Reason.Trim(),
            FacilityName = Clean(request.FacilityName),
            GrantedAt = now,
            ExpiresAt = isDemo && profile.ShareTokenExpiresAt.Value < now.AddMinutes(30)
                ? profile.ShareTokenExpiresAt.Value : now.AddMinutes(30)
        };
        _db.EmergencyAccessGrants.Add(grant);
        await _db.SaveChangesAsync();
        await AuditAccessAsync(CurrentUserId, profile.PatientId, true, grant.Reason);
        await NotifyPatientAsync(profile.PatientId, requester, grant);

        return Ok(await BuildRecordAsync(profile, grant));
    }

    [HttpGet("grants/{grantId:guid}")]
    public async Task<ActionResult<EmergencyRecordResponse>> GetGrant(Guid grantId)
    {
        var isDemo = ConferenceDoctorScope.IsDoctor(CurrentUserId);
        var grants = _db.EmergencyAccessGrants.AsNoTracking();
        if (isDemo)
            grants = grants.Where(item => item.PatientId == ConferenceDoctorScope.PatientId &&
                item.RequesterId == CurrentUserId);
        var grant = await grants
            .SingleOrDefaultAsync(item => item.Id == grantId);
        if (grant is null)
        {
            if (isDemo) await AuditAccessAsync(CurrentUserId, null, false, "Conference emergency demo: unavailable access grant");
            return NotFound(new { error = "Access grant not found" });
        }
        if (isDemo && !await IsFictionalPatientAsync(grant.PatientId)) return Forbid();
        if (grant.PatientId != CurrentUserId && grant.RequesterId != CurrentUserId) return Forbid();
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == grant.PatientId && u.IsActive) ||
            !await _db.Users.AsNoTracking().AnyAsync(u => u.Id == grant.RequesterId && u.IsActive && u.Role == UserRole.Doctor))
            return StatusCode(StatusCodes.Status410Gone, new { error = "This emergency access window has ended" });
        if (grant.RevokedAt != null || grant.ExpiresAt <= DateTime.UtcNow)
            return StatusCode(StatusCodes.Status410Gone, new { error = "This emergency access window has ended" });

        var profile = await _db.EmergencyHealthProfiles.AsNoTracking()
            .SingleAsync(item => item.PatientId == grant.PatientId);
        if (isDemo && (!profile.IsSharingEnabled || profile.ShareTokenExpiresAt is null ||
            profile.ShareTokenExpiresAt <= DateTime.UtcNow))
            return StatusCode(StatusCodes.Status410Gone, new { error = "This emergency access window has ended" });
        if (isDemo) await AuditAccessAsync(CurrentUserId, grant.PatientId, true, "Conference emergency demo: access grant read");
        return Ok(await BuildRecordAsync(profile, grant));
    }

    private Task<bool> IsFictionalPatientAsync(Guid id) => _db.Users.AsNoTracking().AnyAsync(u =>
        u.Id == id && u.Id == ConferenceDoctorScope.PatientId && u.IsActive && u.Role == UserRole.Patient &&
        u.Username == "conference_patient" && u.Email == "conference.patient@example.invalid");

    [HttpGet("access-log")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<IReadOnlyList<EmergencyAccessLogResponse>>> GetAccessLog()
    {
        var items = await _db.EmergencyAccessGrants.AsNoTracking()
            .Where(item => item.PatientId == CurrentUserId)
            .Include(item => item.Requester)
            .OrderByDescending(item => item.GrantedAt)
            .Take(50)
            .Select(item => new EmergencyAccessLogResponse(
                item.Id, item.Requester.FirstName + " " + item.Requester.LastName,
                item.Reason, item.FacilityName, item.GrantedAt, item.ExpiresAt,
                item.RevokedAt, item.RevokedAt == null && item.ExpiresAt > DateTime.UtcNow))
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost("grants/{grantId:guid}/revoke")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> RevokeGrant(Guid grantId)
    {
        var grant = await _db.EmergencyAccessGrants
            .SingleOrDefaultAsync(item => item.Id == grantId && item.PatientId == CurrentUserId);
        if (grant is null) return NotFound(new { error = "Access grant not found" });
        if (grant.RevokedAt is null)
        {
            grant.RevokedAt = DateTime.UtcNow;
            grant.RevokedByUserId = CurrentUserId;
            await _db.SaveChangesAsync();
            await _auditService.LogEntityActionAsync(
                "RevokeEmergencyAccess", "EmergencyAccessGrant", grant.Id.ToString(),
                CurrentUserId, "Patient revoked an emergency access window");
        }
        return NoContent();
    }

    private async Task<EmergencyRecordResponse> BuildRecordAsync(
        EmergencyHealthProfile profile, EmergencyAccessGrant grant)
    {
        var patient = await _db.Users.AsNoTracking().SingleAsync(item => item.Id == profile.PatientId);
        var appointments = await _db.Appointments.AsNoTracking()
            .Where(item => item.PatientId == profile.PatientId)
            .Include(item => item.Doctor).Include(item => item.Hospital)
            .OrderByDescending(item => item.ScheduledAt).Take(30)
            .Select(item => new EmergencyAppointmentSummary(
                item.Id, item.ScheduledAt, item.Status.ToString(), item.Reason, item.AppointmentNotes,
                item.Doctor.FirstName + " " + item.Doctor.LastName, item.Hospital.Name))
            .ToListAsync();
        var prescriptions = await _db.Prescriptions.AsNoTracking()
            .Where(item => item.PatientId == profile.PatientId)
            .Include(item => item.Doctor).Include(item => item.Items).ThenInclude(item => item.Medication)
            .OrderByDescending(item => item.IssuedAt).Take(30).ToListAsync();
        var tests = await _db.MedicalTests.AsNoTracking()
            .Where(item => item.PatientId == profile.PatientId)
            .OrderByDescending(item => item.CreatedAt).Take(30)
            .Select(item => new EmergencyTestSummary(item.Id, item.TestType.ToString(), item.Status.ToString(),
                item.Notes, item.Findings, item.ResultsAt ?? item.PerformedAt ?? item.CreatedAt, item.IsEmergency))
            .ToListAsync();
        var referrals = await _db.Referrals.AsNoTracking()
            .Where(item => item.PatientId == profile.PatientId)
            .OrderByDescending(item => item.CreatedAt).Take(30)
            .Select(item => new EmergencyReferralSummary(item.Id, item.ReferredToSpecialty.ToString(),
                item.Status.ToString(), item.Urgency.ToString(), item.Reason, item.ClinicalNotes,
                item.ResultNotes, item.CreatedAt))
            .ToListAsync();
        var notes = await _db.DoctorNotes.AsNoTracking()
            .Where(item => item.PatientId == profile.PatientId && item.Status == DoctorNoteStatus.Active)
            .OrderByDescending(item => item.IssuedAt).Take(30)
            .Select(item => new EmergencyNoteSummary(item.Id, item.Title, item.Summary,
                item.ClinicName, item.IssuedAt, item.ValidTo))
            .ToListAsync();

        return new EmergencyRecordResponse(
            grant.Id, grant.ExpiresAt,
            new EmergencyPatientSummary(patient.Id, patient.FirstName, patient.LastName, patient.DateOfBirth),
            EmergencyProfileResponse.From(profile), appointments,
            prescriptions.Select(item => new EmergencyPrescriptionSummary(
                item.Id, item.Status.ToString(), item.Diagnosis, item.Instructions,
                item.IssuedAt, item.ExpiresAt, item.Doctor.FirstName + " " + item.Doctor.LastName,
                item.Items.Select(line => new EmergencyMedicationSummary(
                    line.Medication.BrandName, line.Medication.GenericName, line.Dosage,
                    line.Frequency, line.Duration, line.Instructions, line.IsFulfilled)).ToList())).ToList(),
            tests, referrals, notes);
    }

    private async Task AuditAccessAsync(Guid requesterId, Guid? patientId, bool success, string detail)
    {
        await _auditService.LogAsync(new AuditLog
        {
            Id = Guid.NewGuid(), UserId = requesterId, Action = "EmergencyRecordAccess",
            EntityType = "User", EntityId = patientId?.ToString(), Description = detail,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(), HttpMethod = Request.Method,
            Endpoint = Request.Path, IsSuccess = success,
            Metadata = JsonSerializer.Serialize(new { patientId, accessType = "patient-controlled-token" })
        });
    }

    private async Task NotifyPatientAsync(Guid patientId, User requester, EmergencyAccessGrant grant)
    {
        try
        {
            await _fcmService.SendToUserAsync(patientId, "Emergency record accessed",
                $"Dr. {requester.FirstName} {requester.LastName} opened a 30-minute access window.",
                new Dictionary<string, string>
                {
                    ["type"] = "emergencyAccess", ["grantId"] = grant.Id.ToString()
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not deliver emergency-access alert to patient {PatientId}", patientId);
        }
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record UpdateEmergencyProfileRequest(
    string? BloodType, string? Allergies, string? ChronicConditions,
    string? CurrentMedications, string? EmergencyContacts, string? AdditionalNotes);
public sealed record EmergencyAccessRequest(string Token, string Reason, string FacilityName);
public sealed record EmergencyShareTokenResponse(string Token, DateTime ExpiresAt);
public sealed record EmergencyProfileResponse(
    Guid? Id, string? BloodType, string? Allergies, string? ChronicConditions,
    string? CurrentMedications, string? EmergencyContacts, string? AdditionalNotes,
    bool IsSharingEnabled, DateTime? ShareTokenExpiresAt, DateTime? UpdatedAt)
{
    public static EmergencyProfileResponse From(EmergencyHealthProfile? item) => item is null
        ? new(null, null, null, null, null, null, null, false, null, null)
        : new(item.Id, item.BloodType, item.Allergies, item.ChronicConditions,
            item.CurrentMedications, item.EmergencyContacts, item.AdditionalNotes,
            item.IsSharingEnabled && item.ShareTokenExpiresAt > DateTime.UtcNow,
            item.ShareTokenExpiresAt, item.UpdatedAt);
}
public sealed record EmergencyAccessLogResponse(
    Guid Id, string RequesterName, string Reason, string? FacilityName,
    DateTime GrantedAt, DateTime ExpiresAt, DateTime? RevokedAt, bool IsActive);
public sealed record EmergencyRecordResponse(
    Guid GrantId, DateTime AccessExpiresAt, EmergencyPatientSummary Patient,
    EmergencyProfileResponse EmergencyProfile,
    IReadOnlyList<EmergencyAppointmentSummary> Appointments,
    IReadOnlyList<EmergencyPrescriptionSummary> Prescriptions,
    IReadOnlyList<EmergencyTestSummary> MedicalTests,
    IReadOnlyList<EmergencyReferralSummary> Referrals,
    IReadOnlyList<EmergencyNoteSummary> DoctorNotes);
public sealed record EmergencyPatientSummary(Guid Id, string FirstName, string LastName, DateTime? DateOfBirth);
public sealed record EmergencyAppointmentSummary(
    Guid Id, DateTime ScheduledAt, string Status, string? Reason, string? Notes,
    string DoctorName, string HospitalName);
public sealed record EmergencyPrescriptionSummary(
    Guid Id, string Status, string? Diagnosis, string? Instructions, DateTime IssuedAt,
    DateTime ExpiresAt, string DoctorName, IReadOnlyList<EmergencyMedicationSummary> Medications);
public sealed record EmergencyMedicationSummary(
    string BrandName, string GenericName, string Dosage, string Frequency,
    string Duration, string? Instructions, bool IsFulfilled);
public sealed record EmergencyTestSummary(
    Guid Id, string Type, string Status, string? Notes, string? Findings, DateTime Date, bool IsEmergency);
public sealed record EmergencyReferralSummary(
    Guid Id, string Specialty, string Status, string Urgency, string Reason,
    string? ClinicalNotes, string? ResultNotes, DateTime CreatedAt);
public sealed record EmergencyNoteSummary(
    Guid Id, string Title, string? Summary, string? ClinicName, DateTime IssuedAt, DateTime ValidTo);
