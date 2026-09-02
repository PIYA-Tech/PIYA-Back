using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public sealed class PatientPickupService(
    PharmacyApiDbContext db,
    IPharmacyStaffService pharmacyStaff,
    IPatientNotificationInboxService inbox,
    INotificationService notifications,
    IAuditService audit,
    ILogger<PatientPickupService> logger) : IPatientPickupService
{
    private readonly PharmacyApiDbContext _db = db;
    private readonly IPharmacyStaffService _pharmacyStaff = pharmacyStaff;
    private readonly IPatientNotificationInboxService _inbox = inbox;
    private readonly INotificationService _notifications = notifications;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<PatientPickupService> _logger = logger;

    public async Task<IReadOnlyList<PharmacyPickupResponse>> GetPatientHistoryAsync(Guid patientId)
    {
        var items = await PickupQuery().Where(item => item.PatientId == patientId)
            .OrderByDescending(item => item.CollectedAt).Take(200).ToListAsync();
        return items.Select(PharmacyPickupResponse.From).ToList();
    }

    public async Task<IReadOnlyList<PharmacyPickupResponse>> GetPharmacyHistoryAsync(
        Guid pharmacyId, Guid actorId, bool isAdmin)
    {
        if (!isAdmin && !await _pharmacyStaff.IsStaffAtPharmacyAsync(pharmacyId, actorId))
            throw new UnauthorizedAccessException("Not assigned to this pharmacy.");
        var items = await PickupQuery().Where(item => item.PharmacyId == pharmacyId)
            .OrderByDescending(item => item.CollectedAt).Take(500).ToListAsync();
        return items.Select(PharmacyPickupResponse.From).ToList();
    }

    public async Task<PharmacyPickupResponse> CollectAsync(
        Guid refillRequestId, Guid actorId, bool isAdmin, decimal quantityCollected)
    {
        if (quantityCollected <= 0 || quantityCollected > 1_000_000)
            throw new ArgumentException("Collected quantity must be positive.");

        var refill = await _db.Set<PatientRefillRequest>()
            .Include(item => item.Pharmacy)
            .Include(item => item.PrescriptionItem).ThenInclude(item => item.Medication)
            .Include(item => item.Prescription).ThenInclude(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == refillRequestId)
            ?? throw new PatientHealthNotFoundException("Refill request not found.");
        if (!isAdmin && !await _pharmacyStaff.IsStaffAtPharmacyAsync(refill.PharmacyId, actorId))
            throw new UnauthorizedAccessException("Not assigned to this pharmacy.");

        var existing = await PickupQuery()
            .SingleOrDefaultAsync(item => item.RefillRequestId == refillRequestId);
        if (existing is not null) return PharmacyPickupResponse.From(existing);
        if (refill.Status != PatientRefillRequestStatus.Ready)
            throw new PatientHealthConflictException("Only a ready refill can be collected.");
        if (refill.PrescriptionItem.IsFulfilled)
            throw new PatientHealthConflictException("This prescription item has already been fulfilled.");

        var now = DateTime.UtcNow;
        var pickup = new PharmacyPickup
        {
            Id = Guid.NewGuid(),
            RefillRequestId = refill.Id,
            PatientId = refill.PatientId,
            PharmacyId = refill.PharmacyId,
            PrescriptionItemId = refill.PrescriptionItemId,
            CollectedByUserId = actorId,
            QuantityCollected = quantityCollected,
            Status = PharmacyPickupStatus.Collected,
            CollectedAt = now,
            CreatedAt = now
        };
        _db.Set<PharmacyPickup>().Add(pickup);
        _db.Set<PatientRefillStatusEvent>().Add(new PatientRefillStatusEvent
        {
            Id = Guid.NewGuid(),
            RefillRequestId = refill.Id,
            Status = PatientRefillRequestStatus.Collected,
            ActorUserId = actorId,
            OccurredAt = now
        });

        refill.Status = PatientRefillRequestStatus.Collected;
        refill.ReviewedByUserId = actorId;
        refill.ReviewedAt = now;
        refill.UpdatedAt = now;
        refill.PrescriptionItem.IsFulfilled = true;
        refill.PrescriptionItem.FulfilledAt = now;
        refill.Prescription.UpdatedAt = now;
        refill.Prescription.FulfilledByPharmacyId = refill.PharmacyId;
        if (refill.Prescription.Items.All(item => item.IsFulfilled))
        {
            refill.Prescription.Status = PrescriptionStatus.Fulfilled;
            refill.Prescription.FulfilledAt = now;
        }
        else
        {
            refill.Prescription.Status = PrescriptionStatus.PartiallyFulfilled;
        }

        var tracked = await _db.Set<PatientMedication>()
            .SingleOrDefaultAsync(item => item.PatientId == refill.PatientId &&
                                          item.PrescriptionItemId == refill.PrescriptionItemId);
        if (tracked is not null)
        {
            tracked.SupplyTotal += quantityCollected;
            tracked.SupplyRemaining += quantityCollected;
            tracked.LastRefilledAt = now;
            tracked.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();
        await _audit.LogEntityActionAsync(
            "CollectPatientMedication", nameof(PharmacyPickup), pickup.Id.ToString(), actorId,
            "Connected pharmacy recorded a patient medication pickup");

        await _inbox.EnqueueAsync(
            refill.PatientId,
            PatientNotificationCategory.Pickup,
            "Medication collected",
            $"Your pickup from {refill.Pharmacy.Name} has been added to your history.",
            $"piya://medications/pickups/{pickup.Id}",
            new Dictionary<string, string>
            {
                ["type"] = "medication_pickup",
                ["pickupId"] = pickup.Id.ToString()
            },
            $"pickup:{pickup.Id}");
        try
        {
            await _notifications.SendPushNotificationAsync(
                refill.PatientId,
                "Pickup recorded",
                "Your pharmacy pickup has been added to PIYA.",
                new Dictionary<string, string>
                {
                    ["type"] = "medication_pickup",
                    ["pickupId"] = pickup.Id.ToString()
                });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Pickup {PickupId} saved but push delivery failed", pickup.Id);
        }

        // Populate navigation properties for the response without another round trip.
        pickup.Pharmacy = refill.Pharmacy;
        pickup.PrescriptionItem = refill.PrescriptionItem;
        return PharmacyPickupResponse.From(pickup);
    }

    private IQueryable<PharmacyPickup> PickupQuery() => _db.Set<PharmacyPickup>().AsNoTracking()
        .Include(item => item.Pharmacy)
        .Include(item => item.PrescriptionItem).ThenInclude(item => item.Medication);
}
