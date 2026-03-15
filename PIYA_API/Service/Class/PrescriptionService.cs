using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class PrescriptionService(
    PharmacyApiDbContext context,
    IAuditService auditService,
    IQRService qrService,
    IConfiguration configuration,
    IInventoryService inventoryService,
    ILogger<PrescriptionService> logger) : IPrescriptionService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly IQRService _qrService = qrService;
    private readonly IConfiguration _configuration = configuration;
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

    public async Task<List<Prescription>> GetPatientPrescriptionsAsync(Guid patientId, PrescriptionStatus? status = null)
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
            .ToListAsync();
    }

    public async Task<List<Prescription>> GetDoctorPrescriptionsAsync(Guid doctorId, PrescriptionStatus? status = null)
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
            .ToListAsync();
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
        var prescription = await GetByIdAsync(prescriptionId) ?? throw new InvalidOperationException("Prescription not found");
        if (prescription.Status != PrescriptionStatus.Active && prescription.Status != PrescriptionStatus.PartiallyFulfilled)
        {
            throw new InvalidOperationException($"Cannot fulfill a prescription with status '{prescription.Status}'");
        }

        // Verify adequate stock exists for UNFULFILLED items BEFORE making any state changes.
        // This prevents a prescription being marked Fulfilled when stock is insufficient.
        // Items that are already fulfilled (PartiallyFulfilled re-entry) are skipped to
        // avoid double-deducting stock that was already decremented in a prior partial call.
        var unfulfilledItems = prescription.Items.Where(i => !i.IsFulfilled).ToList();
        var stockErrors = new List<string>();
        foreach (var item in unfulfilledItems)
        {
            var available = await _inventoryService.GetAvailableStockAsync(pharmacyId, item.MedicationId);
            if (available < item.Quantity)
            {
                stockErrors.Add(
                    $"Medication {item.MedicationId}: required {item.Quantity}, available {available}");
            }
        }
        if (stockErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Insufficient stock to fulfill prescription. " + string.Join("; ", stockErrors));
        }

        prescription.Status = PrescriptionStatus.Fulfilled;
        prescription.FulfilledAt = DateTime.UtcNow;
        prescription.FulfilledByPharmacyId = pharmacyId;
        prescription.UpdatedAt = DateTime.UtcNow;

        // Mark all items as fulfilled
        foreach (var item in prescription.Items)
        {
            item.IsFulfilled = true;
            item.FulfilledAt = DateTime.UtcNow;
        }

        // Revoke QR token (one-time use)
        if (!string.IsNullOrEmpty(prescription.QrToken))
        {
            await _qrService.RevokeTokenAsync(prescription.QrToken, prescription.PatientId, "Prescription fulfilled");
        }

        // Deduct stock only for the items that were NOT already fulfilled before this call.
        // This prevents double-deducting stock for items already handled in a prior partial fulfillment.
        var referenceNumber = $"RX-{prescriptionId.ToString()[..8]}";
        foreach (var item in unfulfilledItems)
        {
            await _inventoryService.DecreaseStockAsync(
                pharmacyId,
                item.MedicationId,
                item.Quantity,
                prescription.PatientId,
                prescriptionId,
                referenceNumber: referenceNumber
            );
        }

        // Persist the prescription status only after all stock has been successfully deducted.
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "FulfillPrescription",
            "Prescription",
            prescriptionId.ToString(),
            null,
            $"Prescription fulfilled by pharmacy {pharmacyId}"
        );

        return prescription;
    }

    public async Task<PrescriptionItem> FulfillPrescriptionItemAsync(Guid itemId)
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

        // Check if all items are now fulfilled
        var allItemsFulfilled = await _context.PrescriptionItems
            .Where(i => i.PrescriptionId == prescription.Id)
            .AllAsync(i => i.IsFulfilled);

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
        var signingKey = _configuration["Security:PrescriptionSigningKey"]
            ?? _configuration["Security:QrSigningKey"]   // fallback for legacy deploys
            ?? throw new InvalidOperationException("Security:PrescriptionSigningKey is not configured");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }
}
