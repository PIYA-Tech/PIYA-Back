using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class PrescriptionService(
    PharmacyApiDbContext context,
    IAuditService auditService,
    IQRService qrService,
    IOptions<SecurityOptions> securityOptions,
    IHostEnvironment environment,
    IInventoryService inventoryService,
    ILogger<PrescriptionService> logger) : IPrescriptionService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly IQRService _qrService = qrService;
    private readonly string _prescriptionSigningKey =
        ResolvePrescriptionSigningKey(securityOptions.Value, environment);
    private readonly IInventoryService _inventoryService = inventoryService;
    private readonly ILogger<PrescriptionService> _logger = logger;

    public async Task<Prescription> CreatePrescriptionAsync(Prescription prescription)
    {
        // Only assign a new Id if one wasn't already set by the caller
        if (prescription.Id == Guid.Empty)
            prescription.Id = Guid.NewGuid();
        prescription.IssuedAt = DateTime.UtcNow;
        prescription.Status = PrescriptionStatus.Active;
        prescription.CreatedAt = DateTime.UtcNow;
        prescription.UpdatedAt = DateTime.UtcNow;

        // Generate digital signature
        prescription.DigitalSignature = GenerateDigitalSignature(prescription);

        _context.Prescriptions.Add(prescription);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "CreatePrescription",
            "Prescription",
            prescription.Id.ToString(),
            prescription.DoctorId,
            $"Prescription created for patient {prescription.PatientId}"
        );

        return prescription;
    }

    public async Task<Prescription?> GetByIdAsync(Guid id)
    {
        return await _context.Prescriptions
            .Include(p => p.Patient)
            .Include(p => p.Doctor)
            .Include(p => p.Items)
                .ThenInclude(i => i.Medication)
            .Include(p => p.FulfilledByPharmacy)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<Prescription>> GetPatientPrescriptionsAsync(Guid patientId, PrescriptionStatus? status = null, CancellationToken ct = default)
    {
        var query = _context.Prescriptions
            .Include(p => p.Doctor)
            .Include(p => p.Items)
                .ThenInclude(i => i.Medication)
            .Where(p => p.PatientId == patientId);

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        return await query
            .OrderByDescending(p => p.IssuedAt)
            .ToListAsync(ct);
    }

    public async Task<List<Prescription>> GetDoctorPrescriptionsAsync(Guid doctorId, PrescriptionStatus? status = null, CancellationToken ct = default)
    {
        var query = _context.Prescriptions
            .Include(p => p.Patient)
            .Include(p => p.Items)
                .ThenInclude(i => i.Medication)
            .Where(p => p.DoctorId == doctorId);

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        return await query
            .OrderByDescending(p => p.IssuedAt)
            .ToListAsync(ct);
    }

    public async Task<string> GenerateQrCodeAsync(Guid prescriptionId)
    {
        var prescription = await GetByIdAsync(prescriptionId) ?? throw new InvalidOperationException("Prescription not found");
        if (prescription.Status != PrescriptionStatus.Active)
        {
            throw new InvalidOperationException("Cannot generate QR code for inactive prescription");
        }

        // Generate QR token with 5-minute validity
        var (qrToken, tokenId) = await _qrService.GeneratePrescriptionQrTokenAsync(prescriptionId, prescription.PatientId);

        prescription.QrToken = qrToken;
        prescription.QrTokenExpiresAt = DateTime.UtcNow.AddMinutes(5);
        prescription.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "GeneratePrescriptionQR",
            "Prescription",
            prescriptionId.ToString(),
            prescription.PatientId,
            "QR code generated for prescription"
        );

        return qrToken;
    }

    public async Task<Prescription?> ValidateQrCodeAsync(string qrToken)
    {
        var (isValid, entityId, entityType, expiresAt, errorMessage) = await _qrService.ValidateQrTokenAsync(qrToken);

        if (!isValid || entityType != "Prescription")
        {
            _logger.LogWarning("Invalid QR token: {ErrorMessage}", errorMessage);
            return null;
        }

        var prescription = await GetByIdAsync(entityId);
        return prescription;
    }

    public async Task<Prescription> FulfillPrescriptionAsync(Guid prescriptionId, Guid pharmacyId)
    {
        if (_context.Database.CurrentTransaction != null)
            return await FulfillPrescriptionCoreAsync(prescriptionId, pharmacyId);

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            try
            {
                var rx = await FulfillPrescriptionCoreAsync(prescriptionId, pharmacyId);
                await tx.CommitAsync();
                return rx;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
    }

    public async Task<Prescription> FulfillPrescriptionByQrAsync(
        string qrToken,
        Guid pharmacistUserId,
        Guid pharmacyId,
        string? ipAddress = null,
        string? userAgent = null)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            try
            {
                // Resolve and authorize the prescription before consuming the token.
                // Serializable isolation plus the second validation during consumption
                // prevents two scanners from successfully using the same QR.
                var (isValid, prescriptionId, entityType, _, validationError) =
                    await _qrService.ValidateQrTokenAsync(qrToken);
                if (!isValid)
                    throw new InvalidOperationException(validationError);
                if (!string.Equals(entityType, "Prescription", StringComparison.Ordinal))
                    throw new InvalidOperationException("QR code is not for a prescription.");

                var prescription = await GetByIdAsync(prescriptionId)
                    ?? throw new InvalidOperationException("Prescription not found");

                if (prescription.FulfilledByPharmacyId.HasValue &&
                    prescription.FulfilledByPharmacyId.Value != pharmacyId)
                {
                    throw new UnauthorizedAccessException(
                        "Prescription is assigned to a different pharmacy.");
                }

                if (prescription.Status is not PrescriptionStatus.Active
                    and not PrescriptionStatus.PartiallyFulfilled
                    and not PrescriptionStatus.Fulfilled)
                {
                    throw new InvalidOperationException(
                        $"Cannot dispense a prescription with status '{prescription.Status}'.");
                }

                var (consumed, consumedPrescriptionId, consumeError) =
                    await _qrService.ValidateAndUsePrescriptionQrTokenAsync(
                        qrToken,
                        pharmacistUserId,
                        ipAddress,
                        userAgent);
                if (!consumed || consumedPrescriptionId != prescriptionId)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(consumeError)
                            ? "QR code could not be consumed."
                            : consumeError);
                }

                // A second QR may have been generated before a successful dispense whose
                // response was lost. Consume that token without deducting stock again.
                if (prescription.Status == PrescriptionStatus.Fulfilled)
                {
                    await tx.CommitAsync();
                    return prescription;
                }

                prescription = await FulfillPrescriptionCoreAsync(prescriptionId, pharmacyId);
                await tx.CommitAsync();
                return prescription;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
    }

    private async Task<Prescription> FulfillPrescriptionCoreAsync(
        Guid prescriptionId,
        Guid pharmacyId)
    {
        var rx = await GetByIdAsync(prescriptionId)
            ?? throw new InvalidOperationException("Prescription not found");

        if (rx.Status != PrescriptionStatus.Active &&
            rx.Status != PrescriptionStatus.PartiallyFulfilled)
        {
            throw new InvalidOperationException(
                $"Cannot fulfill a prescription with status '{rx.Status}'");
        }

        var unfulfilledItems = rx.Items.Where(i => !i.IsFulfilled).ToList();
        if (unfulfilledItems.Count == 0)
            throw new InvalidOperationException("Prescription has no unfulfilled items.");

        var stockErrors = new List<string>();
        foreach (var item in unfulfilledItems)
        {
            var available = await _inventoryService.GetAvailableStockAsync(
                pharmacyId, item.MedicationId);
            if (available < item.Quantity)
            {
                stockErrors.Add(
                    $"Medication {item.MedicationId}: required {item.Quantity}, available {available}");
            }
        }

        if (stockErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Insufficient stock to fulfill prescription. " + string.Join("; ", stockErrors));
        }

        rx.Status = PrescriptionStatus.Fulfilled;
        rx.FulfilledAt = DateTime.UtcNow;
        rx.FulfilledByPharmacyId = pharmacyId;
        rx.UpdatedAt = DateTime.UtcNow;

        foreach (var item in unfulfilledItems)
        {
            item.IsFulfilled = true;
            item.FulfilledAt = DateTime.UtcNow;
        }

        var referenceNumber = $"RX-{prescriptionId.ToString()[..8]}";
        foreach (var item in unfulfilledItems)
        {
            await _inventoryService.DecreaseStockAsync(
                pharmacyId,
                item.MedicationId,
                item.Quantity,
                rx.PatientId,
                prescriptionId,
                referenceNumber: referenceNumber);
        }

        if (!string.IsNullOrEmpty(rx.QrToken))
        {
            await _qrService.RevokeTokenAsync(
                rx.QrToken, rx.PatientId, "Prescription fulfilled");
        }

        await _context.SaveChangesAsync();
        await _auditService.LogEntityActionAsync(
            "FulfillPrescription",
            "Prescription",
            prescriptionId.ToString(),
            null,
            $"Prescription fulfilled by pharmacy {pharmacyId}");

        return rx;
    }

    public async Task<PrescriptionItem?> GetPrescriptionItemAsync(Guid itemId)
        => await _context.PrescriptionItems
            .Include(i => i.Prescription)
            .FirstOrDefaultAsync(i => i.Id == itemId);

    public async Task<PrescriptionItem> FulfillPrescriptionItemAsync(Guid itemId)
    {
        if (_context.Database.CurrentTransaction != null)
            return await FulfillPrescriptionItemCoreAsync(itemId);

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            try
            {
                var fulfilledItem = await FulfillPrescriptionItemCoreAsync(itemId);
                await tx.CommitAsync();
                return fulfilledItem;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
    }

    private async Task<PrescriptionItem> FulfillPrescriptionItemCoreAsync(Guid itemId)
    {
        var item = await _context.PrescriptionItems
            .Include(i => i.Prescription)
            .FirstOrDefaultAsync(i => i.Id == itemId) ?? throw new InvalidOperationException("Prescription item not found");

        if (item.IsFulfilled)
            throw new InvalidOperationException("Prescription item is already fulfilled");

        var prescription = item.Prescription;

        if (prescription.Status != PrescriptionStatus.Active && prescription.Status != PrescriptionStatus.PartiallyFulfilled)
            throw new InvalidOperationException($"Cannot fulfill an item on a prescription with status '{prescription.Status}'");

        // Require the caller to supply the dispensing pharmacy via FulfilledByPharmacyId
        // (already set on the prescription when the first item was fulfilled, or passed explicitly).
        // If the prescription has never been partially fulfilled, pharmacyId is unknown here,
        // so we check that it is already recorded on the prescription entity.
        if (!prescription.FulfilledByPharmacyId.HasValue)
            throw new InvalidOperationException(
                "Cannot fulfill an individual item: no dispensing pharmacy is recorded on the prescription. " +
                "Use FulfillPrescriptionAsync to start fulfillment.");

        var pharmacyId = prescription.FulfilledByPharmacyId.Value;

        // Check stock for this specific item before deducting
        var available = await _inventoryService.GetAvailableStockAsync(pharmacyId, item.MedicationId);
        if (available < item.Quantity)
            throw new InvalidOperationException(
                $"Insufficient stock for medication {item.MedicationId}: required {item.Quantity}, available {available}");

        item.IsFulfilled = true;
        item.FulfilledAt = DateTime.UtcNow;

        // Deduct stock immediately for this item (mirrors FulfillPrescriptionAsync behaviour)
        var referenceNumber = $"RX-{prescription.Id.ToString()[..8]}";
        await _inventoryService.DecreaseStockAsync(
            pharmacyId,
            item.MedicationId,
            item.Quantity,
            prescription.PatientId,
            prescription.Id,
            referenceNumber: referenceNumber);

        // The current item has not been saved yet, so exclude it from the database
        // predicate and combine the persisted state with the tracked mutation.
        var hasOtherUnfulfilledItems = await _context.PrescriptionItems
            .AnyAsync(i => i.PrescriptionId == prescription.Id &&
                           i.Id != item.Id &&
                           !i.IsFulfilled);
        var allItemsFulfilled = !hasOtherUnfulfilledItems;

        if (allItemsFulfilled)
        {
            prescription.Status = PrescriptionStatus.Fulfilled;
            prescription.FulfilledAt = DateTime.UtcNow;

            // Revoke QR token once the entire prescription is fulfilled
            if (!string.IsNullOrEmpty(prescription.QrToken))
            {
                await _qrService.RevokeTokenAsync(
                    prescription.QrToken, prescription.PatientId, "Prescription fully fulfilled via item");
            }
        }
        else
        {
            prescription.Status = PrescriptionStatus.PartiallyFulfilled;
        }

        prescription.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return item;
    }

    public async Task<Prescription> CancelPrescriptionAsync(Guid id, string? reason)
    {
        var prescription = await GetByIdAsync(id) ?? throw new InvalidOperationException("Prescription not found");
        prescription.Status = PrescriptionStatus.Cancelled;
        prescription.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "CancelPrescription",
            "Prescription",
            id.ToString(),
            prescription.DoctorId,
            $"Prescription cancelled: {reason}"
        );

        return prescription;
    }

    public async Task<bool> IsExpiredAsync(Guid id)
    {
        var prescription = await _context.Prescriptions.FindAsync(id)
            ?? throw new KeyNotFoundException($"Prescription {id} not found");

        return prescription.ExpiresAt < DateTime.UtcNow;
    }

    public async Task ExpireAsync(Guid prescriptionId)
    {
        var prescription = await _context.Prescriptions.FindAsync(prescriptionId)
            ?? throw new KeyNotFoundException($"Prescription {prescriptionId} not found.");

        // Only transition from non-terminal states — never overwrite Cancelled / Fulfilled
        if (prescription.Status is PrescriptionStatus.Active or PrescriptionStatus.PartiallyFulfilled)
        {
            prescription.Status = PrescriptionStatus.Expired;
            prescription.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditService.LogEntityActionAsync(
                "ExpirePrescription",
                "Prescription",
                prescriptionId.ToString(),
                prescription.DoctorId,
                "Prescription expired on-demand (past ExpiresAt) during QR generation request."
            );
        }
    }

    public async Task<List<Prescription>> GetExpiringSoonAsync(int daysThreshold = 7)
    {
        var thresholdDate = DateTime.UtcNow.AddDays(daysThreshold);

        return await _context.Prescriptions
            .Include(p => p.Patient)
            .Include(p => p.Doctor)
            .Where(p => p.Status == PrescriptionStatus.Active)
            .Where(p => p.ExpiresAt <= thresholdDate && p.ExpiresAt > DateTime.UtcNow)
            .OrderBy(p => p.ExpiresAt)
            .ToListAsync();
    }

    public async Task<List<Prescription>> GetAllAsync(PrescriptionStatus? status = null, int pageNumber = 1, int pageSize = 50)
    {
        var query = _context.Prescriptions
            .Include(p => p.Patient)
            .Include(p => p.Doctor)
            .Include(p => p.Items)
                .ThenInclude(i => i.Medication)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        return await query
            .OrderByDescending(p => p.IssuedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<Prescription>> GetByPharmacyAsync(Guid pharmacyId)
    {
        // Only returns prescriptions directly linked to this pharmacy:
        //   - Fulfilled/PartiallyFulfilled ones where FulfilledByPharmacyId == pharmacyId
        // We deliberately do NOT return all system-wide Active prescriptions here —
        // those are only accessible via QR scan (QRValidationController).
        return await _context.Prescriptions
            .Include(p => p.Patient)
            .Include(p => p.Doctor)
            .Include(p => p.Items)
                .ThenInclude(i => i.Medication)
            .Where(p => p.FulfilledByPharmacyId == pharmacyId)
            .OrderByDescending(p => p.IssuedAt)
            .ToListAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var prescription = await _context.Prescriptions
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException($"Prescription {id} not found");

        _context.Prescriptions.Remove(prescription);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "DeletePrescription",
            "Prescription",
            id.ToString(),
            Guid.Empty,
            $"Prescription {id} permanently deleted by admin"
        );
    }

    public async Task<int> CountDoctorPrescriptionsAsync(Guid doctorId, DateTime? issuedFrom = null, PrescriptionStatus? status = null)
    {
        var query = _context.Prescriptions
            .AsNoTracking()
            .Where(p => p.DoctorId == doctorId);

        if (issuedFrom.HasValue)
            query = query.Where(p => p.IssuedAt >= issuedFrom.Value);

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        return await query.CountAsync();
    }

    private string GenerateDigitalSignature(Prescription prescription)
    {
        var data = $"{prescription.Id}|{prescription.PatientId}|{prescription.DoctorId}|{prescription.IssuedAt:O}";
        // Use a dedicated prescription signing key that is separate from the QR signing key.
        // Reusing the same key for two distinct cryptographic purposes violates key-separation
        // best practice and could expose one scheme's signatures to the other.
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(_prescriptionSigningKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

    private static string ResolvePrescriptionSigningKey(
        SecurityOptions options,
        IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(options.PrescriptionSigningKey))
            return options.PrescriptionSigningKey;

        if (!environment.IsProduction() &&
            !string.IsNullOrWhiteSpace(options.QrSigningKey))
        {
            return options.QrSigningKey;
        }

        throw new InvalidOperationException(
            "Security:PrescriptionSigningKey is required in production.");
    }
}
