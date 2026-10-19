using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using System.Globalization;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class AppointmentService(PharmacyApiDbContext context, IAuditService auditService, IEmailService emailService, ILogger<AppointmentService> logger) : IAppointmentService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger<AppointmentService> _logger = logger;

    private IQueryable<Appointment> QueryAppointments(bool asNoTracking = false)
    {
        var query = _context.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Hospital);

        return asNoTracking ? query.AsNoTracking() : query;
    }

    public async Task<Appointment> BookAppointmentAsync(Appointment appointment)
    {
        // Reject appointments scheduled in the past.
        // A small tolerance of 1 minute is allowed to absorb clock-skew between client and server.
        if (appointment.ScheduledAt < DateTime.UtcNow.AddMinutes(-1))
            throw new ArgumentException("Appointment cannot be scheduled in the past.");

        // Wrap the availability check and INSERT in a serializable transaction so that
        // two concurrent booking requests for the same slot cannot both pass the check
        // before either has committed — eliminating the TOCTOU double-booking race.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            try
            {
                await ValidateBookingParticipantsAsync(appointment);
                var isAvailable = await IsDoctorAvailableAsync(
                    appointment.DoctorId,
                    appointment.ScheduledAt,
                    appointment.DurationMinutes);

                if (!isAvailable)
                    throw new InvalidOperationException("Doctor is not available at the specified time");

                appointment.Id = Guid.NewGuid();
                appointment.Status = AppointmentStatus.Scheduled;
                appointment.CreatedAt = DateTime.UtcNow;
                appointment.UpdatedAt = DateTime.UtcNow;

                _context.Appointments.Add(appointment);
                await SaveAppointmentChangesAsync();

                // Audit log is inside the transaction so a logging failure rolls back
                // the booking rather than leaving a booked-but-unaudited appointment.
                await _auditService.LogEntityActionAsync(
                    "BookAppointment",
                    "Appointment",
                    appointment.Id.ToString(),
                    appointment.PatientId,
                    $"Appointment booked with Dr. {appointment.DoctorId} for {appointment.ScheduledAt}"
                );

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                if (IsSchedulingConflict(ex)) throw new InvalidOperationException("The schedule changed. Refresh availability and try again.", ex);
                throw;
            }

            return appointment;
        });
    }

    private async Task ValidateBookingParticipantsAsync(Appointment appointment)
    {
        if (appointment.PatientId == Guid.Empty || appointment.DoctorId == Guid.Empty || appointment.HospitalId == Guid.Empty)
            throw new ArgumentException("Patient, doctor and hospital are required.");
        if (appointment.PatientId == appointment.DoctorId)
            throw new ArgumentException("The patient and treating doctor must be different people.");
        if (appointment.DurationMinutes < 5 || appointment.DurationMinutes > 240)
            throw new ArgumentException("Appointment duration must be between 5 and 240 minutes.");
        if (!await _context.Users.AnyAsync(u => u.Id == appointment.PatientId && u.IsActive))
            throw new KeyNotFoundException("The patient account is unavailable.");
        if (!await _context.Set<Hospital>().AnyAsync(h => h.Id == appointment.HospitalId && h.IsActive))
            throw new KeyNotFoundException("This hospital is not currently accepting appointments.");
        var profile = await _context.DoctorProfiles.AsNoTracking()
            .Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == appointment.DoctorId);
        if (profile is null || !profile.User.IsActive || profile.User.Role != UserRole.Doctor)
            throw new KeyNotFoundException("The doctor is unavailable.");
        if (!profile.HospitalIds.Contains(appointment.HospitalId))
            throw new ArgumentException("The doctor is not affiliated with the selected hospital.");
        if (string.IsNullOrWhiteSpace(profile.LicenseNumber) || profile.LicenseExpiryDate < appointment.ScheduledAt)
            throw new ArgumentException("The doctor's registration does not cover the appointment date.");
        if (!profile.AcceptingNewPatients && !await _context.Appointments.AnyAsync(a =>
                a.DoctorId == appointment.DoctorId && a.PatientId == appointment.PatientId && a.Status == AppointmentStatus.Completed))
            throw new InvalidOperationException("The doctor is not accepting new patients.");
    }

    private static bool IsSchedulingConflict(Exception error) => error is DbUpdateConcurrencyException
        || error is PostgresException { SqlState: "40001" or "40P01" }
        || (error.InnerException is not null && IsSchedulingConflict(error.InnerException));

    private async Task SaveAppointmentChangesAsync()
    {
        try { await _context.SaveChangesAsync(); }
        catch (Exception ex) when (IsSchedulingConflict(ex))
        { throw new InvalidOperationException("The appointment changed. Refresh and try again.", ex); }
    }

    private static void ValidateTransition(AppointmentStatus current, AppointmentStatus next)
    {
        var allowed = current switch {
            AppointmentStatus.Scheduled => next is AppointmentStatus.Confirmed or AppointmentStatus.InProgress or AppointmentStatus.Cancelled or AppointmentStatus.NoShow,
            AppointmentStatus.Confirmed => next is AppointmentStatus.InProgress or AppointmentStatus.Cancelled or AppointmentStatus.NoShow,
            AppointmentStatus.InProgress => next is AppointmentStatus.Completed,
            _ => false,
        };
        if (!allowed) throw new InvalidOperationException($"Cannot change a {current} appointment to {next}.");
    }

    private async Task<bool> IsWithinWorkingHoursAsync(Guid doctorId, DateTime scheduledAt, int durationMinutes)
    {
        if (durationMinutes is < 5 or > 240) return false;
        var profile = await _context.DoctorProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == doctorId);
        if (profile is null) return false;
        // Unconfigured legacy profiles retain their existing booking behavior. An
        // explicitly configured empty schedule means there are no available shifts.
        if (string.IsNullOrWhiteSpace(profile.WorkingHours)) return true;
        List<WorkingHoursSlot>? hours;
        try { hours = JsonSerializer.Deserialize<List<WorkingHoursSlot>>(profile.WorkingHours,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { return false; }
        if (hours is null) return false;
        var utc = scheduledAt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(scheduledAt, DateTimeKind.Utc) : scheduledAt.ToUniversalTime();
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku"));
        var end = local.AddMinutes(durationMinutes);
        if (end.Date != local.Date) return false;
        return hours.Where(day => string.Equals(day.DayOfWeek, local.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase))
            .SelectMany(day => day.Slots ?? [])
            .Any(slot => TimeOnly.TryParseExact(slot.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                && TimeOnly.TryParseExact(slot.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var finish)
                && start < finish && TimeOnly.FromDateTime(local) >= start && TimeOnly.FromDateTime(end) <= finish);
    }

    public async Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await QueryAppointments(asNoTracking: true)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    private async Task<Appointment> GetTrackedByIdAsync(Guid id)
    {
        return await QueryAppointments()
            .FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new InvalidOperationException("Appointment not found");
    }

    public async Task<List<Appointment>> GetPatientAppointmentsAsync(Guid patientId, AppointmentStatus? status = null, CancellationToken ct = default)
    {
        var query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor)
            .Include(a => a.Hospital)
            .Where(a => a.PatientId == patientId);

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        return await query
            .OrderByDescending(a => a.ScheduledAt)
            .ToListAsync(ct);
    }

    public async Task<List<Appointment>> GetDoctorAppointmentsAsync(Guid doctorId, DateTime? date = null, AppointmentStatus? status = null, CancellationToken ct = default)
    {
        var query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Hospital)
            .Where(a => a.DoctorId == doctorId);

        if (date.HasValue)
        {
            var startOfDay = date.Value.Date;
            var endOfDay = startOfDay.AddDays(1);
            query = query.Where(a => a.ScheduledAt >= startOfDay && a.ScheduledAt < endOfDay);
        }

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        return await query
            .OrderBy(a => a.ScheduledAt)
            .ToListAsync(ct);
    }

    public async Task<bool> IsDoctorAvailableAsync(Guid doctorId, DateTime scheduledAt, int durationMinutes = 30)
    {
        if (!await IsWithinWorkingHoursAsync(doctorId, scheduledAt, durationMinutes)) return false;
        var endTime = scheduledAt.AddMinutes(durationMinutes);

        var conflict = await _context.Appointments
            .Where(a => a.DoctorId == doctorId)
            .Where(a => a.Status == AppointmentStatus.Scheduled
                     || a.Status == AppointmentStatus.Confirmed
                     || a.Status == AppointmentStatus.InProgress)
            .Where(a =>
                (a.ScheduledAt < endTime && a.ScheduledAt.AddMinutes(a.DurationMinutes) > scheduledAt)
            )
            .AnyAsync();

        return !conflict;
    }

    public async Task<Appointment> UpdateStatusAsync(Guid id, AppointmentStatus status, string? reason = null)
    {
        var appointment = await GetTrackedByIdAsync(id);
        ValidateTransition(appointment.Status, status);
        appointment.Status = status;
        appointment.UpdatedAt = DateTime.UtcNow;

        if (status == AppointmentStatus.InProgress)
        {
            appointment.ActualStartTime = DateTime.UtcNow;
        }
        else if (status == AppointmentStatus.Completed)
        {
            appointment.ActualEndTime = DateTime.UtcNow;
        }

        await SaveAppointmentChangesAsync();

        await _auditService.LogEntityActionAsync(
            "UpdateAppointmentStatus",
            "Appointment",
            id.ToString(),
            appointment.PatientId,
            $"Status updated to {status}"
        );

        return appointment;
    }

    public async Task<Appointment> CancelAppointmentAsync(Guid id, Guid cancelledBy, string? reason)
    {
        var appointment = await GetTrackedByIdAsync(id);
        ValidateTransition(appointment.Status, AppointmentStatus.Cancelled);
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancellationReason = reason;
        appointment.CancelledBy = cancelledBy;
        appointment.CancelledAt = DateTime.UtcNow;
        appointment.UpdatedAt = DateTime.UtcNow;

        await SaveAppointmentChangesAsync();

        await _auditService.LogEntityActionAsync(
            "CancelAppointment",
            "Appointment",
            id.ToString(),
            cancelledBy,
            $"Appointment cancelled: {reason}"
        );

        // Notify the patient by email when someone else (doctor/admin) cancels
        try
        {
            var wasDoctor = cancelledBy != appointment.PatientId;
            if (wasDoctor && appointment.Patient?.Email != null)
            {
                var patientName  = $"{appointment.Patient.FirstName} {appointment.Patient.LastName}".Trim();
                var doctorName   = appointment.Doctor != null
                    ? $"Dr. {appointment.Doctor.FirstName} {appointment.Doctor.LastName}".Trim()
                    : "your doctor";
                var cancelledByLabel = wasDoctor ? doctorName : "you";

                await _emailService.SendAppointmentCancelledAsync(
                    appointment.Patient.Email,
                    patientName,
                    appointment.ScheduledAt,
                    doctorName,
                    cancelledByLabel,
                    reason
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send cancellation email for appointment {AppointmentId}", id);
        }

        return appointment;
    }

    public async Task<Appointment> RescheduleAppointmentAsync(Guid id, DateTime newScheduledAt)
    {
        if (newScheduledAt < DateTime.UtcNow.AddMinutes(-1))
            throw new ArgumentException("Cannot reschedule an appointment to a time in the past.");

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => {
            try { return await RescheduleCoreAsync(id, newScheduledAt); }
            catch (Exception ex) when (IsSchedulingConflict(ex))
            { throw new InvalidOperationException("The schedule changed. Refresh availability and try again.", ex); }
        });
    }

    private async Task<Appointment> RescheduleCoreAsync(Guid id, DateTime newScheduledAt)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var appointment = await GetTrackedByIdAsync(id);

        if (appointment.Status is not (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed) || appointment.RescheduledToId.HasValue)
            throw new InvalidOperationException($"Cannot reschedule a {appointment.Status} appointment");

        await ValidateBookingParticipantsAsync(new Appointment {
            PatientId = appointment.PatientId, DoctorId = appointment.DoctorId, HospitalId = appointment.HospitalId,
            ScheduledAt = newScheduledAt, DurationMinutes = appointment.DurationMinutes });
        if (!await IsWithinWorkingHoursAsync(appointment.DoctorId, newScheduledAt, appointment.DurationMinutes))
            throw new InvalidOperationException("The requested time is outside the doctor's configured working hours.");

        // Check if new time is available (exclude this appointment from its own conflict check)
        var endTime = newScheduledAt.AddMinutes(appointment.DurationMinutes);
        var conflict = await _context.Appointments
            .Where(a => a.DoctorId == appointment.DoctorId && a.Id != id)
            .Where(a => a.Status == AppointmentStatus.Scheduled
                     || a.Status == AppointmentStatus.Confirmed
                     || a.Status == AppointmentStatus.InProgress)
            .Where(a => a.ScheduledAt < endTime && a.ScheduledAt.AddMinutes(a.DurationMinutes) > newScheduledAt)
            .AnyAsync();

        if (conflict)
        {
            throw new InvalidOperationException("Doctor is not available at the new time");
        }

        var oldTime = appointment.ScheduledAt;

        // Create a new appointment row for the rescheduled slot.
        // The original row is kept as an immutable audit record with Status = Rescheduled
        // and RescheduledToId pointing at the replacement so clients can follow the chain.
        var newAppointment = new Appointment
        {
            Id            = Guid.NewGuid(),
            PatientId     = appointment.PatientId,
            DoctorId      = appointment.DoctorId,
            HospitalId    = appointment.HospitalId,
            ScheduledAt   = newScheduledAt,
            DurationMinutes = appointment.DurationMinutes,
            Status        = AppointmentStatus.Scheduled,
            Reason        = appointment.Reason,
            ReferralId    = appointment.ReferralId,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow
        };

        _context.Appointments.Add(newAppointment);

        // Seal the old appointment: terminal status + forward link
        appointment.Status = AppointmentStatus.Rescheduled;
        appointment.RescheduledToId = newAppointment.Id;
        appointment.UpdatedAt = DateTime.UtcNow;

        await SaveAppointmentChangesAsync();

        await _auditService.LogEntityActionAsync(
            "RescheduleAppointment",
            "Appointment",
            id.ToString(),
            appointment.PatientId,
            $"Rescheduled from {oldTime} to {newScheduledAt}; new appointment id={newAppointment.Id}"
        );

        await tx.CommitAsync();

        // Notify only after committing, so rolled-back bookings never send mail.
        try
        {
            if (appointment.Patient?.Email != null)
            {
                var patientName = $"{appointment.Patient.FirstName} {appointment.Patient.LastName}".Trim();
                var doctorName  = appointment.Doctor != null
                    ? $"Dr. {appointment.Doctor.FirstName} {appointment.Doctor.LastName}".Trim()
                    : "your doctor";

                await _emailService.SendAppointmentRescheduledAsync(
                    appointment.Patient.Email,
                    patientName,
                    oldTime,
                    newScheduledAt,
                    doctorName
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send reschedule email for appointment {AppointmentId}", id);
        }

        // Return the new (active) appointment so the caller/client gets the usable booking
        return newAppointment;
    }

    public async Task<Appointment> CompleteAppointmentAsync(Guid id, string? doctorNotes)
    {
        var appointment = await GetTrackedByIdAsync(id);
        ValidateTransition(appointment.Status, AppointmentStatus.Completed);
        appointment.Status = AppointmentStatus.Completed;
        appointment.AppointmentNotes = doctorNotes;
        appointment.ActualEndTime = DateTime.UtcNow;
        appointment.UpdatedAt = DateTime.UtcNow;

        await SaveAppointmentChangesAsync();

        await _auditService.LogEntityActionAsync(
            "CompleteAppointment",
            "Appointment",
            id.ToString(),
            appointment.DoctorId,
            "Appointment completed"
        );

        return appointment;
    }

    public async Task<List<Appointment>> GetHospitalAppointmentsAsync(Guid hospitalId, DateTime? date = null)
    {
        var query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Where(a => a.HospitalId == hospitalId);

        if (date.HasValue)
        {
            var startOfDay = date.Value.Date;
            var endOfDay = startOfDay.AddDays(1);
            query = query.Where(a => a.ScheduledAt >= startOfDay && a.ScheduledAt < endOfDay);
        }

        return await query
            .OrderBy(a => a.ScheduledAt)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetAllAppointmentsAsync(
        Guid? hospitalId = null,
        Guid? doctorId = null,
        Guid? patientId = null,
        AppointmentStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = _context.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Include(a => a.Hospital)
            .AsQueryable();

        if (hospitalId.HasValue)
            query = query.Where(a => a.HospitalId == hospitalId.Value);
        if (doctorId.HasValue)
            query = query.Where(a => a.DoctorId == doctorId.Value);
        if (patientId.HasValue)
            query = query.Where(a => a.PatientId == patientId.Value);
        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);
        if (from.HasValue)
            query = query.Where(a => a.ScheduledAt >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.ScheduledAt <= to.Value);

        return await query
            .OrderByDescending(a => a.ScheduledAt)
            .Take(500) // safety cap — use pagination for larger exports
            .ToListAsync(ct);
    }

    public async Task<bool> HasDoctorPatientRelationshipAsync(Guid doctorId, Guid patientId)
    {
        return await _context.Appointments
            .AsNoTracking()
            .AnyAsync(a =>
                a.DoctorId == doctorId &&
                a.PatientId == patientId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.Status != AppointmentStatus.NoShow);
    }

    public async Task<DoctorAppointmentCounts> GetDoctorAppointmentCountsAsync(Guid doctorId, DateTime asOf)
    {
        var todayStart = asOf.Date;
        var todayEnd   = todayStart.AddDays(1);

        // Single round-trip: pull only upcoming + today appointments for this doctor.
        // We avoid loading cancelled/completed appointments entirely.
        var activeStatuses = new[] { AppointmentStatus.Scheduled, AppointmentStatus.Confirmed, AppointmentStatus.InProgress };

        var relevant = await _context.Appointments
            .AsNoTracking()
            .Where(a => a.DoctorId == doctorId
                        && activeStatuses.Contains(a.Status)
                        && a.ScheduledAt >= todayStart)
            .OrderBy(a => a.ScheduledAt)
            .ToListAsync();

        // todayCount  = all appointments within today's calendar day
        // upcomingCount = appointments strictly after the current moment (future, excludes today's past slots)
        // These two counts intentionally overlap: an appointment later today is counted in both.
        var todayCount    = relevant.Count(a => a.ScheduledAt >= todayStart && a.ScheduledAt < todayEnd);
        var upcomingCount = relevant.Count(a => a.ScheduledAt > asOf);
        var next          = relevant.FirstOrDefault(a => a.ScheduledAt > asOf);

        return new DoctorAppointmentCounts(todayCount, upcomingCount, next);
    }
}
