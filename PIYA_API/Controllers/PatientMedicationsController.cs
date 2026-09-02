using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/patient-medications")]
[Authorize]
public sealed class PatientMedicationsController(
    PharmacyApiDbContext db,
    IPharmacyStaffService pharmacyStaffService,
    IAuditService auditService,
    INotificationService notifications,
    ILogger<PatientMedicationsController> logger) : ControllerBase
{
    private readonly PharmacyApiDbContext _db = db;
    private readonly IPharmacyStaffService _pharmacyStaffService = pharmacyStaffService;
    private readonly IAuditService _auditService = auditService;
    private readonly INotificationService _notifications = notifications;
    private readonly ILogger<PatientMedicationsController> _logger = logger;

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>Create a pharmacy refill request for one active prescription item.</summary>
    [HttpPost("refill-requests")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<PatientRefillResponse>> CreateRefillRequest(
        [FromBody] CreatePatientRefillRequest request)
    {
        if (request.PrescriptionId == Guid.Empty || request.PrescriptionItemId == Guid.Empty ||
            request.PharmacyId == Guid.Empty)
            return BadRequest(new { error = "Prescription item and pharmacy are required." });

        var now = DateTime.UtcNow;
        var prescription = await _db.Prescriptions
            .Include(item => item.Items)
                .ThenInclude(item => item.Medication)
            .SingleOrDefaultAsync(item => item.Id == request.PrescriptionId);

        if (prescription is null || prescription.PatientId != CurrentUserId)
            return NotFound(new { error = "Prescription not found." });

        if (prescription.Status is not (PrescriptionStatus.Active or PrescriptionStatus.PartiallyFulfilled) ||
            prescription.ExpiresAt <= now)
            return BadRequest(new { error = "Only an active, unexpired prescription can be refilled." });

        var prescriptionItem = prescription.Items.SingleOrDefault(item => item.Id == request.PrescriptionItemId);
        if (prescriptionItem is null)
            return BadRequest(new { error = "The selected medication is not part of this prescription." });
        if (prescriptionItem.IsFulfilled)
            return BadRequest(new { error = "This prescription item has already been fulfilled." });

        var pharmacy = await _db.Pharmacies.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.PharmacyId && item.IsActive);
        if (pharmacy is null)
            return NotFound(new { error = "Connected pharmacy not found." });

        var hasStaff = await _db.PharmacyStaff.AsNoTracking().AnyAsync(item =>
            item.PharmacyId == pharmacy.Id && item.IsActive &&
            (!item.AssignmentEndsAt.HasValue || item.AssignmentEndsAt > now));
        if (!hasStaff)
            return BadRequest(new { error = "This pharmacy is not ready to receive digital refill requests." });

        var duplicate = await _db.PatientRefillRequests.AsNoTracking().AnyAsync(item =>
            item.PrescriptionItemId == prescriptionItem.Id && item.PharmacyId == pharmacy.Id &&
            (item.Status == PatientRefillRequestStatus.Pending ||
             item.Status == PatientRefillRequestStatus.Accepted ||
             item.Status == PatientRefillRequestStatus.Ready));
        if (duplicate)
            return Conflict(new { error = "An active refill request already exists for this medication and pharmacy." });

        var refill = new PatientRefillRequest
        {
            Id = Guid.NewGuid(),
            PatientId = CurrentUserId,
            PrescriptionId = prescription.Id,
            PrescriptionItemId = prescriptionItem.Id,
            PharmacyId = pharmacy.Id,
            AutoRefill = request.AutoRefill,
            Status = PatientRefillRequestStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.PatientRefillRequests.Add(refill);
        AddStatusEvent(refill.Id, PatientRefillRequestStatus.Pending, now);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(exception, "Duplicate refill request rejected for item {ItemId}", prescriptionItem.Id);
            return Conflict(new { error = "An active refill request already exists for this medication and pharmacy." });
        }

        await _auditService.LogEntityActionAsync(
            "CreatePatientRefillRequest", nameof(PatientRefillRequest), refill.Id.ToString(),
            CurrentUserId, "Patient sent a refill request to an assigned pharmacy");

        await NotifyPharmacyStaff(refill);

        return CreatedAtAction(
            nameof(GetMyRefillRequest),
            new { id = refill.Id },
            PatientRefillResponse.From(refill, prescriptionItem.Medication.BrandName, pharmacy.Name));
    }

    /// <summary>List the signed-in patient's refill requests.</summary>
    [HttpGet("refill-requests/mine")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<IReadOnlyList<PatientRefillResponse>>> GetMyRefillRequests()
    {
        var requests = await RefillQuery()
            .Where(item => item.PatientId == CurrentUserId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(100)
            .ToListAsync();
        return Ok(requests.Select(item => PatientRefillResponse.From(item)));
    }

    /// <summary>Get one refill request owned by the signed-in patient.</summary>
    [HttpGet("refill-requests/mine/{id:guid}")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<PatientRefillResponse>> GetMyRefillRequest(Guid id)
    {
        var request = await RefillQuery()
            .SingleOrDefaultAsync(item => item.Id == id && item.PatientId == CurrentUserId);
        return request is null
            ? NotFound(new { error = "Refill request not found." })
            : Ok(PatientRefillResponse.From(request));
    }

    /// <summary>Cancel a pending or accepted request owned by the patient.</summary>
    [HttpPost("refill-requests/{id:guid}/cancel")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<PatientRefillResponse>> CancelRefillRequest(Guid id)
    {
        var request = await _db.PatientRefillRequests
            .Include(item => item.PrescriptionItem).ThenInclude(item => item.Medication)
            .Include(item => item.Pharmacy)
            .SingleOrDefaultAsync(item => item.Id == id && item.PatientId == CurrentUserId);
        if (request is null) return NotFound(new { error = "Refill request not found." });
        if (request.Status is not (PatientRefillRequestStatus.Pending or PatientRefillRequestStatus.Accepted))
            return Conflict(new { error = "This refill request can no longer be cancelled." });

        request.Status = PatientRefillRequestStatus.Cancelled;
        request.UpdatedAt = DateTime.UtcNow;
        AddStatusEvent(request.Id, PatientRefillRequestStatus.Cancelled, request.UpdatedAt);
        await _db.SaveChangesAsync();
        await _auditService.LogEntityActionAsync(
            "CancelPatientRefillRequest", nameof(PatientRefillRequest), request.Id.ToString(),
            CurrentUserId, "Patient cancelled their refill request");
        return Ok(PatientRefillResponse.From(request));
    }

    /// <summary>List requests for a pharmacy assigned to the current professional.</summary>
    [HttpGet("refill-requests/pharmacy/{pharmacyId:guid}")]
    [Authorize(Roles = "Pharmacist,PharmacyManager,Admin,SuperAdmin")]
    public async Task<ActionResult<IReadOnlyList<PatientRefillResponse>>> GetPharmacyRefillRequests(
        Guid pharmacyId,
        [FromQuery] PatientRefillRequestStatus? status = null)
    {
        if (!await CanAccessPharmacy(pharmacyId)) return Forbid();
        var query = RefillQuery().Where(item => item.PharmacyId == pharmacyId);
        if (status.HasValue) query = query.Where(item => item.Status == status.Value);
        var requests = await query.OrderBy(item => item.CreatedAt).Take(200).ToListAsync();
        return Ok(requests.Select(item => PatientRefillResponse.From(item)));
    }

    /// <summary>Advance a pharmacy request through its controlled lifecycle.</summary>
    [HttpPost("refill-requests/{id:guid}/status")]
    [Authorize(Roles = "Pharmacist,PharmacyManager,Admin,SuperAdmin")]
    public async Task<ActionResult<PatientRefillResponse>> UpdateRefillRequestStatus(
        Guid id,
        [FromBody] UpdatePatientRefillStatusRequest update)
    {
        if (update.Note?.Length > 1000)
            return BadRequest(new { error = "Note cannot exceed 1000 characters." });

        var request = await _db.PatientRefillRequests
            .Include(item => item.PrescriptionItem).ThenInclude(item => item.Medication)
            .Include(item => item.Pharmacy)
            .SingleOrDefaultAsync(item => item.Id == id);
        if (request is null) return NotFound(new { error = "Refill request not found." });
        if (!await CanAccessPharmacy(request.PharmacyId)) return Forbid();
        if (!CanTransition(request.Status, update.Status))
            return Conflict(new { error = $"Cannot change a {request.Status} request to {update.Status}." });
        if (update.EstimatedReadyAt.HasValue && update.EstimatedReadyAt.Value <= DateTime.UtcNow)
            return BadRequest(new { error = "Estimated ready time must be in the future." });

        request.Status = update.Status;
        request.EstimatedReadyAt = update.EstimatedReadyAt;
        request.Note = string.IsNullOrWhiteSpace(update.Note) ? null : update.Note.Trim();
        request.ReviewedByUserId = CurrentUserId;
        request.ReviewedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        AddStatusEvent(
            request.Id, update.Status, request.UpdatedAt,
            string.IsNullOrWhiteSpace(update.Note) ? null : update.Note.Trim(),
            update.EstimatedReadyAt);
        await _db.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "UpdatePatientRefillRequestStatus", nameof(PatientRefillRequest), request.Id.ToString(),
            CurrentUserId, $"Pharmacy changed refill request status to {request.Status}");

        await NotifyPatient(request);
        return Ok(PatientRefillResponse.From(request));
    }

    private IQueryable<PatientRefillRequest> RefillQuery() => _db.PatientRefillRequests.AsNoTracking()
        .Include(item => item.Patient)
        .Include(item => item.PrescriptionItem).ThenInclude(item => item.Medication)
        .Include(item => item.Pharmacy);

    private async Task<bool> CanAccessPharmacy(Guid pharmacyId) =>
        User.IsInRole("Admin") || User.IsInRole("SuperAdmin") ||
        await _pharmacyStaffService.IsStaffAtPharmacyAsync(pharmacyId, CurrentUserId);

    private static bool CanTransition(PatientRefillRequestStatus current, PatientRefillRequestStatus next) =>
        (current, next) switch
        {
            (PatientRefillRequestStatus.Pending, PatientRefillRequestStatus.Accepted) => true,
            (PatientRefillRequestStatus.Pending, PatientRefillRequestStatus.Declined) => true,
            (PatientRefillRequestStatus.Accepted, PatientRefillRequestStatus.Ready) => true,
            (PatientRefillRequestStatus.Accepted, PatientRefillRequestStatus.Declined) => true,
            (PatientRefillRequestStatus.Ready, PatientRefillRequestStatus.Collected) => true,
            _ => false
        };

    private void AddStatusEvent(
        Guid refillRequestId,
        PatientRefillRequestStatus status,
        DateTime occurredAt,
        string? note = null,
        DateTime? estimatedReadyAt = null)
    {
        // The patient-health model is integrated by the central DbContext migration.
        // Keeping this guard makes older test/upgrade contexts forward compatible.
        if (_db.Model.FindEntityType(typeof(PatientRefillStatusEvent)) is null) return;
        _db.Set<PatientRefillStatusEvent>().Add(new PatientRefillStatusEvent
        {
            Id = Guid.NewGuid(),
            RefillRequestId = refillRequestId,
            Status = status,
            ActorUserId = CurrentUserId,
            Note = note,
            EstimatedReadyAt = estimatedReadyAt,
            OccurredAt = occurredAt
        });
    }

    private async Task NotifyPharmacyStaff(PatientRefillRequest request)
    {
        try
        {
            var staff = await _pharmacyStaffService.GetPharmacyStaffAsync(request.PharmacyId, activeOnly: true);
            foreach (var member in staff.Where(item =>
                         item.IsActive && (!item.AssignmentEndsAt.HasValue || item.AssignmentEndsAt > DateTime.UtcNow)))
            {
                await _notifications.SendPushNotificationAsync(
                    member.UserId,
                    "New refill request",
                    "A patient refill request needs review.",
                    new Dictionary<string, string>
                    {
                        ["type"] = "patient_refill_request",
                        ["requestId"] = request.Id.ToString(),
                        ["pharmacyId"] = request.PharmacyId.ToString()
                    });
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Refill request {RequestId} was saved but staff notification failed", request.Id);
        }
    }

    private async Task NotifyPatient(PatientRefillRequest request)
    {
        try
        {
            await _notifications.SendPushNotificationAsync(
                request.PatientId,
                "Refill request updated",
                $"Your pharmacy request is now {request.Status.ToString().ToLowerInvariant()}.",
                new Dictionary<string, string>
                {
                    ["type"] = "patient_refill_request",
                    ["requestId"] = request.Id.ToString(),
                    ["status"] = request.Status.ToString()
                });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Status for refill request {RequestId} was saved but patient notification failed", request.Id);
        }
    }
}

public sealed record CreatePatientRefillRequest(
    Guid PrescriptionId,
    Guid PrescriptionItemId,
    Guid PharmacyId,
    bool AutoRefill = false);

public sealed record UpdatePatientRefillStatusRequest(
    PatientRefillRequestStatus Status,
    DateTime? EstimatedReadyAt = null,
    string? Note = null);

public sealed record PatientRefillResponse(
    Guid Id,
    Guid PrescriptionId,
    Guid PrescriptionItemId,
    Guid PharmacyId,
    string? MedicationName,
    string? PharmacyName,
    PatientRefillRequestStatus Status,
    bool AutoRefill,
    DateTime? EstimatedReadyAt,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static PatientRefillResponse From(
        PatientRefillRequest request,
        string? medicationName = null,
        string? pharmacyName = null) => new(
            request.Id,
            request.PrescriptionId,
            request.PrescriptionItemId,
            request.PharmacyId,
            medicationName ?? request.PrescriptionItem?.Medication?.BrandName,
            pharmacyName ?? request.Pharmacy?.Name,
            request.Status,
            request.AutoRefill,
            request.EstimatedReadyAt,
            request.CreatedAt,
            request.UpdatedAt);
}
