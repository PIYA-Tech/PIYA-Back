using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PIYA_API.Model;

/// <summary>
/// DTO for multipart/form-data upload requests
/// Use [FromForm] UploadDocumentRequest to bind file + form fields
/// </summary>
public class UploadDocumentRequest
{
    [Required]
    public IFormFile File { get; set; } = default!;

    [Required]
    public string DocumentType { get; set; } = string.Empty;

    [Required]
    public Guid UserId { get; set; }

    public string? Title { get; set; }

    public string? Notes { get; set; }

    public Guid? AppointmentId { get; set; }

    public Guid? PrescriptionId { get; set; }
}
