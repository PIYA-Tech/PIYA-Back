using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class AppointmentReminderService(
    PharmacyApiDbContext context,
    INotificationService notificationService,
    ISignalRNotificationService signalRNotificationService,
    ILogger<AppointmentReminderService> logger) : IAppointmentReminderService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly INotificationService _notificationService = notificationService;
    private readonly ISignalRNotificationService _signalRNotificationService = signalRNotificationService;
    private readonly ILogger<AppointmentReminderService> _logger = logger;

    public async Task<List<AppointmentReminder>> CreateAppointmentRemindersAsync(Guid appointmentId, Guid userId, 
        List<int> minutesBeforeList, List<ReminderDeliveryMethod> deliveryMethods, string? customMessage = null)
    {
        var appointment = await _context.Appointments.FindAsync(appointmentId) ?? throw new ArgumentException("Appointment not found", nameof(appointmentId));
        var reminders = new List<AppointmentReminder>();

        foreach (var minutesBefore in minutesBeforeList)
        {
            var reminderTime = appointment.ScheduledAt.AddMinutes(-minutesBefore);
            
            // Don't create reminders for past times
            if (reminderTime <= DateTime.UtcNow)
                continue;

            var reminder = new AppointmentReminder
            {
                AppointmentId = appointmentId,
                UserId = userId,
                ReminderTime = reminderTime,
                MinutesBeforeAppointment = minutesBefore,
                DeliveryMethods = deliveryMethods,
                CustomMessage = customMessage
            };

            _context.AppointmentReminders.Add(reminder);
            reminders.Add(reminder);
        }

        if (reminders.Any())
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Created {Count} reminder(s) for appointment {AppointmentId}", 
                reminders.Count, appointmentId);
        }

        return reminders;
    }

    public async Task<List<AppointmentReminder>> GetAppointmentRemindersAsync(Guid appointmentId)
    {
        return await _context.AppointmentReminders
            .Where(ar => ar.AppointmentId == appointmentId)
            .OrderBy(ar => ar.ReminderTime)
            .ToListAsync();
    }

    public async Task<List<AppointmentReminder>> GetPendingRemindersAsync(DateTime? upToTime = null)
    {
        var cutoffTime = upToTime ?? DateTime.UtcNow;

        return await _context.AppointmentReminders
            .Include(ar => ar.Appointment)
            .Include(ar => ar.User)
            .Where(ar => !ar.IsSent && ar.RetryCount < 3 && ar.ReminderTime <= cutoffTime)
            .OrderBy(ar => ar.ReminderTime)
            .ToListAsync();
    }

    public async Task MarkReminderAsSentAsync(Guid reminderId, string? deliveryStatus = null)
    {
        var reminder = await _context.AppointmentReminders.FindAsync(reminderId);
        if (reminder != null)
        {
            reminder.IsSent = true;
            reminder.SentAt = DateTime.UtcNow;
            reminder.DeliveryStatus = deliveryStatus;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<int> CancelAppointmentRemindersAsync(Guid appointmentId)
    {
        var reminders = await _context.AppointmentReminders
            .Where(ar => ar.AppointmentId == appointmentId && !ar.IsSent)
            .ToListAsync();

        _context.AppointmentReminders.RemoveRange(reminders);
        await _context.SaveChangesAsync();

        return reminders.Count;
    }

    public async Task<int> ProcessPendingRemindersAsync()
    {
        var pendingReminders = await GetPendingRemindersAsync();
        int processedCount = 0;

        foreach (var reminder in pendingReminders)
        {
            try
            {
                var message = reminder.CustomMessage ??
                    $"You have an appointment scheduled for {reminder.Appointment.ScheduledAt:u}.";
                var deliveryResults = new List<(ReminderDeliveryMethod Method, bool Succeeded)>();

                foreach (var method in reminder.DeliveryMethods.Distinct())
                {
                    var succeeded = await DeliverAppointmentReminderAsync(reminder, method, message);
                    deliveryResults.Add((method, succeeded));
                }

                var deliveryStatus = deliveryResults.Count == 0
                    ? "No delivery methods configured"
                    : string.Join(", ", deliveryResults.Select(result =>
                        $"{result.Method}: {(result.Succeeded ? "Sent" : "Failed")}"));

                if (deliveryResults.Count > 0 && deliveryResults.All(result => result.Succeeded))
                {
                    await MarkReminderAsSentAsync(reminder.Id, deliveryStatus);
                    processedCount++;

                    _logger.LogInformation(
                        "Sent appointment reminder {ReminderId} for appointment {AppointmentId}",
                        reminder.Id,
                        reminder.AppointmentId);
                }
                else
                {
                    await RecordAppointmentDeliveryFailureAsync(reminder, deliveryStatus);
                    _logger.LogWarning(
                        "Appointment reminder {ReminderId} was not marked sent because at least one requested channel failed",
                        reminder.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send appointment reminder {ReminderId}", reminder.Id);
                await RecordAppointmentDeliveryFailureAsync(reminder, "Delivery failed");
            }
        }

        return processedCount;
    }

    private async Task<bool> DeliverAppointmentReminderAsync(
        AppointmentReminder reminder,
        ReminderDeliveryMethod method,
        string message)
    {
        try
        {
            return method switch
            {
                ReminderDeliveryMethod.Email when !string.IsNullOrWhiteSpace(reminder.User.Email) =>
                    await _notificationService.SendEmailAsync(
                        reminder.User.Email,
                        "Appointment reminder",
                        message,
                        isHtml: false),
                ReminderDeliveryMethod.SMS when !string.IsNullOrWhiteSpace(reminder.User.PhoneNumber) =>
                    await _notificationService.SendSmsAsync(reminder.User.PhoneNumber, message),
                ReminderDeliveryMethod.PushNotification =>
                    await _notificationService.SendPushNotificationAsync(
                        reminder.UserId,
                        "Appointment reminder",
                        message,
                        new Dictionary<string, string>
                        {
                            ["type"] = "appointment-reminder",
                            ["appointmentId"] = reminder.AppointmentId.ToString()
                        }),
                ReminderDeliveryMethod.InApp =>
                    await SendAppointmentInAppAsync(reminder, message),
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Appointment reminder {ReminderId} failed on channel {DeliveryMethod}",
                reminder.Id,
                method);
            return false;
        }
    }

    private async Task<bool> SendAppointmentInAppAsync(AppointmentReminder reminder, string message)
    {
        await _signalRNotificationService.SendAppointmentNotificationAsync(
            reminder.UserId,
            message,
            reminder.AppointmentId);
        return true;
    }

    private async Task RecordAppointmentDeliveryFailureAsync(
        AppointmentReminder reminder,
        string deliveryStatus)
    {
        reminder.DeliveryStatus = deliveryStatus;
        reminder.RetryCount++;
        if (reminder.RetryCount < 3)
            reminder.ReminderTime = DateTime.UtcNow.AddMinutes(5);

        await _context.SaveChangesAsync();
    }
}

public class PrescriptionRefillReminderService(
    PharmacyApiDbContext context,
    INotificationService notificationService,
    ISignalRNotificationService signalRNotificationService,
    ILogger<PrescriptionRefillReminderService> logger) : IPrescriptionRefillReminderService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly INotificationService _notificationService = notificationService;
    private readonly ISignalRNotificationService _signalRNotificationService = signalRNotificationService;
    private readonly ILogger<PrescriptionRefillReminderService> _logger = logger;

    public async Task<PrescriptionRefillReminder> CreateRefillReminderAsync(Guid prescriptionId, Guid patientId, 
        DateTime estimatedRefillDate, int daysBeforeRefill, List<ReminderDeliveryMethod> deliveryMethods, 
        List<Guid>? medicationItemIds = null)
    {
        var prescription = await _context.Prescriptions.FindAsync(prescriptionId) ?? throw new ArgumentException("Prescription not found", nameof(prescriptionId));
        var reminderDate = estimatedRefillDate.AddDays(-daysBeforeRefill);

        var reminder = new PrescriptionRefillReminder
        {
            PrescriptionId = prescriptionId,
            PatientId = patientId,
            ReminderDate = reminderDate,
            EstimatedRefillDate = estimatedRefillDate,
            DaysBeforeRefill = daysBeforeRefill,
            DeliveryMethods = deliveryMethods,
            MedicationItemIds = medicationItemIds ?? []
        };

        _context.PrescriptionRefillReminders.Add(reminder);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created refill reminder for prescription {PrescriptionId}, due {ReminderDate}", 
            prescriptionId, reminderDate);

        return reminder;
    }

    public async Task<PrescriptionRefillReminder?> GetPrescriptionReminderAsync(Guid prescriptionId)
    {
        return await _context.PrescriptionRefillReminders
            .FirstOrDefaultAsync(prr => prr.PrescriptionId == prescriptionId);
    }

    public async Task<List<PrescriptionRefillReminder>> GetUserRefillRemindersAsync(Guid userId, bool includeCompleted = false)
    {
        var query = _context.PrescriptionRefillReminders
            .Include(prr => prr.Prescription)
            .Where(prr => prr.PatientId == userId);

        if (!includeCompleted)
        {
            query = query.Where(prr => !prr.IsRefilled && !prr.IsAcknowledged);
        }

        return await query
            .OrderBy(prr => prr.ReminderDate)
            .ToListAsync();
    }

    public async Task<List<PrescriptionRefillReminder>> GetPendingRefillRemindersAsync(DateTime? upToDate = null)
    {
        var cutoffDate = upToDate ?? DateTime.UtcNow;

        return await _context.PrescriptionRefillReminders
            .Include(prr => prr.Prescription)
            .Include(prr => prr.Patient)
            .Where(prr =>
                !prr.IsSent &&
                prr.RetryCount < 3 &&
                prr.ReminderDate <= cutoffDate &&
                !prr.IsRefilled)
            .OrderBy(prr => prr.ReminderDate)
            .ToListAsync();
    }

    public async Task MarkRefillReminderAsSentAsync(Guid reminderId, string? deliveryStatus = null)
    {
        var reminder = await _context.PrescriptionRefillReminders.FindAsync(reminderId);
        if (reminder != null)
        {
            reminder.IsSent = true;
            reminder.SentAt = DateTime.UtcNow;
            reminder.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task AcknowledgeRefillReminderAsync(Guid reminderId, Guid userId)
    {
        var reminder = await _context.PrescriptionRefillReminders
            .FirstOrDefaultAsync(prr => prr.Id == reminderId && prr.PatientId == userId);

        if (reminder != null)
        {
            reminder.IsAcknowledged = true;
            reminder.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task MarkAsRefilledAsync(Guid reminderId, Guid newPrescriptionId)
    {
        var reminder = await _context.PrescriptionRefillReminders.FindAsync(reminderId);
        if (reminder != null)
        {
            reminder.IsRefilled = true;
            reminder.RefillPrescriptionId = newPrescriptionId;
            reminder.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> CancelRefillReminderAsync(Guid reminderId, Guid userId)
    {
        var reminder = await _context.PrescriptionRefillReminders
            .FirstOrDefaultAsync(prr => prr.Id == reminderId && prr.PatientId == userId);

        if (reminder == null)
            return false;

        _context.PrescriptionRefillReminders.Remove(reminder);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<int> ProcessPendingRefillRemindersAsync()
    {
        var pendingReminders = await GetPendingRefillRemindersAsync();
        int processedCount = 0;

        foreach (var reminder in pendingReminders)
        {
            try
            {
                var message =
                    $"Your prescription refill is due on {reminder.EstimatedRefillDate:yyyy-MM-dd}.";
                var deliveryResults = new List<bool>();

                foreach (var method in reminder.DeliveryMethods.Distinct())
                {
                    deliveryResults.Add(
                        await DeliverRefillReminderAsync(reminder, method, message));
                }

                if (deliveryResults.Count > 0 && deliveryResults.All(succeeded => succeeded))
                {
                    await MarkRefillReminderAsSentAsync(reminder.Id);
                    processedCount++;

                    _logger.LogInformation(
                        "Sent refill reminder {ReminderId} for prescription {PrescriptionId}",
                        reminder.Id,
                        reminder.PrescriptionId);
                }
                else
                {
                    await RecordRefillDeliveryFailureAsync(reminder);
                    _logger.LogWarning(
                        "Refill reminder {ReminderId} was not marked sent because at least one requested channel failed",
                        reminder.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send refill reminder {ReminderId}", reminder.Id);
                await RecordRefillDeliveryFailureAsync(reminder);
            }
        }

        return processedCount;
    }

    private async Task<bool> DeliverRefillReminderAsync(
        PrescriptionRefillReminder reminder,
        ReminderDeliveryMethod method,
        string message)
    {
        try
        {
            return method switch
            {
                ReminderDeliveryMethod.Email when !string.IsNullOrWhiteSpace(reminder.Patient.Email) =>
                    await _notificationService.SendEmailAsync(
                        reminder.Patient.Email,
                        "Prescription refill reminder",
                        message,
                        isHtml: false),
                ReminderDeliveryMethod.SMS when !string.IsNullOrWhiteSpace(reminder.Patient.PhoneNumber) =>
                    await _notificationService.SendSmsAsync(reminder.Patient.PhoneNumber, message),
                ReminderDeliveryMethod.PushNotification =>
                    await _notificationService.SendPushNotificationAsync(
                        reminder.PatientId,
                        "Prescription refill reminder",
                        message,
                        new Dictionary<string, string>
                        {
                            ["type"] = "prescription-refill-reminder",
                            ["prescriptionId"] = reminder.PrescriptionId.ToString()
                        }),
                ReminderDeliveryMethod.InApp =>
                    await SendRefillInAppAsync(reminder, message),
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Refill reminder {ReminderId} failed on channel {DeliveryMethod}",
                reminder.Id,
                method);
            return false;
        }
    }

    private async Task<bool> SendRefillInAppAsync(
        PrescriptionRefillReminder reminder,
        string message)
    {
        await _signalRNotificationService.SendPrescriptionNotificationAsync(
            reminder.PatientId,
            message,
            reminder.PrescriptionId);
        return true;
    }

    private async Task RecordRefillDeliveryFailureAsync(PrescriptionRefillReminder reminder)
    {
        reminder.RetryCount++;
        reminder.UpdatedAt = DateTime.UtcNow;
        if (reminder.RetryCount < 3)
            reminder.ReminderDate = DateTime.UtcNow.AddHours(1);

        await _context.SaveChangesAsync();
    }

    public async Task AutoCreateRefillRemindersAsync(Guid prescriptionId)
    {
        var prescription = await _context.Prescriptions
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == prescriptionId);

        if (prescription == null || prescription.Status != PrescriptionStatus.Active)
            return;

        // Check if reminder already exists
        var existingReminder = await _context.PrescriptionRefillReminders
            .AnyAsync(prr => prr.PrescriptionId == prescriptionId);

        if (existingReminder)
            return;

        // Calculate estimated refill date (simplified: 30 days from issuance)
        var estimatedRefillDate = prescription.IssuedAt.AddDays(30);

        // Create reminder 7 days before refill
        await CreateRefillReminderAsync(
            prescriptionId,
            prescription.PatientId,
            estimatedRefillDate,
            7,
            [ 
                ReminderDeliveryMethod.Email, 
                ReminderDeliveryMethod.PushNotification 
            ]
        );
    }
}
