using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FileUploadController(
    IFileUploadService fileUploadService,
    IFileStorageService fileStorageService,
    IAppointmentService appointmentService,
    IPrescriptionService prescriptionService,
    ILogger<FileUploadController> logger) : ControllerBase
{
    private readonly IFileUploadService _fileUploadService = fileUploadService;
    private readonly IFileStorageService _fileStorageService = fileStorageService;
    private readonly IAppointmentService _appointmentService = appointmentService;
    private readonly IPrescriptionService _prescriptionService = prescriptionService;
    private readonly ILogger<FileUploadController> _logger = logger;

    /// <summary>
    /// Upload a medical document
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadDocument([FromForm] Model.UploadDocumentRequest request)
    {
        try
        {
            if (request.File == null || request.File.Length == 0)
            {
                return BadRequest(new { message = "No file provided" });
            }

            // Get uploading user ID from token
            var uploadedByUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(uploadedByUserIdClaim) || !Guid.TryParse(uploadedByUserIdClaim, out var uploadedByUserId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var isAdministrator = IsAdministrator(callerRole);

            // Cross-user uploads contain PHI and require an established care relationship.
            // A Doctor role by itself is not sufficient.
            if (request.UserId != uploadedByUserId &&
                !isAdministrator &&
                (callerRole != "Doctor" ||
                 !await _appointmentService.HasDoctorPatientRelationshipAsync(uploadedByUserId, request.UserId)))
            {
                return Forbid();
            }

            // Never allow a caller to attach a document to an unrelated appointment or
            // prescription. Besides preventing object-level authorization bypasses, this
            // keeps the medical record graph internally consistent.
            Appointment? appointment = null;
            if (request.AppointmentId.HasValue)
            {
                appointment = await _appointmentService.GetByIdAsync(request.AppointmentId.Value);
                if (appointment == null)
                    return BadRequest(new { message = "Appointment not found" });
                if (appointment.PatientId != request.UserId)
                    return BadRequest(new { message = "Appointment does not belong to the document owner" });
                if (callerRole == "Doctor" && appointment.DoctorId != uploadedByUserId)
                    return Forbid();
            }

            if (request.PrescriptionId.HasValue)
            {
                var prescription = await _prescriptionService.GetByIdAsync(request.PrescriptionId.Value);
                if (prescription == null)
                    return BadRequest(new { message = "Prescription not found" });
                if (prescription.PatientId != request.UserId)
                    return BadRequest(new { message = "Prescription does not belong to the document owner" });
                if (callerRole == "Doctor" && prescription.DoctorId != uploadedByUserId)
                    return Forbid();
                if (appointment != null &&
                    prescription.AppointmentId.HasValue &&
                    prescription.AppointmentId.Value != appointment.Id)
                {
                    return BadRequest(new { message = "Prescription does not belong to the specified appointment" });
                }
            }

            // Parse document type
            if (!Enum.TryParse<MedicalDocumentType>(request.DocumentType, true, out var docType))
            {
                return BadRequest(new { message = $"Invalid document type: {request.DocumentType}" });
            }

            // Validate file type and size
            if (!_fileUploadService.IsValidFileType(request.File.ContentType, request.File.FileName))
            {
                return BadRequest(new { message = "Invalid file type. Allowed types: JPEG, PNG, PDF, DICOM, TIFF, BMP" });
            }

            if (!_fileUploadService.IsValidFileSize(request.File.Length))
            {
                return BadRequest(new { message = "File size exceeds maximum allowed size" });
            }

            // Upload document
            using (var stream = request.File.OpenReadStream())
            {
                var document = await _fileUploadService.UploadDocumentAsync(
                    stream,
                    request.File.FileName,
                    request.File.ContentType,
                    request.UserId,
                    docType,
                    uploadedByUserId,
                    request.Title,
                    request.Notes,
                    request.AppointmentId,
                    request.PrescriptionId);

                return Ok(new
                {
                    message = "Document uploaded successfully",
                    documentId = document.Id,
                    fileName = document.FileName,
                    documentType = document.DocumentType.ToString(),
                    uploadedAt = document.UploadedAt
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected document upload failure");
            return StatusCode(500, new { message = "An error occurred while uploading the document" });
        }
    }

    /// <summary>
    /// Get a short-lived presigned URL to download a document.
    /// The URL is valid for 5 minutes. Clients should redirect / fetch directly to this URL.
    /// </summary>
    [HttpGet("{id}/presigned-url")]
    public async Task<IActionResult> GetPresignedUrl(Guid id, [FromQuery] int expirySeconds = 300)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { message = "Invalid user token" });

            var document = await _fileUploadService.GetDocumentByIdAsync(id);
            if (document == null)
                return NotFound(new { message = "Document not found" });

            var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (!await CanAccessDocumentAsync(document, userId, callerRole))
                return Forbid();

            var boundedExpirySeconds = Math.Clamp(expirySeconds, 30, 3600);
            var url = await _fileUploadService.GetPresignedUrlAsync(id, boundedExpirySeconds);
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(boundedExpirySeconds);

            return Ok(new { url, expiresInSeconds = boundedExpirySeconds, expiresAt });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "Document file not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate a presigned URL for document {DocumentId}", id);
            return StatusCode(500, new { message = "An error occurred while generating the presigned URL" });
        }
    }

    /// <summary>
    /// Redeem a local-storage presigned token (Development only).
    /// This endpoint is called by the URL returned by LocalFileStorageService.GetPresignedUrlAsync.
    /// </summary>
    [HttpGet("local-download/{token}")]
    [AllowAnonymous] // token is single-use + expiry-checked inside the service
    public IActionResult LocalDownload(string token)
    {
        if (_fileStorageService is not LocalFileStorageService localService)
            return NotFound(new { message = "Local download is only available in Development" });

        var result = localService.RedeemToken(token);
        if (result is null)
            return NotFound(new { message = "Token expired or not found" });

        var (stream, objectKey) = result.Value;
        var ext = Path.GetExtension(objectKey).ToLowerInvariant();
        var contentType = ext switch
        {
            ".pdf"  => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png"  => "image/png",
            ".tiff" or ".tif" => "image/tiff",
            ".bmp"  => "image/bmp",
            ".dcm"  => "application/dicom",
            _       => "application/octet-stream"
        };

        return File(stream, contentType, Path.GetFileName(objectKey));
    }

    /// <summary>
    /// Get all documents for the authenticated user
    /// </summary>
    [HttpGet("my-documents")]
    public async Task<IActionResult> GetMyDocuments([FromQuery] bool includeArchived = false)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            var documents = await _fileUploadService.GetUserDocumentsAsync(userId, includeArchived);
            
            var documentDtos = documents.Select(d => new
            {
                d.Id,
                d.DocumentType,
                d.Title,
                d.FileName,
                d.ContentType,
                FileSizeMB = Math.Round(d.FileSizeBytes / (1024.0 * 1024.0), 2),
                d.UploadedAt,
                d.IsVerified,
                d.IsArchived,
                d.AppointmentId,
                d.PrescriptionId,
                UploadedBy = d.UploadedBy != null ? new { 
                    Name = $"{d.UploadedBy.FirstName} {d.UploadedBy.LastName}",
                    d.UploadedBy.Email 
                } : null
            });

            return Ok(documentDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve documents for the current user");
            return StatusCode(500, new { message = "An error occurred while retrieving documents" });
        }
    }

    /// <summary>
    /// Get documents by type
    /// </summary>
    [HttpGet("by-type/{documentType}")]
    public async Task<IActionResult> GetDocumentsByType(string documentType)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            if (!Enum.TryParse<MedicalDocumentType>(documentType, true, out var docType))
            {
                return BadRequest(new { message = $"Invalid document type: {documentType}" });
            }

            var documents = await _fileUploadService.GetDocumentsByTypeAsync(userId, docType);
            
            var documentDtos = documents.Select(d => new
            {
                d.Id,
                d.Title,
                d.FileName,
                FileSizeMB = Math.Round(d.FileSizeBytes / (1024.0 * 1024.0), 2),
                d.UploadedAt,
                d.IsVerified
            });

            return Ok(documentDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve documents by type {DocumentType}", documentType);
            return StatusCode(500, new { message = "An error occurred while retrieving documents" });
        }
    }

    /// <summary>
    /// Archive a document
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> ArchiveDocument(Guid id)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            var success = await _fileUploadService.ArchiveDocumentAsync(id, userId);
            
            if (!success)
            {
                return NotFound(new { message = "Document not found or you don't have permission to archive it" });
            }

            return Ok(new { message = "Document archived successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive document {DocumentId}", id);
            return StatusCode(500, new { message = "An error occurred while archiving the document" });
        }
    }

    /// <summary>
    /// Delete a document
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            var success = await _fileUploadService.DeleteDocumentAsync(id, userId);
            
            if (!success)
            {
                return NotFound(new { message = "Document not found or you don't have permission to delete it" });
            }

            return Ok(new { message = "Document deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document {DocumentId}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the document" });
        }
    }

    /// <summary>
    /// Verify a document (doctors only)
    /// </summary>
    [HttpPost("{id}/verify")]
    public async Task<IActionResult> VerifyDocument(Guid id)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var doctorUserId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            // Only the patient's treating doctor (or an administrator) may verify a
            // document. A global Doctor role is not an object-level permission.
            var callerRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (callerRole != "Doctor" && !IsAdministrator(callerRole))
                return Forbid();

            var document = await _fileUploadService.GetDocumentByIdAsync(id);
            if (document == null)
                return NotFound(new { message = "Document not found" });
            if (!await CanAccessDocumentAsync(document, doctorUserId, callerRole))
                return Forbid();

            var success = await _fileUploadService.VerifyDocumentAsync(id, doctorUserId);
            
            if (!success)
            {
                return NotFound(new { message = "Document not found" });
            }

            return Ok(new { message = "Document verified successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify document {DocumentId}", id);
            return StatusCode(500, new { message = "An error occurred while verifying the document" });
        }
    }

    private static bool IsAdministrator(string? role) =>
        role is "Admin" or "SuperAdmin";

    private async Task<bool> CanAccessDocumentAsync(
        MedicalDocument document,
        Guid callerId,
        string? callerRole)
    {
        if (document.UserId == callerId || IsAdministrator(callerRole))
            return true;

        if (callerRole != "Doctor")
            return false;

        // Prefer an explicit document link when present.
        if (document.AppointmentId.HasValue)
        {
            var appointment = await _appointmentService.GetByIdAsync(document.AppointmentId.Value);
            if (appointment != null &&
                appointment.PatientId == document.UserId &&
                appointment.DoctorId == callerId &&
                appointment.Status is not AppointmentStatus.Cancelled and not AppointmentStatus.NoShow)
            {
                return true;
            }
        }

        if (document.PrescriptionId.HasValue)
        {
            var prescription = await _prescriptionService.GetByIdAsync(document.PrescriptionId.Value);
            if (prescription != null &&
                prescription.PatientId == document.UserId &&
                prescription.DoctorId == callerId)
            {
                return true;
            }
        }

        // Legacy documents may predate appointment/prescription links. Permit access only
        // when the same doctor has a non-cancelled clinical relationship with the patient.
        return await _appointmentService.HasDoctorPatientRelationshipAsync(callerId, document.UserId);
    }
}
