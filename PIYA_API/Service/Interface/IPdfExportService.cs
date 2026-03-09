namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for generating PDF documents
/// </summary>
public interface IPdfExportService
{
    /// <summary>
    /// Generate prescription PDF
    /// </summary>
    Task<byte[]> GeneratePrescriptionPdfAsync(Guid prescriptionId);
    
    /// <summary>
    /// Generate doctor note/certificate PDF
    /// </summary>
    Task<byte[]> GenerateDoctorNotePdfAsync(Guid doctorNoteId);
    
    /// <summary>
    /// Generate appointment confirmation PDF
    /// </summary>
    Task<byte[]> GenerateAppointmentConfirmationPdfAsync(Guid appointmentId);
    
    /// <summary>
    /// Generate patient medical summary PDF
    /// </summary>
    Task<byte[]> GeneratePatientMedicalSummaryPdfAsync(Guid userId);

    /// <summary>
    /// Generate referral letter PDF
    /// </summary>
    Task<byte[]> GenerateReferralLetterPdfAsync(Guid referralId);
}
