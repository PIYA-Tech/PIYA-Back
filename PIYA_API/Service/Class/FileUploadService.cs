using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class FileUploadService : IFileUploadService
{
    private readonly PharmacyApiDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly string _storagePath;
    private readonly long _maxFileSizeBytes;
    private readonly HashSet<string> _allowedMimeTypes;

    public FileUploadService(PharmacyApiDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
        
        // Get storage configuration
        _storagePath = _configuration["FileUpload:LocalStoragePath"] ?? "./uploads";
        _maxFileSizeBytes = long.Parse(_configuration["FileUpload:MaxFileSizeMB"] ?? "10") * 1024 * 1024;
        
        // Allowed MIME types
        _allowedMimeTypes = new HashSet<string>
        {
            "image/jpeg",
            "image/jpg",
            "image/png",
            "application/pdf",
            "application/dicom", // Medical imaging format
            "image/tiff",
            "image/bmp"
        };
        
        // Ensure storage directory exists
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
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
        // Validate file type
        if (!IsValidFileType(contentType, fileName))
        {
            throw new InvalidOperationException($"File type '{contentType}' is not allowed");
        }

        // Validate file size
        if (fileStream.Length > _maxFileSizeBytes)
        {
            throw new InvalidOperationException($"File size exceeds maximum allowed size of {_maxFileSizeBytes / (1024 * 1024)} MB");
        }

        // Generate unique stored filename
        var storedFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
        var filePath = Path.Combine(_storagePath, storedFileName);

        // Calculate file hash
        string fileHash;
        using (var sha256 = SHA256.Create())
        {
            fileStream.Position = 0;
            var hashBytes = await sha256.ComputeHashAsync(fileStream);
            fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        // Save file to storage
        fileStream.Position = 0;
        using (var fileWriteStream = File.Create(filePath))
        {
            await fileStream.CopyToAsync(fileWriteStream);
        }

        // Create database record
        var document = new MedicalDocument
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DocumentType = documentType,
            Title = title ?? $"{documentType} - {DateTime.UtcNow:yyyy-MM-dd}",
            FileName = fileName,
            StoredFileName = storedFileName,
            FilePath = filePath,
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
        var query = _context.MedicalDocuments
            .Where(d => d.UserId == userId);

        if (!includeArchived)
        {
            query = query.Where(d => !d.IsArchived);
        }

        return await query
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();
    }

    public async Task<List<MedicalDocument>> GetDocumentsByTypeAsync(Guid userId, MedicalDocumentType documentType)
    {
        return await _context.MedicalDocuments
            .Where(d => d.UserId == userId && d.DocumentType == documentType && !d.IsArchived)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();
    }

    public async Task<(Stream FileStream, string ContentType, string FileName)> DownloadDocumentAsync(Guid id)
    {
        var document = await GetDocumentByIdAsync(id) ?? throw new FileNotFoundException("Document not found");
        if (!File.Exists(document.FilePath))
        {
            throw new FileNotFoundException("Physical file not found");
        }

        var fileStream = new FileStream(document.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        
        return (fileStream, document.ContentType, document.FileName);
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, Guid userId)
    {
        var document = await GetDocumentByIdAsync(id);
        
        if (document == null || document.UserId != userId)
        {
            return false;
        }

        // Delete physical file
        if (File.Exists(document.FilePath))
        {
            File.Delete(document.FilePath);
        }

        // Remove from database
        _context.MedicalDocuments.Remove(document);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> ArchiveDocumentAsync(Guid id, Guid userId)
    {
        var document = await GetDocumentByIdAsync(id);
        
        if (document == null || document.UserId != userId)
        {
            return false;
        }

        document.IsArchived = true;
        document.ArchivedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> VerifyDocumentAsync(Guid id, Guid doctorUserId)
    {
        var document = await GetDocumentByIdAsync(id);
        
        if (document == null)
        {
            return false;
        }

        // TODO: Add check to ensure doctorUserId is actually a doctor
        // This would require a Role check or Doctor entity check

        document.IsVerified = true;
        document.VerifiedByUserId = doctorUserId;
        document.VerifiedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<string> GetDocumentPathAsync(Guid id)
    {
        var document = await _context.MedicalDocuments
            .Where(d => d.Id == id)
            .Select(d => d.FilePath)
            .FirstOrDefaultAsync();
        
        return document ?? throw new FileNotFoundException("Document not found");
    }

    public bool IsValidFileType(string contentType, string fileName)
    {
        // Check MIME type
        if (!_allowedMimeTypes.Contains(contentType.ToLowerInvariant()))
        {
            return false;
        }

        // Check file extension
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".pdf", ".dcm", ".tiff", ".tif", ".bmp" };
        
        return allowedExtensions.Contains(extension);
    }

    public bool IsValidFileSize(long fileSizeBytes)
    {
        return fileSizeBytes > 0 && fileSizeBytes <= _maxFileSizeBytes;
    }
}
