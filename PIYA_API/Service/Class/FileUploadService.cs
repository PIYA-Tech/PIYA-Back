using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class FileUploadService(
    PharmacyApiDbContext context,
    IConfiguration configuration,
    IFileStorageService fileStorage,
    IFileSecurityScanner fileSecurityScanner) : IFileUploadService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IFileStorageService _fileStorage = fileStorage;
    private readonly IFileSecurityScanner _fileSecurityScanner = fileSecurityScanner;
    private readonly long _maxFileSizeBytes = long.Parse(configuration["FileUpload:MaxFileSizeMB"] ?? "10") * 1024 * 1024;
    private readonly HashSet<string> _allowedMimeTypes =
        [
            "image/jpeg",
            "image/jpg",
            "image/png",
            "application/pdf",
            "application/dicom",
            "image/tiff",
            "image/bmp"
        ];

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
        var safeFileName = Path.GetFileName(fileName);
        var normalizedContentType = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(safeFileName) || !IsValidFileType(normalizedContentType, safeFileName))
            throw new InvalidOperationException($"File type '{contentType}' is not allowed");

        if (!fileStream.CanSeek)
            throw new InvalidOperationException("The upload stream must be seekable.");

        if (!IsValidFileSize(fileStream.Length))
            throw new InvalidOperationException(
                $"File size exceeds maximum allowed size of {_maxFileSizeBytes / (1024 * 1024)} MB");

        await ValidateFileSignatureAsync(fileStream, safeFileName);
        await _fileSecurityScanner.ScanAsync(fileStream);

        var documentId = Guid.NewGuid();

        string fileHash;
        using (var sha256 = SHA256.Create())
        {
            fileStream.Position = 0;
            var hashBytes = await sha256.ComputeHashAsync(fileStream);
            fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var objectKey = IFileStorageService.BuildObjectKey("patients", userId, documentId, safeFileName);

        fileStream.Position = 0;
        await _fileStorage.UploadAsync(fileStream, objectKey, normalizedContentType);

        var document = new MedicalDocument
        {
            Id = documentId,
            UserId = userId,
            DocumentType = documentType,
            Title = title ?? $"{documentType} - {DateTime.UtcNow:yyyy-MM-dd}",
            FileName = safeFileName,
            StoredFileName = Path.GetFileName(objectKey),
            FilePath = null,
            ObjectKey = objectKey,
            ContentType = normalizedContentType,
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

        try
        {
            _context.MedicalDocuments.Add(document);
            await _context.SaveChangesAsync();
        }
        catch
        {
            // Object storage and the relational database cannot share a transaction.
            // Compensate a metadata failure so sensitive orphaned blobs are not retained.
            await _fileStorage.DeleteAsync(objectKey);
            throw;
        }

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
        var normalizedMime = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (!_allowedMimeTypes.Contains(normalizedMime))
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => normalizedMime is "image/jpeg" or "image/jpg",
            ".png" => normalizedMime == "image/png",
            ".pdf" => normalizedMime == "application/pdf",
            ".dcm" => normalizedMime == "application/dicom",
            ".tiff" or ".tif" => normalizedMime == "image/tiff",
            ".bmp" => normalizedMime == "image/bmp",
            _ => false,
        };
    }

    public bool IsValidFileSize(long fileSizeBytes)
        => fileSizeBytes > 0 && fileSizeBytes <= _maxFileSizeBytes;

    private static async Task ValidateFileSignatureAsync(Stream stream, string fileName)
    {
        var header = new byte[132];
        stream.Position = 0;
        var bytesRead = await stream.ReadAsync(header);
        stream.Position = 0;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var valid = extension switch
        {
            ".jpg" or ".jpeg" => bytesRead >= 3 &&
                header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
            ".png" => bytesRead >= 8 &&
                header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            ".pdf" => bytesRead >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            ".tiff" or ".tif" => bytesRead >= 4 &&
                (header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x49, 0x49, 0x2a, 0x00 }) ||
                 header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x4d, 0x4d, 0x00, 0x2a })),
            ".bmp" => bytesRead >= 2 && header[0] == 0x42 && header[1] == 0x4d,
            ".dcm" => bytesRead >= 132 && header.AsSpan(128, 4).SequenceEqual("DICM"u8),
            _ => false,
        };

        if (!valid)
            throw new InvalidOperationException("The file content does not match its declared type.");
    }
}
