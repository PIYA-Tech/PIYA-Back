using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class FileUploadService : IFileUploadService
{
    private readonly PharmacyApiDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly long _maxFileSizeBytes;
    private readonly HashSet<string> _allowedMimeTypes;

    public FileUploadService(
        PharmacyApiDbContext context,
        IConfiguration configuration,
        IFileStorageService fileStorage)
    {
        _context = context;
        _fileStorage = fileStorage;

        _maxFileSizeBytes = long.Parse(configuration["FileUpload:MaxFileSizeMB"] ?? "10") * 1024 * 1024;

        _allowedMimeTypes = new HashSet<string>
        {
            "image/jpeg",
            "image/jpg",
            "image/png",
            "application/pdf",
            "application/dicom",
            "image/tiff",
            "image/bmp"
        };
    }

    public async Task<MedicalDocument> UploadDocumentAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        Guid userId,
        MedicalDocumentType documentType,
        Guid uploadedByUserId,
        string? title = null,
        string? notes = null,
        Guid? appointmentId = null,
        Guid? prescriptionId = null)
    {
        if (!IsValidFileType(contentType, fileName))
            throw new InvalidOperationException($"File type '{contentType}' is not allowed");

        if (fileStream.Length > _maxFileSizeBytes)
            throw new InvalidOperationException(
                $"File size exceeds maximum allowed size of {_maxFileSizeBytes / (1024 * 1024)} MB");

        var documentId = Guid.NewGuid();

        string fileHash;
        using (var sha256 = SHA256.Create())
        {
            fileStream.Position = 0;
            var hashBytes = await sha256.ComputeHashAsync(fileStream);
            fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var objectKey = IFileStorageService.BuildObjectKey("patients", userId, documentId, fileName);

        fileStream.Position = 0;
        await _fileStorage.UploadAsync(fileStream, objectKey, contentType);

        var document = new MedicalDocument
        {
            Id = documentId,
            UserId = userId,
            DocumentType = documentType,
            Title = title ?? $"{documentType} - {DateTime.UtcNow:yyyy-MM-dd}",
            FileName = fileName,
            StoredFileName = Path.GetFileName(objectKey),
            FilePath = null,
            ObjectKey = objectKey,
            ContentType = contentType,
            FileSizeBytes = fileStream.Length,
            FileHash = fileHash,
            UploadedByUserId = uploadedByUserId,
            UploadedAt = DateTime.UtcNow,
            Notes = notes,
            AppointmentId = appointmentId,
            PrescriptionId = prescriptionId,
            IsArchived = false,
            IsVerified = false
        };

        _context.MedicalDocuments.Add(document);
        await _context.SaveChangesAsync();

        return document;
    }

    public async Task<MedicalDocument?> GetDocumentByIdAsync(Guid id)
    {
        return await _context.MedicalDocuments
            .Include(d => d.User)
            .Include(d => d.UploadedBy)
            .Include(d => d.VerifiedBy)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<List<MedicalDocument>> GetUserDocumentsAsync(Guid userId, bool includeArchived = false)
    {
        var query = _context.MedicalDocuments.Where(d => d.UserId == userId);

        if (!includeArchived)
            query = query.Where(d => !d.IsArchived);

        return await query.OrderByDescending(d => d.UploadedAt).ToListAsync();
    }

    public async Task<List<MedicalDocument>> GetDocumentsByTypeAsync(Guid userId, MedicalDocumentType documentType)
    {
        return await _context.MedicalDocuments
            .Where(d => d.UserId == userId && d.DocumentType == documentType && !d.IsArchived)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();
    }

    public async Task<string> GetPresignedUrlAsync(Guid id, int expirySeconds = 300)
    {
        var doc = await _context.MedicalDocuments
            .Where(d => d.Id == id)
            .Select(d => new { d.ObjectKey, d.FilePath })
            .FirstOrDefaultAsync()
            ?? throw new FileNotFoundException("Document not found");

        if (!string.IsNullOrEmpty(doc.ObjectKey))
            return await _fileStorage.GetPresignedUrlAsync(doc.ObjectKey, expirySeconds);

        if (!string.IsNullOrEmpty(doc.FilePath) && File.Exists(doc.FilePath))
            return $"__legacy:{doc.FilePath}";

        throw new FileNotFoundException("Document file not found");
    }

    [Obsolete("Use GetPresignedUrlAsync. Kept for legacy stream-download of records that predate S3 migration.")]
    public async Task<(Stream FileStream, string ContentType, string FileName)> DownloadDocumentAsync(Guid id)
    {
        var document = await GetDocumentByIdAsync(id) ?? throw new FileNotFoundException("Document not found");

        if (!string.IsNullOrEmpty(document.FilePath) && File.Exists(document.FilePath))
        {
            var fs = new FileStream(document.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (fs, document.ContentType, document.FileName);
        }

        throw new FileNotFoundException("Physical file not found");
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, Guid userId)
    {
        var document = await GetDocumentByIdAsync(id);

        if (document == null || document.UserId != userId)
            return false;

        if (!string.IsNullOrEmpty(document.ObjectKey))
            await _fileStorage.DeleteAsync(document.ObjectKey);
        else if (!string.IsNullOrEmpty(document.FilePath) && File.Exists(document.FilePath))
            File.Delete(document.FilePath);

        _context.MedicalDocuments.Remove(document);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> ArchiveDocumentAsync(Guid id, Guid userId)
    {
        var document = await GetDocumentByIdAsync(id);

        if (document == null || document.UserId != userId)
            return false;

        document.IsArchived = true;
        document.ArchivedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> VerifyDocumentAsync(Guid id, Guid doctorUserId)
    {
        var document = await GetDocumentByIdAsync(id);

        if (document == null)
            return false;

        document.IsVerified = true;
        document.VerifiedByUserId = doctorUserId;
        document.VerifiedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public bool IsValidFileType(string contentType, string fileName)
    {
        if (!_allowedMimeTypes.Contains(contentType.ToLowerInvariant()))
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".pdf", ".dcm", ".tiff", ".tif", ".bmp" };
        return allowedExtensions.Contains(extension);
    }

    public bool IsValidFileSize(long fileSizeBytes)
        => fileSizeBytes > 0 && fileSizeBytes <= _maxFileSizeBytes;
}
