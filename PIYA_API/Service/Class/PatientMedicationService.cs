using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public sealed class PatientMedicationService(PharmacyApiDbContext db) : IPatientMedicationService
{
    private readonly PharmacyApiDbContext _db = db;

    public async Task<IReadOnlyList<TrackedMedicationResponse>> GetAsync(
        Guid patientId, bool includeArchived = false)
    {
        var query = MedicationQuery().Where(item => item.PatientId == patientId);
        if (!includeArchived)
            query = query.Where(item => item.Status != PatientMedicationStatus.Archived);

        var items = await query.OrderBy(item => item.DisplayName).ToListAsync();
        return items.Select(TrackedMedicationResponse.From).ToList();
    }

    public async Task<TrackedMedicationResponse?> GetAsync(Guid patientId, Guid medicationId)
    {
        var item = await MedicationQuery()
            .SingleOrDefaultAsync(entry => entry.Id == medicationId && entry.PatientId == patientId);
        return item is null ? null : TrackedMedicationResponse.From(item);
    }

    public async Task<TrackedMedicationResponse> CreateAsync(
        Guid patientId, CreateTrackedMedicationRequest request)
    {
        var startDate = request.StartDate == default
            ? DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc)
            : AsUtc(request.StartDate);
        DateTime? endDate = request.EndDate.HasValue ? AsUtc(request.EndDate.Value) : null;
        ValidateDates(startDate, endDate);
        ValidateSupply(request.SupplyTotal, request.SupplyRemaining, request.LowSupplyThreshold);

        PrescriptionItem? prescriptionItem = null;
        if (request.PrescriptionItemId.HasValue)
        {
            prescriptionItem = await _db.Set<PrescriptionItem>()
                .Include(item => item.Prescription)
                .Include(item => item.Medication)
                .SingleOrDefaultAsync(item => item.Id == request.PrescriptionItemId.Value &&
                                              item.Prescription.PatientId == patientId)
                ?? throw new PatientHealthNotFoundException("Prescription item not found.");

            if (await _db.Set<PatientMedication>().AnyAsync(item =>
                    item.PatientId == patientId && item.PrescriptionItemId == prescriptionItem.Id))
                throw new PatientHealthConflictException("This prescription item is already being tracked.");
        }

        var displayName = prescriptionItem?.Medication.BrandName ?? request.DisplayName?.Trim();
        var dosage = prescriptionItem?.Dosage ?? request.Dosage?.Trim();
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(dosage))
            throw new ArgumentException("Medication name and dosage are required.");
        if (displayName.Length > 200 || dosage.Length > 200)
            throw new ArgumentException("Medication name and dosage must not exceed 200 characters.");

        var item = new PatientMedication
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            MedicationId = prescriptionItem?.MedicationId,
            PrescriptionItemId = prescriptionItem?.Id,
            Source = prescriptionItem is null
                ? PatientMedicationSource.PatientEntered
                : PatientMedicationSource.Prescription,
            DisplayName = displayName,
            GenericName = prescriptionItem?.Medication.GenericName ?? Clean(request.GenericName),
            Strength = prescriptionItem?.Medication.Strength ?? Clean(request.Strength),
            Form = prescriptionItem?.Medication.Form ?? Clean(request.Form),
            Dosage = dosage,
            Instructions = prescriptionItem?.Instructions ?? Clean(request.Instructions),
            StartDate = startDate,
            EndDate = endDate,
            SupplyTotal = request.SupplyTotal,
            SupplyRemaining = request.SupplyRemaining,
            SupplyUnit = request.SupplyUnit.Trim(),
            LowSupplyThreshold = request.LowSupplyThreshold,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Set<PatientMedication>().Add(item);
        await _db.SaveChangesAsync();
        return TrackedMedicationResponse.From(item);
    }

    public async Task<TrackedMedicationResponse> UpdateAsync(
        Guid patientId, Guid medicationId, UpdateTrackedMedicationRequest request)
    {
        var item = await _db.Set<PatientMedication>()
            .Include(entry => entry.Schedules)
            .SingleOrDefaultAsync(entry => entry.Id == medicationId && entry.PatientId == patientId)
            ?? throw new PatientHealthNotFoundException("Tracked medication not found.");

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            item.DisplayName = request.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(request.Dosage))
            item.Dosage = request.Dosage.Trim();
        if (request.Instructions is not null)
            item.Instructions = Clean(request.Instructions);
        if (request.Status.HasValue)
        {
            if (!Enum.IsDefined(request.Status.Value))
                throw new ArgumentException("Unknown medication status.");
            item.Status = request.Status.Value;
        }
        if (request.EndDate.HasValue)
            item.EndDate = AsUtc(request.EndDate.Value);
        if (request.SupplyRemaining.HasValue)
            item.SupplyRemaining = request.SupplyRemaining.Value;
        if (request.LowSupplyThreshold.HasValue)
            item.LowSupplyThreshold = request.LowSupplyThreshold.Value;

        ValidateDates(item.StartDate, item.EndDate);
        ValidateSupply(item.SupplyTotal, item.SupplyRemaining, item.LowSupplyThreshold);
        item.UpdatedAt = DateTime.UtcNow;

        foreach (var schedule in item.Schedules)
        {
            schedule.NextReminderAt = item.Status == PatientMedicationStatus.Active &&
                                      schedule.RemindersEnabled
                ? MedicationScheduleClock.NextOccurrence(item, schedule, DateTime.UtcNow)
                : null;
            schedule.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return TrackedMedicationResponse.From(item);
    }

    public async Task<DoseScheduleResponse> AddScheduleAsync(
        Guid patientId, Guid medicationId, UpsertDoseScheduleRequest request)
    {
        ValidateSchedule(request);
        var medication = await _db.Set<PatientMedication>()
            .SingleOrDefaultAsync(item => item.Id == medicationId && item.PatientId == patientId &&
                                          item.Status != PatientMedicationStatus.Archived)
            ?? throw new PatientHealthNotFoundException("Tracked medication not found.");

        var schedule = new MedicationDoseSchedule
        {
            Id = Guid.NewGuid(),
            PatientMedicationId = medication.Id,
            DoseAmount = request.DoseAmount,
            TimeOfDayMinutes = request.TimeOfDayMinutes,
            DaysOfWeek = request.DaysOfWeek,
            TimeZoneId = request.TimeZoneId.Trim(),
            RemindersEnabled = request.RemindersEnabled,
            GracePeriodMinutes = request.GracePeriodMinutes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        schedule.NextReminderAt = medication.Status == PatientMedicationStatus.Active &&
                                  schedule.RemindersEnabled
            ? MedicationScheduleClock.NextOccurrence(medication, schedule, DateTime.UtcNow)
            : null;

        _db.Set<MedicationDoseSchedule>().Add(schedule);
        await _db.SaveChangesAsync();
        return DoseScheduleResponse.From(schedule);
    }

    public async Task<DoseScheduleResponse> UpdateScheduleAsync(
        Guid patientId, Guid scheduleId, UpsertDoseScheduleRequest request)
    {
        ValidateSchedule(request);
        var schedule = await _db.Set<MedicationDoseSchedule>()
            .Include(item => item.PatientMedication)
            .SingleOrDefaultAsync(item => item.Id == scheduleId &&
                                          item.PatientMedication.PatientId == patientId)
            ?? throw new PatientHealthNotFoundException("Medication schedule not found.");

        schedule.DoseAmount = request.DoseAmount;
        schedule.TimeOfDayMinutes = request.TimeOfDayMinutes;
        schedule.DaysOfWeek = request.DaysOfWeek;
        schedule.TimeZoneId = request.TimeZoneId.Trim();
        schedule.RemindersEnabled = request.RemindersEnabled;
        schedule.GracePeriodMinutes = request.GracePeriodMinutes;
        schedule.UpdatedAt = DateTime.UtcNow;
        schedule.NextReminderAt = schedule.PatientMedication.Status == PatientMedicationStatus.Active &&
                                  schedule.RemindersEnabled
            ? MedicationScheduleClock.NextOccurrence(schedule.PatientMedication, schedule, DateTime.UtcNow)
            : null;

        await _db.SaveChangesAsync();
        return DoseScheduleResponse.From(schedule);
    }

    public async Task DeleteScheduleAsync(Guid patientId, Guid scheduleId)
    {
        var schedule = await _db.Set<MedicationDoseSchedule>()
            .Include(item => item.PatientMedication)
            .SingleOrDefaultAsync(item => item.Id == scheduleId &&
                                          item.PatientMedication.PatientId == patientId)
            ?? throw new PatientHealthNotFoundException("Medication schedule not found.");
        _db.Set<MedicationDoseSchedule>().Remove(schedule);
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ScheduledMedicationDoseResponse>> GetDosesForDateAsync(
        Guid patientId, DateOnly localDate, string timeZoneId)
    {
        var zone = MedicationScheduleClock.GetTimeZone(timeZoneId);
        var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var rangeStart = TimeZoneInfo.ConvertTimeToUtc(localStart, zone);
        var rangeEnd = TimeZoneInfo.ConvertTimeToUtc(localEnd, zone);

        var medications = await _db.Set<PatientMedication>()
            .AsNoTracking()
            .Include(item => item.Schedules)
            .Where(item => item.PatientId == patientId && item.Status == PatientMedicationStatus.Active)
            .ToListAsync();
        var scheduleIds = medications.SelectMany(item => item.Schedules).Select(item => item.Id).ToList();
        var occurrences = await _db.Set<MedicationDoseOccurrence>()
            .AsNoTracking()
            .Where(item => scheduleIds.Contains(item.ScheduleId) &&
                           item.ScheduledFor >= rangeStart && item.ScheduledFor < rangeEnd)
            .ToDictionaryAsync(item => (item.ScheduleId, item.ScheduledFor));

        var result = new List<ScheduledMedicationDoseResponse>();
        foreach (var medication in medications)
        {
            foreach (var schedule in medication.Schedules)
            {
                var scheduledFor = MedicationScheduleClock.OccurrenceOnDate(medication, schedule, localDate);
                if (!scheduledFor.HasValue) continue;
                occurrences.TryGetValue((schedule.Id, scheduledFor.Value), out var occurrence);
                result.Add(new ScheduledMedicationDoseResponse(
                    occurrence?.Id,
                    schedule.Id,
                    medication.Id,
                    medication.DisplayName,
                    medication.Dosage,
                    schedule.DoseAmount,
                    medication.SupplyUnit,
                    scheduledFor.Value,
                    occurrence?.Status ?? MedicationDoseStatus.Scheduled,
                    occurrence?.RecordedAt));
            }
        }

        return result.OrderBy(item => item.ScheduledFor).ToList();
    }

    public async Task<MedicationAdherenceHistoryResponse> GetDoseHistoryAsync(
        Guid patientId, DateTime from, DateTime to, MedicationDoseStatus? status = null)
    {
        from = AsUtc(from);
        to = AsUtc(to);
        if (to <= from) throw new ArgumentException("History end must be after its start.");
        if (to - from > TimeSpan.FromDays(366))
            throw new ArgumentException("Medication history is limited to 366 days per request.");

        var query = _db.Set<MedicationDoseOccurrence>().AsNoTracking()
            .Include(item => item.PatientMedication)
            .Where(item => item.PatientId == patientId &&
                           item.ScheduledFor >= from && item.ScheduledFor < to);
        if (status.HasValue) query = query.Where(item => item.Status == status.Value);
        var occurrences = await query.OrderByDescending(item => item.ScheduledFor)
            .Take(2000).ToListAsync();
        var entries = occurrences.Select(item => new MedicationAdherenceEntryResponse(
            item.Id,
            item.PatientMedicationId,
            item.PatientMedication.DisplayName,
            item.PatientMedication.Dosage,
            item.ScheduledFor,
            item.Status,
            item.RecordedAt,
            item.SupplyDeducted,
            item.Note)).ToList();
        return new MedicationAdherenceHistoryResponse(
            from,
            to,
            occurrences.Count(item => item.Status == MedicationDoseStatus.Taken),
            occurrences.Count(item => item.Status == MedicationDoseStatus.Skipped),
            occurrences.Count(item => item.Status == MedicationDoseStatus.Missed),
            occurrences.Count(item => item.Status is MedicationDoseStatus.Scheduled or
                MedicationDoseStatus.ReminderSent),
            entries);
    }

    public async Task<MedicationDoseResponse> RecordDoseAsync(
        Guid patientId, RecordMedicationDoseRequest request)
    {
        if (request.Status is not (MedicationDoseStatus.Taken or MedicationDoseStatus.Skipped))
            throw new ArgumentException("A patient can record a dose only as taken or skipped.");
        if (request.ScheduleId == Guid.Empty || request.ScheduledFor == default)
            throw new ArgumentException("Schedule and scheduled time are required.");

        if (request.ClientEventId.HasValue)
        {
            var prior = await _db.Set<MedicationDoseOccurrence>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.PatientId == patientId &&
                                              item.ClientEventId == request.ClientEventId);
            if (prior is not null) return MedicationDoseResponse.From(prior);
        }

        var schedule = await _db.Set<MedicationDoseSchedule>()
            .Include(item => item.PatientMedication)
            .SingleOrDefaultAsync(item => item.Id == request.ScheduleId &&
                                          item.PatientMedication.PatientId == patientId)
            ?? throw new PatientHealthNotFoundException("Medication schedule not found.");

        var scheduledFor = AsUtc(request.ScheduledFor);
        if (scheduledFor > DateTime.UtcNow.AddMinutes(30))
            throw new ArgumentException("A future dose cannot be recorded yet.");

        var local = TimeZoneInfo.ConvertTimeFromUtc(
            scheduledFor, MedicationScheduleClock.GetTimeZone(schedule.TimeZoneId));
        var expected = MedicationScheduleClock.OccurrenceOnDate(
            schedule.PatientMedication, schedule, DateOnly.FromDateTime(local));
        if (!expected.HasValue || Math.Abs((expected.Value - scheduledFor).TotalSeconds) > 60)
            throw new ArgumentException("The supplied time is not an occurrence of this schedule.");

        var occurrence = await _db.Set<MedicationDoseOccurrence>()
            .SingleOrDefaultAsync(item => item.ScheduleId == schedule.Id &&
                                          item.ScheduledFor == scheduledFor);
        if (occurrence is not null && occurrence.Status is MedicationDoseStatus.Taken or MedicationDoseStatus.Skipped)
            throw new PatientHealthConflictException("This dose has already been recorded.");

        occurrence ??= new MedicationDoseOccurrence
        {
            Id = Guid.NewGuid(),
            ScheduleId = schedule.Id,
            PatientMedicationId = schedule.PatientMedicationId,
            PatientId = patientId,
            ScheduledFor = scheduledFor,
            CreatedAt = DateTime.UtcNow
        };
        if (_db.Entry(occurrence).State == EntityState.Detached)
            _db.Set<MedicationDoseOccurrence>().Add(occurrence);

        occurrence.Status = request.Status;
        occurrence.ClientEventId = request.ClientEventId;
        occurrence.Note = Clean(request.Note);
        occurrence.RecordedAt = DateTime.UtcNow;
        occurrence.UpdatedAt = DateTime.UtcNow;

        if (request.Status == MedicationDoseStatus.Taken && occurrence.SupplyDeducted == 0)
        {
            var deduction = Math.Min(schedule.DoseAmount, schedule.PatientMedication.SupplyRemaining);
            schedule.PatientMedication.SupplyRemaining -= deduction;
            schedule.PatientMedication.UpdatedAt = DateTime.UtcNow;
            occurrence.SupplyDeducted = deduction;
        }

        await _db.SaveChangesAsync();
        return MedicationDoseResponse.From(occurrence);
    }

    private IQueryable<PatientMedication> MedicationQuery() => _db.Set<PatientMedication>()
        .AsNoTracking().Include(item => item.Schedules);

    private static void ValidateDates(DateTime start, DateTime? end)
    {
        if (end.HasValue && end.Value.Date < start.Date)
            throw new ArgumentException("End date cannot be before start date.");
    }

    private static void ValidateSupply(decimal total, decimal remaining, decimal threshold)
    {
        if (total < 0 || remaining < 0 || threshold < 0)
            throw new ArgumentException("Medication supply values cannot be negative.");
        if (remaining > total)
            throw new ArgumentException("Remaining supply cannot exceed total supply.");
    }

    private static void ValidateSchedule(UpsertDoseScheduleRequest request)
    {
        if (request.DoseAmount <= 0) throw new ArgumentException("Dose amount must be positive.");
        if (request.TimeOfDayMinutes is < 0 or > 1439)
            throw new ArgumentException("Dose time must be within a day.");
        if (request.DaysOfWeek == MedicationScheduleDays.None ||
            (request.DaysOfWeek & ~MedicationScheduleDays.EveryDay) != 0)
            throw new ArgumentException("At least one valid schedule day is required.");
        if (request.GracePeriodMinutes is < 15 or > 1440)
            throw new ArgumentException("Grace period must be between 15 and 1440 minutes.");
        _ = MedicationScheduleClock.GetTimeZone(request.TimeZoneId);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class MedicationScheduleClock
{
    public static TimeZoneInfo GetTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ArgumentException("Time zone is required.");
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ArgumentException("Unknown time zone.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new ArgumentException("Invalid time zone.");
        }
    }

    public static DateTime? NextOccurrence(
        PatientMedication medication,
        MedicationDoseSchedule schedule,
        DateTime afterUtc)
    {
        var zone = GetTimeZone(schedule.TimeZoneId);
        var utc = afterUtc.Kind == DateTimeKind.Utc
            ? afterUtc
            : afterUtc.ToUniversalTime();
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var firstDate = DateOnly.FromDateTime(local);
        for (var offset = 0; offset <= 14; offset++)
        {
            var candidate = OccurrenceOnDate(medication, schedule, firstDate.AddDays(offset));
            if (candidate.HasValue && candidate.Value > utc) return candidate;
        }
        return null;
    }

    public static DateTime? OccurrenceOnDate(
        PatientMedication medication,
        MedicationDoseSchedule schedule,
        DateOnly localDate)
    {
        if (localDate < DateOnly.FromDateTime(medication.StartDate)) return null;
        if (medication.EndDate.HasValue && localDate > DateOnly.FromDateTime(medication.EndDate.Value))
            return null;
        if (!IsDayEnabled(schedule.DaysOfWeek, localDate.DayOfWeek)) return null;

        var zone = GetTimeZone(schedule.TimeZoneId);
        var localDateTime = localDate.ToDateTime(
            new TimeOnly(schedule.TimeOfDayMinutes / 60, schedule.TimeOfDayMinutes % 60),
            DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(localDateTime)) return null;
        return TimeZoneInfo.ConvertTimeToUtc(localDateTime, zone);
    }

    private static bool IsDayEnabled(MedicationScheduleDays days, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => days.HasFlag(MedicationScheduleDays.Monday),
        DayOfWeek.Tuesday => days.HasFlag(MedicationScheduleDays.Tuesday),
        DayOfWeek.Wednesday => days.HasFlag(MedicationScheduleDays.Wednesday),
        DayOfWeek.Thursday => days.HasFlag(MedicationScheduleDays.Thursday),
        DayOfWeek.Friday => days.HasFlag(MedicationScheduleDays.Friday),
        DayOfWeek.Saturday => days.HasFlag(MedicationScheduleDays.Saturday),
        DayOfWeek.Sunday => days.HasFlag(MedicationScheduleDays.Sunday),
        _ => false
    };
}
