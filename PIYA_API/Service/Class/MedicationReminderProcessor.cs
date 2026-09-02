using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public sealed class MedicationReminderProcessor(
    PharmacyApiDbContext db,
    IPatientNotificationInboxService inbox,
    INotificationService notifications,
    ILogger<MedicationReminderProcessor> logger) : IMedicationReminderProcessor
{
    private readonly PharmacyApiDbContext _db = db;
    private readonly IPatientNotificationInboxService _inbox = inbox;
    private readonly INotificationService _notifications = notifications;
    private readonly ILogger<MedicationReminderProcessor> _logger = logger;

    public async Task<ReminderProcessingResponse> ProcessDueAsync(
        DateTime? now = null, CancellationToken cancellationToken = default)
    {
        var instant = ToUtc(now ?? DateTime.UtcNow);
        var remindersCreated = 0;
        var missedCreated = 0;

        var schedules = await _db.Set<MedicationDoseSchedule>()
            .Include(item => item.PatientMedication)
            .Where(item => item.RemindersEnabled && item.NextReminderAt != null &&
                           item.NextReminderAt <= instant &&
                           item.PatientMedication.Status == PatientMedicationStatus.Active)
            .OrderBy(item => item.NextReminderAt)
            .Take(1000)
            .ToListAsync(cancellationToken);

        foreach (var schedule in schedules)
        {
            // Bound catch-up work if processing was offline for a long period.
            for (var generated = 0;
                 generated < 32 && schedule.NextReminderAt.HasValue &&
                 schedule.NextReminderAt.Value <= instant;
                 generated++)
            {
                var scheduledFor = schedule.NextReminderAt.Value;
                var existing = await _db.Set<MedicationDoseOccurrence>()
                    .SingleOrDefaultAsync(item => item.ScheduleId == schedule.Id &&
                                                  item.ScheduledFor == scheduledFor,
                        cancellationToken);
                if (existing is null)
                {
                    var graceEnd = scheduledFor.AddMinutes(schedule.GracePeriodMinutes);
                    var status = graceEnd < instant
                        ? MedicationDoseStatus.Missed
                        : MedicationDoseStatus.ReminderSent;
                    existing = new MedicationDoseOccurrence
                    {
                        Id = Guid.NewGuid(),
                        ScheduleId = schedule.Id,
                        PatientMedicationId = schedule.PatientMedicationId,
                        PatientId = schedule.PatientMedication.PatientId,
                        ScheduledFor = scheduledFor,
                        Status = status,
                        ReminderSentAt = status == MedicationDoseStatus.ReminderSent ? instant : null,
                        CreatedAt = instant,
                        UpdatedAt = instant
                    };
                    _db.Set<MedicationDoseOccurrence>().Add(existing);

                    if (status == MedicationDoseStatus.ReminderSent)
                    {
                        await _inbox.EnqueueAsync(
                            schedule.PatientMedication.PatientId,
                            PatientNotificationCategory.MedicationReminder,
                            "Medication reminder",
                            $"It is time to take {schedule.PatientMedication.DisplayName}.",
                            $"piya://medications/doses/{existing.Id}",
                            new Dictionary<string, string>
                            {
                                ["type"] = "medication_reminder",
                                ["occurrenceId"] = existing.Id.ToString(),
                                ["medicationId"] = schedule.PatientMedicationId.ToString()
                            },
                            $"medication-dose:{schedule.Id}:{scheduledFor.Ticks}",
                            cancellationToken);

                        // Push carries no medication name or other clinical information.
                        await _notifications.SendPushNotificationAsync(
                            schedule.PatientMedication.PatientId,
                            "Medication reminder",
                            "It is time for a scheduled medication.",
                            new Dictionary<string, string>
                            {
                                ["type"] = "medication_reminder",
                                ["occurrenceId"] = existing.Id.ToString()
                            });
                        remindersCreated++;
                    }
                    else
                    {
                        missedCreated++;
                    }
                }

                schedule.NextReminderAt = MedicationScheduleClock.NextOccurrence(
                    schedule.PatientMedication, schedule, scheduledFor);
                schedule.UpdatedAt = instant;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        var sent = await _db.Set<MedicationDoseOccurrence>()
            .Include(item => item.Schedule)
            .Where(item => item.Status == MedicationDoseStatus.ReminderSent &&
                           item.ScheduledFor < instant)
            .Take(2000)
            .ToListAsync(cancellationToken);
        var markedMissed = 0;
        foreach (var occurrence in sent)
        {
            if (occurrence.ScheduledFor.AddMinutes(occurrence.Schedule.GracePeriodMinutes) > instant)
                continue;
            occurrence.Status = MedicationDoseStatus.Missed;
            occurrence.UpdatedAt = instant;
            markedMissed++;
        }
        if (markedMissed > 0) await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Processed medication reminders at {ProcessedAt}: {Created} sent, {Missed} marked missed",
            instant, remindersCreated, markedMissed);
        return new ReminderProcessingResponse(remindersCreated, missedCreated + markedMissed, instant);
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
