using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for managing medical document uploads
/// </summary>
public interface IFileUploadService
{
    /// <summary>
    /// Upload a medical document
    /// </summary>
    Task<MedicalDocument> UploadDocumentAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        Guid userId,
        MedicalDocumentType documentType,
        Guid uploadedByUserId,
        string? title = null,
        string? notes = null,
        Guid? appointmentId = null,
        Guid? prescriptionId = null);
    
    /// <summary>
    /// Get document by ID
    /// </summary>
    Task<MedicalDocument?> GetDocumentByIdAsync(Guid id);
    
    /// <summary>
    /// Get all documents for a user
    /// </summary>
    Task<List<MedicalDocument>> GetUserDocumentsAsync(Guid userId, bool includeArchived = false);
    
    /// <summary>
    /// Get documents by type
    /// </summary>
    Task<List<MedicalDocument>> GetDocumentsByTypeAsync(Guid userId, MedicalDocumentType documentType);
    
    /// <summary>
    /// Download document
    /// </summary>
    Task<(Stream FileStream, string ContentType, string FileName)> DownloadDocumentAsync(Guid id);
    
    /// <summary>
    /// Delete document
    /// </summary>
    Task<bool> DeleteDocumentAsync(Guid id, Guid userId);
    
    /// <summary>
    /// Archive document
    /// </summary>
    Task<bool> ArchiveDocumentAsync(Guid id, Guid userId);
    
    /// <summary>
    /// Verify document (doctor only)
    /// </summary>
    Task<bool> VerifyDocumentAsync(Guid id, Guid doctorUserId);
    
    /// <summary>
    /// Get a short-lived presigned URL to access the document's binary content.
    /// </summary>
    Task<string> GetPresignedUrlAsync(Guid id, int expirySeconds = 300);
    
    /// <summary>
    /// Validate file type
    /// </summary>
    bool IsValidFileType(string contentType, string fileName);
    
    /// <summary>
    /// Validate file size
    /// </summary>
    bool IsValidFileSize(long fileSizeBytes);
}
