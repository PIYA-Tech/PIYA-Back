using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/care-circle")]
[Authorize]
public sealed class CareCircleController(
    PharmacyApiDbContext db,
    IAuditService auditService) : ControllerBase
{
    private const CareCircleScope AllScopes =
        CareCircleScope.Appointments | CareCircleScope.Medications |
        CareCircleScope.CareTimeline | CareCircleScope.EmergencyProfile |
        CareCircleScope.Documents;

    private readonly PharmacyApiDbContext _db = db;
    private readonly IAuditService _auditService = auditService;
    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpPost("invitations")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<CareCircleInvitationCreatedResponse>> CreateInvitation(
        [FromBody] CreateCareCircleInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.InviteeEmail);
        var now = DateTime.UtcNow;
        var accessExpiresAt = request.ExpiresAt ?? now.AddDays(90);
        if (email is null || email.Length > 254)
            return BadRequest(new { error = "A valid invitee email address is required." });
        if (!Enum.IsDefined(request.Role))
            return BadRequest(new { error = "A valid Care Circle role is required." });
        if (!ValidScopes(request.Scopes))
            return BadRequest(new { error = "Select at least one supported access scope." });
        if (accessExpiresAt <= now.AddMinutes(5) || accessExpiresAt > now.AddYears(1))
            return BadRequest(new { error = "Care Circle access must expire between five minutes and one year from now." });

        var patient = await _db.Set<User>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == CurrentUserId, cancellationToken);
        if (patient is null || !patient.IsActive) return Forbid();
        if (NormalizeEmail(patient.Email) == email)
            return BadRequest(new { error = "You cannot invite yourself to your Care Circle." });

        await MaterializeExpiryAsync(CurrentUserId, cancellationToken);
        var invitee = await _db.Set<User>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);
        if (invitee is not null)
        {
            var alreadyMember = await _db.Set<CareCircleMember>().AsNoTracking().AnyAsync(item =>
                item.PatientId == CurrentUserId && item.MemberUserId == invitee.Id &&
                item.Status == CareCircleMemberStatus.Active && item.ExpiresAt > now,
                cancellationToken);
            if (alreadyMember) return Conflict(new { error = "This person is already in your Care Circle." });
        }

        var pending = await _db.Set<CareCircleInvitation>().AsNoTracking().AnyAsync(item =>
            item.PatientId == CurrentUserId && item.InviteeEmailNormalized == email &&
            item.Status == CareCircleInvitationStatus.Pending && item.ExpiresAt > now,
            cancellationToken);
        if (pending) return Conflict(new { error = "An active invitation already exists for this email address." });

        var token = NewToken();
        var invitation = new CareCircleInvitation
        {
            Id = Guid.NewGuid(),
            PatientId = CurrentUserId,
            InviteeEmailNormalized = email,
            Role = request.Role,
            RequestedScopes = request.Scopes,
            Status = CareCircleInvitationStatus.Pending,
            TokenHash = HashToken(token),
            ExpiresAt = Min(now.AddDays(7), accessExpiresAt),
            AccessExpiresAt = accessExpiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Set<CareCircleInvitation>().Add(invitation);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "CreateCareCircleInvitation", nameof(CareCircleInvitation), invitation.Id.ToString(),
            CurrentUserId, $"Patient created a {request.Role} invitation with scoped, expiring consent");

        return CreatedAtAction(nameof(GetInvitations), new CareCircleInvitationCreatedResponse(
            ToResponse(invitation), token));
    }

    [HttpGet("invitations")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<IReadOnlyList<CareCircleInvitationResponse>>> GetInvitations(
        CancellationToken cancellationToken)
    {
        await MaterializeExpiryAsync(CurrentUserId, cancellationToken);
        var invitations = await _db.Set<CareCircleInvitation>().AsNoTracking()
            .Where(item => item.PatientId == CurrentUserId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Ok(invitations.Select(ToResponse));
    }

    [HttpPost("invitations/accept")]
    public async Task<ActionResult<CareCircleMemberResponse>> AcceptInvitation(
        [FromBody] AcceptCareCircleInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.TermsAccepted)
            return BadRequest(new { error = "Accept the Care Circle sharing terms before continuing." });
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 512)
            return BadRequest(new { error = "A valid invitation token is required." });

        var tokenHash = HashToken(request.Token.Trim());
        var invitation = await _db.Set<CareCircleInvitation>().SingleOrDefaultAsync(item =>
            item.TokenHash == tokenHash && item.Status == CareCircleInvitationStatus.Pending,
            cancellationToken);
        if (invitation is null) return NotFound(new { error = "Invitation not found or already used." });

        var now = DateTime.UtcNow;
        if (invitation.ExpiresAt <= now || invitation.AccessExpiresAt <= now)
        {
            invitation.Status = CareCircleInvitationStatus.Expired;
            invitation.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status410Gone, new { error = "This Care Circle invitation has expired." });
        }

        var invitee = await _db.Set<User>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == CurrentUserId, cancellationToken);
        if (invitee is null || !invitee.IsActive ||
            NormalizeEmail(invitee.Email) != invitation.InviteeEmailNormalized)
            return Forbid();
        if (invitation.PatientId == CurrentUserId)
            return BadRequest(new { error = "A patient cannot accept their own invitation." });

        var duplicate = await _db.Set<CareCircleMember>().AsNoTracking().AnyAsync(item =>
            item.PatientId == invitation.PatientId && item.MemberUserId == CurrentUserId &&
            item.Status == CareCircleMemberStatus.Active && item.ExpiresAt > now,
            cancellationToken);
        if (duplicate) return Conflict(new { error = "You already belong to this Care Circle." });

        var member = new CareCircleMember
        {
            Id = Guid.NewGuid(), PatientId = invitation.PatientId,
            MemberUserId = CurrentUserId, InvitationId = invitation.Id,
            Role = invitation.Role, Status = CareCircleMemberStatus.Active,
            JoinedAt = now, ExpiresAt = invitation.AccessExpiresAt,
            CreatedAt = now, UpdatedAt = now
        };
        var consent = NewConsent(member, invitation.RequestedScopes, invitation.PatientId, now);
        invitation.Status = CareCircleInvitationStatus.Accepted;
        invitation.AcceptedAt = now;
        invitation.RespondedByUserId = CurrentUserId;
        invitation.UpdatedAt = now;
        _db.Set<CareCircleMember>().Add(member);
        _db.Set<CareCircleConsent>().Add(consent);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "AcceptCareCircleInvitation", nameof(CareCircleMember), member.Id.ToString(),
            CurrentUserId, "Invitee accepted Care Circle terms and scoped access");
        return Ok(await BuildMemberResponseAsync(member, consent, showMember: false, cancellationToken));
    }

    [HttpPost("invitations/decline")]
    public async Task<IActionResult> DeclineInvitation(
        [FromBody] DeclineCareCircleInvitationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 512)
            return BadRequest(new { error = "A valid invitation token is required." });
        var invitee = await _db.Set<User>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == CurrentUserId, cancellationToken);
        var invitation = await _db.Set<CareCircleInvitation>().SingleOrDefaultAsync(item =>
            item.TokenHash == HashToken(request.Token.Trim()) &&
            item.Status == CareCircleInvitationStatus.Pending, cancellationToken);
        if (invitation is null) return NotFound(new { error = "Invitation not found or already used." });
        if (invitee is null || NormalizeEmail(invitee.Email) != invitation.InviteeEmailNormalized) return Forbid();

        invitation.Status = CareCircleInvitationStatus.Declined;
        invitation.DeclinedAt = DateTime.UtcNow;
        invitation.RespondedByUserId = CurrentUserId;
        invitation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "DeclineCareCircleInvitation", nameof(CareCircleInvitation), invitation.Id.ToString(),
            CurrentUserId, "Invitee declined Care Circle access");
        return NoContent();
    }

    [HttpDelete("invitations/{id:guid}")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken cancellationToken)
    {
        var invitation = await _db.Set<CareCircleInvitation>().SingleOrDefaultAsync(item =>
            item.Id == id && item.PatientId == CurrentUserId, cancellationToken);
        if (invitation is null) return NotFound(new { error = "Invitation not found." });
        if (invitation.Status != CareCircleInvitationStatus.Pending)
            return Conflict(new { error = "Only a pending invitation can be revoked." });
        invitation.Status = CareCircleInvitationStatus.Revoked;
        invitation.RevokedAt = DateTime.UtcNow;
        invitation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "RevokeCareCircleInvitation", nameof(CareCircleInvitation), invitation.Id.ToString(),
            CurrentUserId, "Patient revoked a pending Care Circle invitation");
        return NoContent();
    }

    [HttpGet("members")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<IReadOnlyList<CareCircleMemberResponse>>> GetMembers(
        CancellationToken cancellationToken)
    {
        await MaterializeExpiryAsync(CurrentUserId, cancellationToken);
        var members = await _db.Set<CareCircleMember>().AsNoTracking()
            .Where(item => item.PatientId == CurrentUserId)
            .OrderByDescending(item => item.JoinedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Ok(await BuildMemberResponsesAsync(members, showMember: true, cancellationToken));
    }

    [HttpGet("memberships/mine")]
    public async Task<ActionResult<IReadOnlyList<CareCircleMemberResponse>>> GetMyMemberships(
        CancellationToken cancellationToken)
    {
        await MaterializeMembershipExpiryAsync(CurrentUserId, cancellationToken);
        var members = await _db.Set<CareCircleMember>().AsNoTracking()
            .Where(item => item.MemberUserId == CurrentUserId)
            .OrderByDescending(item => item.JoinedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Ok(await BuildMemberResponsesAsync(members, showMember: false, cancellationToken));
    }

    [HttpPut("members/{id:guid}")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<CareCircleMemberResponse>> UpdateMember(
        Guid id,
        [FromBody] UpdateCareCircleMemberRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Role) || !ValidScopes(request.Scopes))
            return BadRequest(new { error = "A valid role and at least one supported scope are required." });
        if (request.ExpiresAt <= DateTime.UtcNow.AddMinutes(5) || request.ExpiresAt > DateTime.UtcNow.AddYears(1))
            return BadRequest(new { error = "Care Circle access must expire between five minutes and one year from now." });
        var member = await _db.Set<CareCircleMember>().SingleOrDefaultAsync(item =>
            item.Id == id && item.PatientId == CurrentUserId, cancellationToken);
        if (member is null) return NotFound(new { error = "Care Circle member not found." });
        if (member.Status != CareCircleMemberStatus.Active || member.ExpiresAt <= DateTime.UtcNow)
            return Conflict(new { error = "Only an active Care Circle membership can be changed." });

        var now = DateTime.UtcNow;
        var oldConsents = await _db.Set<CareCircleConsent>().Where(item =>
            item.MemberId == member.Id && item.Status == CareCircleConsentStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var old in oldConsents)
        {
            old.Status = CareCircleConsentStatus.Revoked;
            old.RevokedAt = now;
            old.RevokedByUserId = CurrentUserId;
            old.RevocationReason = "Replaced by a new patient consent grant";
        }
        member.Role = request.Role;
        member.ExpiresAt = request.ExpiresAt;
        member.UpdatedAt = now;
        var consent = NewConsent(member, request.Scopes, CurrentUserId, now);
        _db.Set<CareCircleConsent>().Add(consent);
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "UpdateCareCircleConsent", nameof(CareCircleMember), member.Id.ToString(),
            CurrentUserId, "Patient replaced the member role, scopes, or consent expiry");
        return Ok(await BuildMemberResponseAsync(member, consent, showMember: true, cancellationToken));
    }

    [HttpPost("members/{id:guid}/revoke")]
    [Authorize(Roles = "Patient")]
    public async Task<IActionResult> RevokeMember(
        Guid id,
        [FromBody] RevokeCareCircleMemberRequest? request,
        CancellationToken cancellationToken)
    {
        var member = await _db.Set<CareCircleMember>().SingleOrDefaultAsync(item =>
            item.Id == id && item.PatientId == CurrentUserId, cancellationToken);
        if (member is null) return NotFound(new { error = "Care Circle member not found." });
        if (request?.Reason?.Length > 500) return BadRequest(new { error = "Reason is too long." });
        if (member.Status == CareCircleMemberStatus.Revoked) return NoContent();
        await RevokeMembershipAsync(member, CareCircleMemberStatus.Revoked, CurrentUserId,
            request?.Reason, cancellationToken);
        await _auditService.LogEntityActionAsync(
            "RevokeCareCircleMember", nameof(CareCircleMember), member.Id.ToString(),
            CurrentUserId, "Patient revoked a Care Circle membership and its active consent");
        return NoContent();
    }

    [HttpPost("members/{id:guid}/leave")]
    public async Task<IActionResult> LeaveMembership(Guid id, CancellationToken cancellationToken)
    {
        var member = await _db.Set<CareCircleMember>().SingleOrDefaultAsync(item =>
            item.Id == id && item.MemberUserId == CurrentUserId, cancellationToken);
        if (member is null) return NotFound(new { error = "Care Circle membership not found." });
        if (member.Status == CareCircleMemberStatus.Left) return NoContent();
        await RevokeMembershipAsync(member, CareCircleMemberStatus.Left, CurrentUserId,
            "Member left the Care Circle", cancellationToken);
        member.LeftAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _auditService.LogEntityActionAsync(
            "LeaveCareCircle", nameof(CareCircleMember), member.Id.ToString(),
            CurrentUserId, "Member left a Care Circle and revoked their access");
        return NoContent();
    }

    private async Task RevokeMembershipAsync(
        CareCircleMember member,
        CareCircleMemberStatus status,
        Guid actorId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        member.Status = status;
        member.RevokedAt = now;
        member.RevokedByUserId = actorId;
        member.UpdatedAt = now;
        var consents = await _db.Set<CareCircleConsent>().Where(item =>
            item.MemberId == member.Id && item.Status == CareCircleConsentStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var consent in consents)
        {
            consent.Status = CareCircleConsentStatus.Revoked;
            consent.RevokedAt = now;
            consent.RevokedByUserId = actorId;
            consent.RevocationReason = Clean(reason);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MaterializeExpiryAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var invitations = await _db.Set<CareCircleInvitation>().Where(item =>
            item.PatientId == patientId && item.Status == CareCircleInvitationStatus.Pending &&
            item.ExpiresAt <= now).ToListAsync(cancellationToken);
        foreach (var invitation in invitations)
        {
            invitation.Status = CareCircleInvitationStatus.Expired;
            invitation.UpdatedAt = now;
        }
        var members = await _db.Set<CareCircleMember>().Where(item =>
            item.PatientId == patientId && item.Status == CareCircleMemberStatus.Active &&
            item.ExpiresAt <= now).ToListAsync(cancellationToken);
        await ExpireMembersAsync(members, now, cancellationToken);
        if (invitations.Count == 0 && members.Count == 0) return;
        await _db.SaveChangesAsync(cancellationToken);
        foreach (var invitation in invitations)
            await _auditService.LogEntityActionAsync(
                "ExpireCareCircleInvitation", nameof(CareCircleInvitation), invitation.Id.ToString(),
                patientId, "Care Circle invitation reached its configured expiry");
        foreach (var member in members)
            await _auditService.LogEntityActionAsync(
                "ExpireCareCircleConsent", nameof(CareCircleMember), member.Id.ToString(),
                patientId, "Care Circle membership and consent reached their configured expiry");
    }

    private async Task MaterializeMembershipExpiryAsync(Guid memberUserId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var members = await _db.Set<CareCircleMember>().Where(item =>
            item.MemberUserId == memberUserId && item.Status == CareCircleMemberStatus.Active &&
            item.ExpiresAt <= now).ToListAsync(cancellationToken);
        await ExpireMembersAsync(members, now, cancellationToken);
        if (members.Count == 0) return;
        await _db.SaveChangesAsync(cancellationToken);
        foreach (var member in members)
            await _auditService.LogEntityActionAsync(
                "ExpireCareCircleConsent", nameof(CareCircleMember), member.Id.ToString(),
                member.PatientId, "Care Circle membership and consent reached their configured expiry");
    }

    private async Task ExpireMembersAsync(
        IReadOnlyList<CareCircleMember> members,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (members.Count == 0) return;
        var ids = members.Select(item => item.Id).ToArray();
        var consents = await _db.Set<CareCircleConsent>().Where(item =>
            ids.Contains(item.MemberId) && item.Status == CareCircleConsentStatus.Active)
            .ToListAsync(cancellationToken);
        foreach (var member in members)
        {
            member.Status = CareCircleMemberStatus.Expired;
            member.UpdatedAt = now;
        }
        foreach (var consent in consents)
        {
            consent.Status = CareCircleConsentStatus.Expired;
            consent.RevokedAt = now;
            consent.RevocationReason = "Consent expired";
        }
    }

    private async Task<IReadOnlyList<CareCircleMemberResponse>> BuildMemberResponsesAsync(
        IReadOnlyList<CareCircleMember> members,
        bool showMember,
        CancellationToken cancellationToken)
    {
        if (members.Count == 0) return [];
        var userIds = members.Select(item => showMember ? item.MemberUserId : item.PatientId).Distinct().ToArray();
        var users = await _db.Set<User>().AsNoTracking()
            .Where(item => userIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var memberIds = members.Select(item => item.Id).ToArray();
        var consents = await _db.Set<CareCircleConsent>().AsNoTracking()
            .Where(item => memberIds.Contains(item.MemberId) && item.Status == CareCircleConsentStatus.Active)
            .ToListAsync(cancellationToken);
        var consentByMember = consents.GroupBy(item => item.MemberId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.GrantedAt).First());
        return members.Select(member =>
        {
            var userId = showMember ? member.MemberUserId : member.PatientId;
            users.TryGetValue(userId, out var user);
            consentByMember.TryGetValue(member.Id, out var consent);
            return ToMemberResponse(member, consent, user);
        }).ToList();
    }

    private async Task<CareCircleMemberResponse> BuildMemberResponseAsync(
        CareCircleMember member,
        CareCircleConsent consent,
        bool showMember,
        CancellationToken cancellationToken)
    {
        var userId = showMember ? member.MemberUserId : member.PatientId;
        var user = await _db.Set<User>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        return ToMemberResponse(member, consent, user);
    }

    private static CareCircleMemberResponse ToMemberResponse(
        CareCircleMember member,
        CareCircleConsent? consent,
        User? user) => new(
        member.Id, member.PatientId, member.MemberUserId,
        user is null ? "PIYA user" : $"{user.FirstName} {user.LastName}".Trim(),
        user?.Email ?? string.Empty, member.Role, consent?.Scopes ?? CareCircleScope.None,
        member.Status, member.JoinedAt, member.ExpiresAt);

    private static CareCircleInvitationResponse ToResponse(CareCircleInvitation value) => new(
        value.Id, value.InviteeEmailNormalized, value.Role, value.RequestedScopes,
        value.Status, value.ExpiresAt, value.AccessExpiresAt, value.CreatedAt);

    private static CareCircleConsent NewConsent(
        CareCircleMember member,
        CareCircleScope scopes,
        Guid patientId,
        DateTime now) => new()
    {
        Id = Guid.NewGuid(), PatientId = patientId, MemberId = member.Id,
        Scopes = scopes, Status = CareCircleConsentStatus.Active,
        GrantedAt = now, ExpiresAt = member.ExpiresAt, CreatedAt = now
    };

    private static bool ValidScopes(CareCircleScope scopes) =>
        scopes != CareCircleScope.None && (scopes & ~AllScopes) == 0;

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string? NormalizeEmail(string? value)
    {
        var normalized = Clean(value)?.ToLowerInvariant();
        if (normalized is null || !normalized.Contains('@') || normalized.StartsWith('@') || normalized.EndsWith('@'))
            return null;
        return normalized;
    }

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
