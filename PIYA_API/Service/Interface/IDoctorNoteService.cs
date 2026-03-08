using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for managing digital medical certificates with public QR verification
/// </summary>
public interface IDoctorNoteService
{
    /// <summary>
    /// Create a new doctor note
    /// </summary>
    Task<(DoctorNote Note, string PublicToken)> CreateNoteAsync(DoctorNote note);
    
    /// <summary>
    /// Get doctor note by ID
    /// </summary>
    Task<DoctorNote?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Get all notes for a patient
    /// </summary>
    Task<List<DoctorNote>> GetPatientNotesAsync(Guid patientId);
    
    /// <summary>
    /// Get all notes created by a doctor
    /// </summary>
    Task<List<DoctorNote>> GetDoctorNotesAsync(Guid doctorId);
    
    /// <summary>
    /// Revoke a doctor note
    /// </summary>
    Task<DoctorNote> RevokeNoteAsync(Guid id, string? reason);
    
    /// <summary>
    /// Verify public token and get note (for anonymous public access)
    /// </summary>
    Task<DoctorNote?> VerifyPublicTokenAsync(string publicToken);

    /// <summary>
    /// Regenerate and return a fresh public token for a note (patient use)
    /// </summary>
    Task<string> RegeneratePublicTokenAsync(Guid noteId);
    
    /// <summary>
    /// Check if note is expired
    /// </summary>
    bool IsNoteExpired(DoctorNote note);
    
    /// <summary>
    /// Get notes expiring soon
    /// </summary>
    Task<List<DoctorNote>> GetExpiringSoonAsync(int daysThreshold = 7);
    
    /// <summary>
    /// Generate unique note number
    /// </summary>
    string GenerateNoteNumber();
}
