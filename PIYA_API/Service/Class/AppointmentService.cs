using Microsoft.EntityFrameworkCore;
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
        // Check for conflicts
        var isAvailable = await IsDoctorAvailableAsync(
            appointment.DoctorId,
            appointment.ScheduledAt,
            appointment.DurationMinutes
        );

        if (!isAvailable)
        {
            throw new InvalidOperationException("Doctor is not available at the specified time");
        }

        appointment.Id = Guid.NewGuid();
        appointment.Status = AppointmentStatus.Scheduled;
        appointment.CreatedAt = DateTime.UtcNow;
        appointment.UpdatedAt = DateTime.UtcNow;

        // Validate the referenced hospital exists
        if (appointment.HospitalId != Guid.Empty)
        {
            var hospitalExists = await _context.Set<PIYA_API.Model.Hospital>().AnyAsync(h => h.Id == appointment.HospitalId);
            if (!hospitalExists)
                throw new KeyNotFoundException($"Hospital {appointment.HospitalId} not found.");
        }

        // Validate the referenced doctor exists
        if (appointment.DoctorId != Guid.Empty)
        {
            var doctorExists = await _context.Set<PIYA_API.Model.User>()
                .AnyAsync(u => u.Id == appointment.DoctorId && u.Role == PIYA_API.Model.UserRole.Doctor);
            if (!doctorExists)
                throw new KeyNotFoundException($"Doctor {appointment.DoctorId} not found.");
        }

        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "BookAppointment",
            "Appointment",
            appointment.Id.ToString(),
            appointment.PatientId,
            $"Appointment booked with Dr. {appointment.DoctorId} for {appointment.ScheduledAt}"
        );

        return appointment;
    }

    public async Task<Appointment?> GetByIdAsync(Guid id)
    {
        return await QueryAppointments(asNoTracking: true)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    private async Task<Appointment> GetTrackedByIdAsync(Guid id)
    {
        return await QueryAppointments()
            .FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new InvalidOperationException("Appointment not found");
    }

    public async Task<List<Appointment>> GetPatientAppointmentsAsync(Guid patientId, AppointmentStatus? status = null)
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
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetDoctorAppointmentsAsync(Guid doctorId, DateTime? date = null, AppointmentStatus? status = null)
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
            .ToListAsync();
    }

    public async Task<bool> IsDoctorAvailableAsync(Guid doctorId, DateTime scheduledAt, int durationMinutes = 30)
    {
        var endTime = scheduledAt.AddMinutes(durationMinutes);

        var conflict = await _context.Appointments
            .Where(a => a.DoctorId == doctorId)
            .Where(a => a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)
            .Where(a =>
                (a.ScheduledAt < endTime && a.ScheduledAt.AddMinutes(a.DurationMinutes) > scheduledAt)
            )
            .AnyAsync();

        return !conflict;
    }

    public async Task<Appointment> UpdateStatusAsync(Guid id, AppointmentStatus status, string? reason = null)
    {
        var appointment = await GetTrackedByIdAsync(id);
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

        await _context.SaveChangesAsync();

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
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancellationReason = reason;
        appointment.CancelledBy = cancelledBy;
        appointment.CancelledAt = DateTime.UtcNow;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

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
        var appointment = await GetTrackedByIdAsync(id);

        // Cannot reschedule appointments that are already finished or cancelled
        if (appointment.Status == AppointmentStatus.Cancelled ||
            appointment.Status == AppointmentStatus.Completed ||
            appointment.Status == AppointmentStatus.NoShow)
        {
            throw new InvalidOperationException($"Cannot reschedule a {appointment.Status} appointment");
        }

        // Check if new time is available
        var isAvailable = await IsDoctorAvailableAsync(
            appointment.DoctorId,
            newScheduledAt,
            appointment.DurationMinutes
        );

        if (!isAvailable)
        {
            throw new InvalidOperationException("Doctor is not available at the new time");
        }

        var oldTime = appointment.ScheduledAt;
        appointment.ScheduledAt = newScheduledAt;
        appointment.Status = AppointmentStatus.Rescheduled;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "RescheduleAppointment",
            "Appointment",
            id.ToString(),
            appointment.PatientId,
            $"Rescheduled from {oldTime} to {newScheduledAt}"
        );

        // Notify the patient by email
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

        return appointment;
    }

    public async Task<Appointment> CompleteAppointmentAsync(Guid id, string? doctorNotes)
    {
        var appointment = await GetTrackedByIdAsync(id);
        appointment.Status = AppointmentStatus.Completed;
        appointment.AppointmentNotes = doctorNotes;
        appointment.ActualEndTime = DateTime.UtcNow;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

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
}
