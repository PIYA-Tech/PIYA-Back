using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class DoctorNoteService(
    PharmacyApiDbContext context,
    IAuditService auditService,
    ILogger<DoctorNoteService> logger) : IDoctorNoteService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly ILogger<DoctorNoteService> _logger = logger;

    public async Task<(DoctorNote Note, string PublicToken)> CreateNoteAsync(DoctorNote note)
    {
        note.Id = Guid.NewGuid();
        note.NoteNumber = GenerateNoteNumber();
        note.Status = DoctorNoteStatus.Active;
        note.IssuedAt = DateTime.UtcNow;
        note.CreatedAt = DateTime.UtcNow;
        note.UpdatedAt = DateTime.UtcNow;

        // Generate 32-byte random token for public verification
        var publicToken = GenerateSecureToken();
        note.PublicTokenHash = HashToken(publicToken);

        _context.DoctorNotes.Add(note);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "CreateDoctorNote",
            "DoctorNote",
            note.Id.ToString(),
            note.DoctorId,
            $"Doctor note {note.NoteNumber} created for patient {note.PatientId}"
        );

        return (note, publicToken);
    }

    public async Task<DoctorNote?> GetByIdAsync(Guid id)
    {
        return await _context.DoctorNotes
            .Include(n => n.Patient)
            .Include(n => n.Doctor)
            .Include(n => n.Appointment)
            .FirstOrDefaultAsync(n => n.Id == id);
    }

    public async Task<List<DoctorNote>> GetPatientNotesAsync(Guid patientId)
    {
        return await _context.DoctorNotes
            .Include(n => n.Patient)
            .Include(n => n.Doctor)
            .Include(n => n.Appointment)
            .Where(n => n.PatientId == patientId)
            .OrderByDescending(n => n.IssuedAt)
            .ToListAsync();
    }

    public async Task<List<DoctorNote>> GetDoctorNotesAsync(Guid doctorId)
    {
        return await _context.DoctorNotes
            .Include(n => n.Patient)
            .Include(n => n.Doctor)
            .Include(n => n.Appointment)
            .Where(n => n.DoctorId == doctorId)
            .OrderByDescending(n => n.IssuedAt)
            .ToListAsync();
    }

    public async Task<DoctorNote> RevokeNoteAsync(Guid id, string? reason)
    {
        var note = await GetByIdAsync(id) ?? throw new InvalidOperationException("Doctor note not found");
        if (note.Status == DoctorNoteStatus.Revoked)
        {
            throw new InvalidOperationException("Doctor note is already revoked");
        }

        note.Status = DoctorNoteStatus.Revoked;
        note.RevokedAt = DateTime.UtcNow;
        note.RevocationReason = reason;
        note.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "RevokeDoctorNote",
            "DoctorNote",
            id.ToString(),
            note.DoctorId,
            $"Doctor note {note.NoteNumber} revoked: {reason}"
        );

        return note;
    }

    public async Task<DoctorNote?> VerifyPublicTokenAsync(string publicToken)
    {
        var tokenHash = HashToken(publicToken);

        var note = await _context.DoctorNotes
            .Include(n => n.Patient)
            .Include(n => n.Doctor)
            .FirstOrDefaultAsync(n => n.PublicTokenHash == tokenHash);

        if (note == null)
        {
            _logger.LogWarning("Invalid public token used for verification");
            return null;
        }

        // Check if note is expired or revoked
        if (note.Status != DoctorNoteStatus.Active)
        {
            _logger.LogWarning($"Attempted to verify {note.Status.ToString().ToLower()} doctor note: {note.NoteNumber}");
            return note; // Return note with status info
        }

        if (await IsNoteExpiredAsync(note.Id))
        {
            note.Status = DoctorNoteStatus.Expired;
            note.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return note;
        }

        await _auditService.LogActionAsync(
            "VerifyDoctorNote",
            null,
            $"Doctor note {note.NoteNumber} verified via public token"
        );

        return note;
    }

    public async Task<string> RegeneratePublicTokenAsync(Guid noteId)
    {
        var note = await _context.DoctorNotes.FindAsync(noteId)
            ?? throw new InvalidOperationException("Doctor note not found");

        var newToken = GenerateSecureToken();
        note.PublicTokenHash = HashToken(newToken);
        note.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return newToken;
    }

    public bool IsNoteExpired(DoctorNote note)
    {
        return DateTime.UtcNow > note.ValidTo;
    }

    private async Task<bool> IsNoteExpiredAsync(Guid id)
    {
        var note = await _context.DoctorNotes.FindAsync(id);
        if (note == null)
        {
            return true;
        }

        return DateTime.UtcNow > note.ValidTo;
    }

    public async Task<List<DoctorNote>> GetExpiringSoonAsync(int daysThreshold = 7)
    {
        var thresholdDate = DateTime.UtcNow.AddDays(daysThreshold);

        return await _context.DoctorNotes
            .Include(n => n.Patient)
            .Include(n => n.Doctor)
            .Where(n => n.Status == DoctorNoteStatus.Active)
            .Where(n => n.ValidTo <= thresholdDate && n.ValidTo > DateTime.UtcNow)
            .OrderBy(n => n.ValidTo)
            .ToListAsync();
    }

    public string GenerateNoteNumber()
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"DN-{year}-";

        // Get last note number for this year (sync version for synchronous context)
        var lastNote = _context.DoctorNotes
            .Where(n => n.NoteNumber.StartsWith(prefix))
            .OrderByDescending(n => n.NoteNumber)
            .FirstOrDefault();

        int nextSequence = 1;

        if (lastNote != null)
        {
            // Extract sequence number from last note
            var parts = lastNote.NoteNumber.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out int lastSequence))
            {
                nextSequence = lastSequence + 1;
            }
        }

        return $"{prefix}{nextSequence:D6}"; // e.g., DN-2026-000001
    }

    private string GenerateSecureToken()
    {
        // Generate 32-byte random token, encoded as URL-safe base64 (no +, /, or = padding)
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private string HashToken(string token)
    {
        // Normalise URL-safe base64 back to standard base64 before hashing
        var normalized = token
            .Replace('-', '+')
            .Replace('_', '/');
        // Re-add padding if needed
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
        }
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
