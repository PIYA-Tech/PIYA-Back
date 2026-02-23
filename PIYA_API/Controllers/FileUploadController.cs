using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FileUploadController(IFileUploadService fileUploadService) : ControllerBase
{
    private readonly IFileUploadService _fileUploadService = fileUploadService;

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

            // Verify user can upload documents for the specified userId
            // For now, users can only upload their own documents or doctors can upload for patients
            if (request.UserId != uploadedByUserId)
            {
                // TODO: Add role check - only doctors should be able to upload for other users
                return Forbid();
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
            return StatusCode(500, new { message = "An error occurred while uploading the document", error = ex.Message });
        }
    }

    /// <summary>
    /// Download a medical document
    /// </summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadDocument(Guid id)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Invalid user token" });
            }

            var document = await _fileUploadService.GetDocumentByIdAsync(id);
            
            if (document == null)
            {
                return NotFound(new { message = "Document not found" });
            }

            // Check access - user owns the document or is the uploader
            if (document.UserId != userId && document.UploadedByUserId != userId)
            {
                // TODO: Add role check - doctors should be able to view patient documents
                return Forbid();
            }

            var (fileStream, contentType, fileName) = await _fileUploadService.DownloadDocumentAsync(id);
            
            return File(fileStream, contentType, fileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "Document file not found" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while downloading the document", error = ex.Message });
        }
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
            return StatusCode(500, new { message = "An error occurred while retrieving documents", error = ex.Message });
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
            return StatusCode(500, new { message = "An error occurred while retrieving documents", error = ex.Message });
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
            return StatusCode(500, new { message = "An error occurred while archiving the document", error = ex.Message });
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
            return StatusCode(500, new { message = "An error occurred while deleting the document", error = ex.Message });
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

            // TODO: Add role check to ensure user is a doctor
            // For now, any authenticated user can verify

            var success = await _fileUploadService.VerifyDocumentAsync(id, doctorUserId);
            
            if (!success)
            {
                return NotFound(new { message = "Document not found" });
            }

            return Ok(new { message = "Document verified successfully" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while verifying the document", error = ex.Message });
        }
    }
}
