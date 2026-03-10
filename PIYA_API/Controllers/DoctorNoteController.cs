using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/doctor-notes")]
[Authorize]
public class DoctorNoteController(
    IDoctorNoteService doctorNoteService,
    IDoctorProfileService doctorProfileService,
    ILogger<DoctorNoteController> logger) : ControllerBase
{
    private readonly IDoctorNoteService _doctorNoteService = doctorNoteService;
    private readonly IDoctorProfileService _doctorProfileService = doctorProfileService;
    private readonly ILogger<DoctorNoteController> _logger = logger;

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    private string GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

    /// <summary>
    /// Create a new medical certificate/note (Doctor only)
    /// </summary>
    /// <remarks>
    /// Creates a digital medical certificate with a public verification token.
    /// The token is returned once and should be stored by the client for QR generation.
    /// </remarks>

    [HttpPost]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<CreateDoctorNoteResponse>> CreateNote([FromBody] CreateDoctorNoteRequest request)
    {
        try
        {
            var doctorId = GetUserId();

            // Validate dates
            if (request.ValidFrom >= request.ValidTo)
            {
                return BadRequest(new { error = "ValidFrom must be before ValidTo" });
            }

            if (request.ValidTo < DateTime.UtcNow)
            {
                return BadRequest(new { error = "ValidTo cannot be in the past" });
            }

            // Create note
            var note = new DoctorNote
            {
                Id = Guid.NewGuid(),
                PatientId = request.PatientId,
                DoctorId = doctorId,
                AppointmentId = request.AppointmentId,
                Title = request.Title,
                Summary = request.Summary,
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                IncludeSummaryInPublicView = request.IncludeSummaryInPublicView,
                ClinicName = request.ClinicName,
                PublicTokenHash = string.Empty, // Will be set by service
                NoteNumber = string.Empty, // Will be set by service
                Status = DoctorNoteStatus.Active,
                IssuedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var (createdNote, publicToken) = await _doctorNoteService.CreateNoteAsync(note);

            return Ok(new CreateDoctorNoteResponse
            {
                NoteId = createdNote.Id,
                NoteNumber = createdNote.NoteNumber,
                PublicToken = publicToken,
                ValidFrom = createdNote.ValidFrom,
                ValidTo = createdNote.ValidTo,
                IssuedAt = createdNote.IssuedAt,
                Message = "Medical note created successfully. Save the public token for QR generation."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating doctor note");
            return StatusCode(500, new { error = "Failed to create doctor note" });
        }
    }

    /// <summary>
    /// Get a specific doctor note by ID (Doctor/Patient only)
    /// </summary>
    /// <remarks>
    /// Doctors can view notes they created. Patients can view their own notes.
    /// </remarks>
    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<DoctorNoteDto>> GetNoteById(Guid id)
    {
        try
        {
            var note = await _doctorNoteService.GetByIdAsync(id);
            if (note == null)
            {
                return NotFound(new { error = "Doctor note not found" });
            }

            var userId = GetUserId();
            var role = GetUserRole();

            // Authorization check
            bool isAuthorized = role == "Doctor" && note.DoctorId == userId ||
                               role == "Patient" && note.PatientId == userId ||
                               role == "Admin";

            if (!isAuthorized)
            {
                return Forbid();
            }

            return Ok(MapToDto(note));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor note {NoteId}", id);
            return StatusCode(500, new { error = "Failed to retrieve doctor note" });
        }
    }

    /// <summary>
    /// Get all notes created by the current doctor
    /// </summary>
    [HttpGet("my-notes")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<List<DoctorNoteDto>>> GetMyNotes()
    {
        try
        {
            var doctorId = GetUserId();
            var notes = await _doctorNoteService.GetDoctorNotesAsync(doctorId);

            return Ok(notes.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor notes");
            return StatusCode(500, new { error = "Failed to retrieve doctor notes" });
        }
    }

    /// <summary>
    /// Get all notes for current patient
    /// </summary>
    [HttpGet("patient/my-notes")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<List<DoctorNoteDto>>> GetMyPatientNotes()
    {
        try
        {
            var patientId = GetUserId();
            var notes = await _doctorNoteService.GetPatientNotesAsync(patientId);

            return Ok(notes.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving patient notes");
            return StatusCode(500, new { error = "Failed to retrieve patient notes" });
        }
    }

    /// <summary>
    /// Get (or regenerate) the public verification token for a note — Patient only
    /// </summary>
    /// <remarks>
    /// Each call issues a fresh token and invalidates the previous one.
    /// The patient uses this to generate a QR code to present to their employer/school.
    /// </remarks>
    [HttpPost("{id}/patient-token")]
    [Authorize(Roles = "Patient")]
    public async Task<ActionResult<PatientTokenResponse>> GetPatientToken(Guid id)
    {
        try
        {
            var note = await _doctorNoteService.GetByIdAsync(id);
            if (note == null)
                return NotFound(new { error = "Doctor note not found" });

            var patientId = GetUserId();
            if (note.PatientId != patientId)
                return Forbid();

            if (note.Status == DoctorNoteStatus.Revoked)
                return BadRequest(new { error = "This note has been revoked and cannot be shared." });

            var token = await _doctorNoteService.RegeneratePublicTokenAsync(id);

            return Ok(new PatientTokenResponse { PublicToken = token });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating patient token for note {NoteId}", id);
            return StatusCode(500, new { error = "Failed to generate token" });
        }
    }

    /// <summary>
    /// Revoke a doctor note (Doctor only)
    /// </summary>
    /// <remarks>
    /// Only the doctor who created the note can revoke it.
    /// </remarks>
    [HttpPost("{id}/revoke")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<DoctorNoteDto>> RevokeNote(Guid id, [FromBody] RevokeNoteRequest? request)
    {
        try
        {
            var note = await _doctorNoteService.GetByIdAsync(id);
            if (note == null)
            {
                return NotFound(new { error = "Doctor note not found" });
            }

            var doctorId = GetUserId();
            if (note.DoctorId != doctorId)
            {
                return Forbid();
            }

            if (note.Status == DoctorNoteStatus.Revoked)
            {
                return BadRequest(new { error = "Note is already revoked" });
            }

            var revokedNote = await _doctorNoteService.RevokeNoteAsync(id, request?.Reason);

            return Ok(MapToDto(revokedNote));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking doctor note {NoteId}", id);
            return StatusCode(500, new { error = "Failed to revoke doctor note" });
        }
    }

    /// <summary>
    /// Verify a public token (Anonymous access - for QR scanning)
    /// </summary>
    /// <remarks>
    /// This endpoint allows anyone to verify the authenticity of a medical certificate
    /// by scanning the QR code. Returns minimal public information.
    /// </remarks>
    [HttpGet("verify")]
    [AllowAnonymous]
    public async Task<ActionResult<DoctorNotePublicDto>> VerifyToken([FromQuery] string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest(new { error = "Token is required" });
            }

            var note = await _doctorNoteService.VerifyPublicTokenAsync(token);
            if (note == null)
            {
                return NotFound(new { error = "Invalid or expired verification token" });
            }

            // Get doctor's license number
            var doctorProfile = await _doctorProfileService.GetByUserIdAsync(note.DoctorId);

            // Check if expired
            var isExpired = _doctorNoteService.IsNoteExpired(note);
            var status = note.Status;
            if (isExpired && status == DoctorNoteStatus.Active)
            {
                status = DoctorNoteStatus.Expired;
            }

            return Ok(new DoctorNotePublicDto
            {
                NoteNumber = note.NoteNumber,
                DoctorName = $"Dr. {note.Doctor.FirstName} {note.Doctor.LastName}",
                DoctorLicenseNumber = doctorProfile?.LicenseNumber,
                PatientName = note.IncludeSummaryInPublicView 
                    ? $"{note.Patient.FirstName} {note.Patient.LastName}"
                    : $"{note.Patient.FirstName[0]}. {note.Patient.LastName[0]}.",
                ClinicName = note.ClinicName,
                Title = note.Title,
                Summary = note.IncludeSummaryInPublicView ? note.Summary : null,
                ValidFrom = note.ValidFrom,
                ValidTo = note.ValidTo,
                IssuedAt = note.IssuedAt,
                Status = status.ToString(),
                IsValid = status == DoctorNoteStatus.Active && !isExpired
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying token");
            return StatusCode(500, new { error = "Failed to verify token" });
        }
    }

    /// <summary>
    /// Get notes expiring soon (Doctor only)
    /// </summary>
    [HttpGet("expiring-soon")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult<List<DoctorNoteDto>>> GetExpiringSoon([FromQuery] int daysThreshold = 7)
    {
        try
        {
            var notes = await _doctorNoteService.GetExpiringSoonAsync(daysThreshold);
            var doctorId = GetUserId();

            // Filter to only show notes created by this doctor
            var doctorNotes = notes.Where(n => n.DoctorId == doctorId).ToList();

            return Ok(doctorNotes.Select(MapToDto).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving expiring notes");
            return StatusCode(500, new { error = "Failed to retrieve expiring notes" });
        }
    }

    #region DTOs and Helpers

    private static DoctorNoteDto MapToDto(DoctorNote note)
    {
        return new DoctorNoteDto
        {
            Id = note.Id,
            PatientId = note.PatientId,
            PatientName = note.Patient is not null
                ? $"{note.Patient.FirstName} {note.Patient.LastName}"
                : "Unknown Patient",
            DoctorId = note.DoctorId,
            DoctorName = note.Doctor is not null
                ? $"Dr. {note.Doctor.FirstName} {note.Doctor.LastName}"
                : "Unknown Doctor",
            AppointmentId = note.AppointmentId,
            Title = note.Title,
            Summary = note.Summary,
            ValidFrom = note.ValidFrom,
            ValidTo = note.ValidTo,
            IssuedAt = note.IssuedAt,
            Status = note.Status.ToString(),
            NoteNumber = note.NoteNumber,
            IncludeSummaryInPublicView = note.IncludeSummaryInPublicView,
            ClinicName = note.ClinicName,
            RevokedAt = note.RevokedAt,
            RevocationReason = note.RevocationReason,
            IsExpired = note.Status == DoctorNoteStatus.Expired || note.ValidTo < DateTime.UtcNow
        };
    }

    #endregion
}

#region Request/Response DTOs

public class CreateDoctorNoteRequest
{
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public required string Title { get; set; }
    public string? Summary { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool IncludeSummaryInPublicView { get; set; } = false;
    public string? ClinicName { get; set; }
}

public class CreateDoctorNoteResponse
{
    public Guid NoteId { get; set; }
    public required string NoteNumber { get; set; }
    public required string PublicToken { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public DateTime IssuedAt { get; set; }
    public required string Message { get; set; }
}

public class RevokeNoteRequest
{
    public string? Reason { get; set; }
}

public class PatientTokenResponse
{
    public required string PublicToken { get; set; }
}

public class DoctorNoteDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public required string PatientName { get; set; }
    public Guid DoctorId { get; set; }
    public required string DoctorName { get; set; }
    public Guid? AppointmentId { get; set; }
    public required string Title { get; set; }
    public string? Summary { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public DateTime IssuedAt { get; set; }
    public required string Status { get; set; }
    public required string NoteNumber { get; set; }
    public bool IncludeSummaryInPublicView { get; set; }
    public string? ClinicName { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public bool IsExpired { get; set; }
}

/// <summary>
/// Minimal public DTO for anonymous verification (no sensitive data)
/// </summary>
public class DoctorNotePublicDto
{
    public required string NoteNumber { get; set; }
    public required string DoctorName { get; set; }
    public string? DoctorLicenseNumber { get; set; }
    public required string PatientName { get; set; } // Initials or full name based on privacy setting
    public string? ClinicName { get; set; }
    public required string Title { get; set; }
    public string? Summary { get; set; } // Only if IncludeSummaryInPublicView = true
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public DateTime IssuedAt { get; set; }
    public required string Status { get; set; }
    public bool IsValid { get; set; }
}

#endregion
