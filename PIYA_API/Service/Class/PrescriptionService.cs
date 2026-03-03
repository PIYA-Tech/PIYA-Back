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
    ILogger<PrescriptionService> logger) : IPrescriptionService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly IQRService _qrService = qrService;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<PrescriptionService> _logger = logger;

    public async Task<Prescription> CreatePrescriptionAsync(Prescription prescription)
    {
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

    public async Task<List<Prescription>> GetDoctorPrescriptionsAsync(Guid doctorId)
    {
        return await _context.Prescriptions
            .Include(p => p.Patient)
            .Include(p => p.Items)
                .ThenInclude(i => i.Medication)
            .Where(p => p.DoctorId == doctorId)
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

        // Revoke QR token (one-time use) - use system user ID for automatic fulfillment
        if (!string.IsNullOrEmpty(prescription.QrToken))
        {
            await _qrService.RevokeTokenAsync(prescription.QrToken, prescription.PatientId, "Prescription fulfilled");
        }

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
        item.IsFulfilled = true;
        item.FulfilledAt = DateTime.UtcNow;

        // Check if all items are fulfilled
        var prescription = item.Prescription;
        var allItemsFulfilled = await _context.PrescriptionItems
            .Where(i => i.PrescriptionId == prescription.Id)
            .AllAsync(i => i.IsFulfilled);

        if (allItemsFulfilled)
        {
            prescription.Status = PrescriptionStatus.Fulfilled;
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
        var prescription = await _context.Prescriptions.FindAsync(id);
        if (prescription == null)
        {
            return true;
        }

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

    private string GenerateDigitalSignature(Prescription prescription)
    {
        var data = $"{prescription.Id}|{prescription.PatientId}|{prescription.DoctorId}|{prescription.IssuedAt:O}";
        // Use the configured QR signing key so the signature differs per environment
        var signingKey = _configuration["Security:QrSigningKey"]
            ?? throw new InvalidOperationException("Security:QrSigningKey is not configured");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }
}
