using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class ReferralService(
    PharmacyApiDbContext context,
    INotificationService notifications,
    IAuditService audit,
    ILogger<ReferralService> logger) : IReferralService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly INotificationService _notifications = notifications;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<ReferralService> _logger = logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Create
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Referral> CreateAsync(Referral referral, List<MedicalTestType> orderedTests)
    {
        referral.Id = Guid.NewGuid();
        referral.CreatedAt = DateTime.UtcNow;
        referral.UpdatedAt = DateTime.UtcNow;
        referral.Status = ReferralStatus.Pending;

        _context.Referrals.Add(referral);

        // Order medical tests — always link PatientId for cross-referral queries
        foreach (var testType in orderedTests)
        {
            _context.MedicalTests.Add(new MedicalTest
            {
                Id = Guid.NewGuid(),
                PatientId = referral.PatientId,
                ReferralId = referral.Id,
                OrderedByDoctorId = referral.ReferringDoctorId,
                TestType = testType,
                IsEmergency = referral.Origin == ReferralOrigin.Emergency,
                Status = MedicalTestStatus.Ordered,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // If a specific doctor was assigned up-front, auto-create a shell appointment
        if (referral.ReferredToDoctorId.HasValue && !referral.IsExternal)
        {
            await CreateShellAppointmentAsync(referral);
        }

        await _context.SaveChangesAsync();

        // Notify patient
        await TrySendNotificationAsync(
            referral.PatientId,
            "New Referral",
            $"Your doctor has referred you to a {referral.ReferredToSpecialty} specialist.",
            new Dictionary<string, string>
            {
                ["referralId"] = referral.Id.ToString(),
                ["type"] = "referral_created"
            });

        // Notify referred doctor (if already assigned)
        if (referral.ReferredToDoctorId.HasValue)
        {
            await TrySendNotificationAsync(
                referral.ReferredToDoctorId.Value,
                "New Patient Referral",
                "A patient has been referred to you.",
                new Dictionary<string, string>
                {
                    ["referralId"] = referral.Id.ToString(),
                    ["type"] = "referral_received"
                });
        }

        await _audit.LogEntityActionAsync(
            "Create", "Referral", referral.Id.ToString(), referral.ReferringDoctorId,
            $"Referral created for patient {referral.PatientId} → specialty {referral.ReferredToSpecialty}");

        _logger.LogInformation("Referral {ReferralId} created by doctor {DoctorId}", referral.Id, referral.ReferringDoctorId);
        return referral;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Read
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Referral?> GetByIdAsync(Guid id) =>
        await _context.Referrals
            .Include(r => r.ReferringDoctor)
            .Include(r => r.ReferredToDoctor)
            .Include(r => r.Patient)
            .Include(r => r.SourceAppointment)
            .Include(r => r.ResultAppointment)
            .Include(r => r.Tests)
            .FirstOrDefaultAsync(r => r.Id == id);

    public async Task<List<Referral>> GetByPatientAsync(Guid patientId) =>
        await _context.Referrals
            .Include(r => r.ReferringDoctor)
            .Include(r => r.ReferredToDoctor)
            .Include(r => r.ResultAppointment)
            .Include(r => r.Tests)
            .Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

    public async Task<List<Referral>> GetByReferringDoctorAsync(Guid doctorId) =>
        await _context.Referrals
            .Include(r => r.Patient)
            .Include(r => r.ReferredToDoctor)
            .Include(r => r.ResultAppointment)
            .Include(r => r.Tests)
            .Where(r => r.ReferringDoctorId == doctorId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

    public async Task<List<Referral>> GetByReferredDoctorAsync(Guid doctorId) =>
        await _context.Referrals
            .Include(r => r.Patient)
            .Include(r => r.ReferringDoctor)
            .Include(r => r.ResultAppointment)
            .Include(r => r.Tests)
            .Where(r => r.ReferredToDoctorId == doctorId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

    // ─────────────────────────────────────────────────────────────────────────
    // Status transitions
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Referral> AssignDoctorAsync(Guid referralId, Guid doctorId)
    {
        var referral = await RequireAsync(referralId);

        // Validate the doctor has the right specialty
        var profile = await _context.DoctorProfiles
            .FirstOrDefaultAsync(dp => dp.UserId == doctorId)
            ?? throw new InvalidOperationException("Doctor profile not found.");

        if (profile.Specialization != referral.ReferredToSpecialty)
            throw new InvalidOperationException(
                $"Doctor's specialization ({profile.Specialization}) does not match referral specialty ({referral.ReferredToSpecialty}).");

        referral.ReferredToDoctorId = doctorId;
        referral.UpdatedAt = DateTime.UtcNow;

        // Create shell appointment now that we have a doctor
        if (!referral.IsExternal && referral.ResultAppointmentId is null)
            await CreateShellAppointmentAsync(referral);

        await _context.SaveChangesAsync();

        // Notify the newly assigned doctor
        await TrySendNotificationAsync(
            doctorId,
            "New Patient Referral",
            "A patient has selected you for a referral appointment.",
            new Dictionary<string, string>
            {
                ["referralId"] = referral.Id.ToString(),
                ["type"] = "referral_received"
            });

        return referral;
    }

    public async Task<Referral> AcceptAsync(Guid referralId, Guid acceptingDoctorId)
    {
        var referral = await RequireAsync(referralId);
        referral.Status = ReferralStatus.Accepted;
        referral.UpdatedAt = DateTime.UtcNow;

        // Confirm the shell appointment so the doctor's list reflects acceptance
        if (referral.ResultAppointmentId.HasValue)
        {
            var shellAppt = await _context.Appointments.FindAsync(referral.ResultAppointmentId.Value);
            if (shellAppt is not null && shellAppt.Status == AppointmentStatus.Scheduled)
            {
                shellAppt.Status = AppointmentStatus.Confirmed;
                shellAppt.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        await TrySendNotificationAsync(
            referral.PatientId,
            "Referral Accepted",
            "Your referral appointment has been accepted by the specialist.",
            new Dictionary<string, string> { ["referralId"] = referral.Id.ToString(), ["type"] = "referral_accepted" });

        return referral;
    }

    public async Task<Referral> DeclineAsync(Guid referralId, string? reason = null)
    {
        var referral = await RequireAsync(referralId);

        // Store the decline reason in ResultNotes before clearing it for reassignment
        if (reason is not null)
            referral.ResultNotes = reason;

        // Reset back to Pending so the patient can pick a different doctor
        referral.Status = ReferralStatus.Pending;
        referral.ReferredToDoctorId = null;
        referral.UpdatedAt = DateTime.UtcNow;

        // Cancel the shell appointment that was created for the declined doctor
        if (referral.ResultAppointmentId.HasValue)
        {
            var shellAppt = await _context.Appointments.FindAsync(referral.ResultAppointmentId.Value);
            if (shellAppt is not null && shellAppt.Status == AppointmentStatus.Scheduled)
            {
                shellAppt.Status = AppointmentStatus.Cancelled;
                shellAppt.UpdatedAt = DateTime.UtcNow;
            }
            referral.ResultAppointmentId = null;
        }

        await _context.SaveChangesAsync();

        await TrySendNotificationAsync(
            referral.PatientId,
            "Referral Declined",
            "A specialist has declined your referral. Please choose another doctor.",
            new Dictionary<string, string> { ["referralId"] = referral.Id.ToString(), ["type"] = "referral_declined" });

        return referral;
    }

    public async Task<Referral> CompleteAsync(Guid referralId, string resultNotes)
    {
        var referral = await RequireAsync(referralId);
        referral.Status = ReferralStatus.Completed;
        referral.ResultNotes = resultNotes;
        referral.CompletedAt = DateTime.UtcNow;
        referral.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await TrySendNotificationAsync(
            referral.PatientId,
            "Referral Complete",
            "Your specialist has sent the referral results to your referring doctor.",
            new Dictionary<string, string> { ["referralId"] = referral.Id.ToString(), ["type"] = "referral_completed" });

        await TrySendNotificationAsync(
            referral.ReferringDoctorId,
            "Referral Results Available",
            "A referral you sent has been completed by the specialist.",
            new Dictionary<string, string> { ["referralId"] = referral.Id.ToString(), ["type"] = "referral_results" });

        return referral;
    }

    public async Task<Referral> CancelAsync(Guid referralId)
    {
        var referral = await RequireAsync(referralId);
        referral.Status = ReferralStatus.Cancelled;
        referral.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return referral;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<ReferralAvailableDoctorsDto> GetAvailableDoctorsAsync(Guid referralId)
    {
        var referral = await RequireAsync(referralId);

        // Count ALL doctors with the target specialty (regardless of capacity)
        // so callers know whether the specialty itself is absent vs just full.
        var allInSpecialty = await _context.DoctorProfiles
            .Where(dp => dp.Specialization == referral.ReferredToSpecialty && dp.User.IsActive)
            .CountAsync();

        // Doctors that are actually available for new referrals
        var available = await _context.DoctorProfiles
            .Include(dp => dp.User)
            .Where(dp => dp.Specialization == referral.ReferredToSpecialty
                      && dp.User.IsActive
                      && dp.AcceptingNewPatients)
            .Select(dp => new AvailableDoctorDto
            {
                Id             = dp.UserId,
                FirstName      = dp.User.FirstName,
                LastName       = dp.User.LastName,
                Specialization = dp.Specialization.ToString()
            })
            .ToListAsync();

        return new ReferralAvailableDoctorsDto
        {
            Doctors          = available,
            TotalInSpecialty = allInSpecialty,
            Specialty        = referral.ReferredToSpecialty.ToString()
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Referral chain — forward to another specialist
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<Referral> ForwardAsync(
        Guid referralId,
        Guid forwardingDoctorId,
        Guid? targetDoctorId,
        MedicalSpecialization specialty,
        string reason)
    {
        var parent = await RequireAsync(referralId);

        // Only the currently assigned / receiving doctor can forward
        if (parent.ReferredToDoctorId != forwardingDoctorId)
            throw new UnauthorizedAccessException("Only the receiving doctor may forward a referral.");

        if (parent.Status is ReferralStatus.Completed or ReferralStatus.Cancelled)
            throw new InvalidOperationException($"Cannot forward a referral in status {parent.Status}.");

        // ── Circular referral guard ────────────────────────────────────────
        // Walk the ParentReferralId chain upward, collecting every doctor who
        // has already appeared in the chain (both referring and referred-to).
        // Reject if the proposed targetDoctorId is already in that set — this
        // prevents infinite referral loops (A→B→C→A) and self-referrals.
        if (targetDoctorId.HasValue)
        {
            var doctorsInChain = new HashSet<Guid>();
            var cursor = parent;

            while (cursor != null)
            {
                if (cursor.ReferringDoctorId != Guid.Empty)
                    doctorsInChain.Add(cursor.ReferringDoctorId);
                if (cursor.ReferredToDoctorId.HasValue)
                    doctorsInChain.Add(cursor.ReferredToDoctorId.Value);

                cursor = cursor.ParentReferralId.HasValue
                    ? await _context.Referrals.FindAsync(cursor.ParentReferralId.Value)
                    : null;
            }

            if (doctorsInChain.Contains(targetDoctorId.Value))
                throw new InvalidOperationException(
                    "Cannot forward to a doctor who is already part of this referral chain. " +
                    "This would create a circular referral loop.");
        }

        // Mark the parent as completed (the forwarding doctor has seen the patient)
        parent.Status = ReferralStatus.Completed;
        parent.CompletedAt = DateTime.UtcNow;
        parent.UpdatedAt = DateTime.UtcNow;

        // Create the child referral
        var child = new Referral
        {
            Id = Guid.NewGuid(),
            ParentReferralId = parent.Id,
            Origin = ReferralOrigin.Internal,
            ReferringDoctorId = forwardingDoctorId,
            PatientId = parent.PatientId,
            ReferredToSpecialty = specialty,
            ReferredToDoctorId = targetDoctorId,
            Reason = reason,
            Urgency = parent.Urgency,
            Status = ReferralStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Referrals.Add(child);

        // Auto-create shell appointment if a target doctor was specified
        if (targetDoctorId.HasValue)
            await CreateShellAppointmentAsync(child);

        await _context.SaveChangesAsync();

        // Notify patient
        await TrySendNotificationAsync(
            parent.PatientId,
            "Referral Forwarded",
            $"Your specialist has referred you on to a {specialty} doctor.",
            new Dictionary<string, string>
            {
                ["referralId"] = child.Id.ToString(),
                ["parentReferralId"] = parent.Id.ToString(),
                ["type"] = "referral_forwarded"
            });

        // Notify the newly assigned doctor (if one was specified)
        if (targetDoctorId.HasValue)
        {
            await TrySendNotificationAsync(
                targetDoctorId.Value,
                "New Patient Referral",
                "A patient has been referred to you from another specialist.",
                new Dictionary<string, string>
                {
                    ["referralId"] = child.Id.ToString(),
                    ["type"] = "referral_received"
                });
        }

        _logger.LogInformation(
            "Referral {ParentId} forwarded to new referral {ChildId} by doctor {DoctorId}",
            parent.Id, child.Id, forwardingDoctorId);

        return child;
    }

    public async Task DeleteAsync(Guid id)
    {
        var referral = await RequireAsync(id);
        _context.Referrals.Remove(referral);
        await _context.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task CreateShellAppointmentAsync(Referral referral)
    {
        // Find the doctor's primary hospital from their profile HospitalIds list
        var doctorProfile = await _context.DoctorProfiles
            .FirstOrDefaultAsync(dp => dp.UserId == referral.ReferredToDoctorId!.Value);

        Guid hospitalId = Guid.Empty;
        if (doctorProfile?.HospitalIds.Count > 0)
            hospitalId = doctorProfile.HospitalIds[0];
        else
            hospitalId = await _context.Hospitals.Select(h => h.Id).FirstOrDefaultAsync();

        // Shell appointment — placeholder date 30 days out.
        // The patient MUST reschedule; a notification is sent immediately.
        var placeholder = DateTime.UtcNow.AddDays(30);
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            PatientId = referral.PatientId,
            DoctorId = referral.ReferredToDoctorId!.Value,
            HospitalId = hospitalId,
            ScheduledAt = placeholder,
            DurationMinutes = 30,
            Status = AppointmentStatus.Scheduled,
            Reason = $"Referral: {referral.Reason}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ReferralId = referral.Id
        };

        _context.Appointments.Add(appointment);
        referral.ResultAppointmentId = appointment.Id;
        referral.Status = ReferralStatus.Scheduled;

        // Notify the patient that a placeholder appointment was created and needs rescheduling
        await TrySendNotificationAsync(
            referral.PatientId,
            "Appointment Needs Scheduling",
            "A referral appointment has been provisionally booked. Please open the app to choose a convenient date and time.",
            new Dictionary<string, string>
            {
                ["appointmentId"] = appointment.Id.ToString(),
                ["referralId"] = referral.Id.ToString(),
                ["type"] = "appointment_reschedule_required"
            });
    }

    private async Task TrySendNotificationAsync(
        Guid userId, string title, string body, Dictionary<string, string>? data = null)
    {
        try
        {
            await _notifications.SendPushNotificationAsync(userId, title, body, data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push notification failed for user {UserId}", userId);
        }
    }

    private async Task<Referral> RequireAsync(Guid id) =>
        await _context.Referrals
            .Include(r => r.ReferringDoctor)
            .Include(r => r.Patient)
            .FirstOrDefaultAsync(r => r.Id == id)
        ?? throw new KeyNotFoundException($"Referral {id} not found.");
}
