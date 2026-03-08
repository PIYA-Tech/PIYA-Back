using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for managing digital prescriptions
/// </summary>
public interface IPrescriptionService
{
    /// <summary>
    /// Create a new prescription
    /// </summary>
    Task<Prescription> CreatePrescriptionAsync(Prescription prescription);
    
    /// <summary>
    /// Get prescription by ID
    /// </summary>
    Task<Prescription?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Get all prescriptions for a patient
    /// </summary>
    Task<List<Prescription>> GetPatientPrescriptionsAsync(Guid patientId, PrescriptionStatus? status = null);
    
    /// <summary>
    /// Get all prescriptions created by a doctor
    /// </summary>
    Task<List<Prescription>> GetDoctorPrescriptionsAsync(Guid doctorId);
    
    /// <summary>
    /// Generate QR code for prescription (5-minute validity)
    /// </summary>
    Task<string> GenerateQrCodeAsync(Guid prescriptionId);
    
    /// <summary>
    /// Validate QR code and return prescription
    /// </summary>
    Task<Prescription?> ValidateQrCodeAsync(string qrToken);
    
    /// <summary>
    /// Mark prescription as fulfilled
    /// </summary>
    Task<Prescription> FulfillPrescriptionAsync(Guid prescriptionId, Guid pharmacyId);
    
    /// <summary>
    /// Mark prescription item as fulfilled
    /// </summary>
    Task<PrescriptionItem> FulfillPrescriptionItemAsync(Guid itemId);
    
    /// <summary>
    /// Cancel prescription
    /// </summary>
    Task<Prescription> CancelPrescriptionAsync(Guid id, string? reason);
    
    /// <summary>
    /// Check if prescription is expired
    /// </summary>
    Task<bool> IsExpiredAsync(Guid id);
    
    /// <summary>
    /// Get prescriptions expiring soon
    /// </summary>
    Task<List<Prescription>> GetExpiringSoonAsync(int daysThreshold = 7);

    /// <summary>
    /// Get all prescriptions (Admin only), optionally filtered by status
    /// </summary>
    Task<List<Prescription>> GetAllAsync(PrescriptionStatus? status = null, int pageNumber = 1, int pageSize = 50);

    /// <summary>
    /// Permanently delete a prescription (Admin only)
    /// </summary>
    Task DeleteAsync(Guid id);
}
