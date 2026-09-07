using System.Security.Claims;
using System.Globalization;
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
[Route("api/patient-verification")]
[Authorize(Roles = "Patient")]
public sealed class PatientVerificationController(
    PharmacyApiDbContext db,
    IVerificationProviderGateway provider,
    IAuditService auditService,
    ILogger<PatientVerificationController> logger) : ControllerBase
{
    private readonly PharmacyApiDbContext _db = db;
    private readonly IVerificationProviderGateway _provider = provider;
    private readonly IAuditService _auditService = auditService;
    private readonly ILogger<PatientVerificationController> _logger = logger;

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("capabilities")]
    public ActionResult<IReadOnlyList<VerificationCapabilityResponse>> GetCapabilities() => Ok(
        Enum.GetValues<PatientVerificationKind>().Select(kind =>
        {
            var capability = _provider.GetCapability(kind);
            return new VerificationCapabilityResponse(
                kind, capability.IsConnected, capability.ProviderName, capability.Message);
        }).ToList());

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PatientVerificationResponse>>> GetMyVerifications(
        CancellationToken cancellationToken)
    {
        var records = await _db.Set<PatientVerification>().AsNoTracking()
            .Where(item => item.PatientId == CurrentUserId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        return Ok(records.Select(PatientVerificationResponse.From));
    }

    [HttpGet("{kind}")]
    public async Task<ActionResult<PatientVerificationResponse>> GetLatest(
        PatientVerificationKind kind,
        CancellationToken cancellationToken)
    {
        var record = await _db.Set<PatientVerification>().AsNoTracking()
            .Where(item => item.PatientId == CurrentUserId && item.Kind == kind)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return record is null
            ? NotFound(new { error = "No verification attempt exists for this type." })
            : Ok(PatientVerificationResponse.From(record));
    }

    [HttpPost("{kind}/start")]
    public async Task<ActionResult<PatientVerificationResponse>> Start(
        PatientVerificationKind kind,
        [FromBody] StartPatientVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(kind, request);
        if (validationError is not null) return BadRequest(new { error = validationError });
        if (kind == PatientVerificationKind.Identity && _provider.GetCapability(kind) is { IsConnected: true, ProviderName: "Veriff" })
        {
            var patient = await _db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == CurrentUserId, cancellationToken);
            if (patient?.DateOfBirth is null)
                return BadRequest(new { error = "Add your legal name and date of birth in Personal Information before verifying your identity." });
        }

        var now = DateTime.UtcNow;
        var latest = await _db.Set<PatientVerification>()
            .Where(item => item.PatientId == CurrentUserId && item.Kind == kind)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest is not null &&
            latest.Status is PatientVerificationStatus.Pending or
                PatientVerificationStatus.RequiresAction or
                PatientVerificationStatus.Verified &&
            (!latest.ExpiresAt.HasValue || latest.ExpiresAt > now))
            return Conflict(new { error = "An active verification already exists for this type." });

        VerificationProviderResult providerResult;
        try
        {
            providerResult = await _provider.StartAsync(new VerificationProviderStartContext(
                CurrentUserId, kind, Clean(request.CountryCode)?.ToUpperInvariant(),
                Clean(request.DocumentType), Clean(request.InsuranceIssuer),
                Clean(request.PolicyReferenceLastFour)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "Verification provider failed to start {Kind} verification for patient {PatientId}",
                kind, CurrentUserId);
            providerResult = new VerificationProviderResult(
                PatientVerificationStatus.NotConnected,
                StatusReasonCode: "provider_unavailable");
        }

        var record = latest is not null && latest.Status == PatientVerificationStatus.NotConnected
            ? latest
            : new PatientVerification { Id = Guid.NewGuid(), PatientId = CurrentUserId, Kind = kind };
        if (record.Id == Guid.Empty) record.Id = Guid.NewGuid();
        providerResult = await MatchVerifiedIdentity(providerResult, cancellationToken);
        ApplyProviderResult(record, providerResult, now);
        record.CountryCode = Clean(request.CountryCode)?.ToUpperInvariant();
        record.DocumentType = kind == PatientVerificationKind.Identity ? Clean(request.DocumentType) : null;
        record.InsuranceIssuer = kind == PatientVerificationKind.Insurance ? Clean(request.InsuranceIssuer) : null;
        record.PolicyReferenceLastFour = kind == PatientVerificationKind.Insurance
            ? Clean(request.PolicyReferenceLastFour)
            : null;
        record.CreatedAt = record.CreatedAt == default ? now : record.CreatedAt;
        record.UpdatedAt = now;
        if (_db.Entry(record).State == EntityState.Detached) _db.Set<PatientVerification>().Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        await _auditService.LogEntityActionAsync(
            "StartPatientVerification", nameof(PatientVerification), record.Id.ToString(),
            CurrentUserId, $"Patient requested {kind} verification; provider status is {record.Status}");
        return CreatedAtAction(nameof(GetLatest), new { kind }, PatientVerificationResponse.From(record));
    }

    [HttpPost("{id:guid}/refresh")]
    public async Task<ActionResult<PatientVerificationResponse>> Refresh(
        Guid id,
        CancellationToken cancellationToken)
    {
        var record = await _db.Set<PatientVerification>()
            .SingleOrDefaultAsync(item => item.Id == id && item.PatientId == CurrentUserId, cancellationToken);
        if (record is null) return NotFound(new { error = "Verification not found." });
        if (record.Status is PatientVerificationStatus.Cancelled or PatientVerificationStatus.Rejected)
            return Conflict(new { error = "This verification is no longer active." });
        if (record.Status == PatientVerificationStatus.Verified &&
            (!record.ExpiresAt.HasValue || record.ExpiresAt > DateTime.UtcNow))
            return Ok(PatientVerificationResponse.From(record));

        VerificationProviderResult result;
        if (string.IsNullOrWhiteSpace(record.ProviderReference))
        {
            result = new VerificationProviderResult(
                PatientVerificationStatus.NotConnected,
                StatusReasonCode: "provider_not_connected");
        }
        else
        {
            try
            {
                result = await _provider.RefreshAsync(
                    record.Kind, record.ProviderReference, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Verification provider refresh failed for case {CaseId}", id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { error = "The verification provider is temporarily unavailable." });
            }
        }

        // The patient may cancel while the provider request is in flight.
        await _db.Entry(record).ReloadAsync(cancellationToken);
        if (record.Status is PatientVerificationStatus.Cancelled or PatientVerificationStatus.Rejected || record.StatusReasonCode == "profile_changed")
            return Conflict(new { error = "This verification is no longer active." });
        result = await MatchVerifiedIdentity(result, cancellationToken);
        ApplyProviderResult(record, result, DateTime.UtcNow);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "Verification changed. Reload its status before continuing." }); }
        await _auditService.LogEntityActionAsync(
            "RefreshPatientVerification", nameof(PatientVerification), record.Id.ToString(),
            CurrentUserId, $"Patient refreshed verification; provider status is {record.Status}");
        return Ok(PatientVerificationResponse.From(record));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var record = await _db.Set<PatientVerification>()
            .SingleOrDefaultAsync(item => item.Id == id && item.PatientId == CurrentUserId, cancellationToken);
        if (record is null) return NotFound(new { error = "Verification not found." });
        if (record.Status == PatientVerificationStatus.Verified)
            return Conflict(new { error = "A completed verification cannot be cancelled." });
        if (record.Status == PatientVerificationStatus.Cancelled) return NoContent();

        record.Status = PatientVerificationStatus.Cancelled;
        record.CancelledAt = DateTime.UtcNow;
        record.ActionUrl = null;
        record.UpdatedAt = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "Verification changed. Reload its status before continuing." }); }
        await _auditService.LogEntityActionAsync(
            "CancelPatientVerification", nameof(PatientVerification), record.Id.ToString(),
            CurrentUserId, "Patient cancelled a verification attempt");
        return NoContent();
    }

    private static string? Validate(
        PatientVerificationKind kind,
        StartPatientVerificationRequest request)
    {
        if (!request.PrivacyNoticeAccepted)
            return "Accept the verification privacy notice before continuing.";
        if (!Enum.IsDefined(kind)) return "Unsupported verification type.";
        if (request.CountryCode?.Length > 3) return "Country code is invalid.";
        if (kind == PatientVerificationKind.Identity &&
            (string.IsNullOrWhiteSpace(request.DocumentType) || request.DocumentType.Length > 64))
            return "A valid document type is required.";
        if (kind == PatientVerificationKind.Insurance)
        {
            if (string.IsNullOrWhiteSpace(request.InsuranceIssuer) || request.InsuranceIssuer.Length > 160)
                return "A valid insurance issuer is required.";
            var suffix = request.PolicyReferenceLastFour?.Trim();
            if (suffix is not { Length: 4 } || !suffix.All(char.IsLetterOrDigit))
                return "Only the final four characters of the policy reference are required.";
        }
        return null;
    }

    private static void ApplyProviderResult(
        PatientVerification record,
        VerificationProviderResult result,
        DateTime now)
    {
        var providerName = CleanMax(result.ProviderName, 120);
        var providerReference = CleanMax(result.ProviderReference, 300);
        var claimsProviderWork = result.Status is PatientVerificationStatus.Pending or
            PatientVerificationStatus.RequiresAction or PatientVerificationStatus.Verified;
        var validProviderResult = !claimsProviderWork ||
                                  (providerName is not null && providerReference is not null);
        record.Status = validProviderResult
            ? result.Status
            : PatientVerificationStatus.NotConnected;
        record.ProviderName = validProviderResult ? providerName : null;
        record.ProviderReference = validProviderResult ? providerReference : null;
        record.ActionUrl = record.Status is PatientVerificationStatus.Pending or PatientVerificationStatus.RequiresAction
            ? (validProviderResult && IsSafeActionUrl(result.ActionUrl) && result.ActionUrl!.Length <= 2000
                ? result.ActionUrl : result.ActionUrl is null && validProviderResult ? record.ActionUrl : null)
            : null;
        record.StatusReasonCode = validProviderResult
            ? CleanMax(result.StatusReasonCode, 120)
            : "invalid_provider_response";
        record.ExpiresAt = validProviderResult ? result.ExpiresAt : null;
        record.SubmittedAt = record.Status is PatientVerificationStatus.Pending or
            PatientVerificationStatus.RequiresAction or PatientVerificationStatus.Verified
            ? record.SubmittedAt ?? now
            : record.SubmittedAt;
        record.VerifiedAt = record.Status == PatientVerificationStatus.Verified ? now : null;
        record.UpdatedAt = now;
    }

    private static bool IsSafeActionUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private async Task<VerificationProviderResult> MatchVerifiedIdentity(VerificationProviderResult result, CancellationToken cancellationToken)
    {
        if (result.ProviderName != "Veriff" || result.Status != PatientVerificationStatus.Verified) return result;
        var patient = await _db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == CurrentUserId, cancellationToken);
        var identity = result.Identity;
        if (patient?.DateOfBirth is null || identity is null || identity.SubjectId != CurrentUserId ||
            NormalizeName(identity.FirstName) != NormalizeName(patient.FirstName) ||
            NormalizeName(identity.LastName) != NormalizeName(patient.LastName) ||
            identity.DateOfBirth != DateOnly.FromDateTime(patient.DateOfBirth.Value))
            return result with { Status = PatientVerificationStatus.Rejected, StatusReasonCode = "identity_profile_mismatch", Identity = null };
        return result with { Identity = null };
    }

    private static string NormalizeName(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character)))
        .ToUpperInvariant();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? CleanMax(string? value, int maxLength)
    {
        var clean = Clean(value);
        return clean is null || clean.Length <= maxLength ? clean : clean[..maxLength];
    }
}
