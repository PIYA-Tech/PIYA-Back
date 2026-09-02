using System.ComponentModel.DataAnnotations;
using PIYA_API.Model;

namespace PIYA_API.DTOs;

public sealed class CreateTrackedMedicationRequest
{
    public Guid? PrescriptionItemId { get; init; }
    [Required, StringLength(200)] public string DisplayName { get; init; } = string.Empty;
    [StringLength(200)] public string? GenericName { get; init; }
    [StringLength(100)] public string? Strength { get; init; }
    [StringLength(100)] public string? Form { get; init; }
    [Required, StringLength(200)] public string Dosage { get; init; } = string.Empty;
    [StringLength(1000)] public string? Instructions { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    [Range(typeof(decimal), "0", "1000000")] public decimal SupplyTotal { get; init; }
    [Range(typeof(decimal), "0", "1000000")] public decimal SupplyRemaining { get; init; }
    [Required, StringLength(50)] public string SupplyUnit { get; init; } = "dose";
    [Range(typeof(decimal), "0", "1000000")] public decimal LowSupplyThreshold { get; init; }
}

public sealed class UpdateTrackedMedicationRequest
{
    [StringLength(200)] public string? DisplayName { get; init; }
    [StringLength(200)] public string? Dosage { get; init; }
    [StringLength(1000)] public string? Instructions { get; init; }
    public PatientMedicationStatus? Status { get; init; }
    public DateTime? EndDate { get; init; }
    [Range(typeof(decimal), "0", "1000000")] public decimal? SupplyRemaining { get; init; }
    [Range(typeof(decimal), "0", "1000000")] public decimal? LowSupplyThreshold { get; init; }
}

public sealed class UpsertDoseScheduleRequest
{
    [Range(typeof(decimal), "0.001", "1000000")] public decimal DoseAmount { get; init; }
    [Range(0, 1439)] public int TimeOfDayMinutes { get; init; }
    public MedicationScheduleDays DaysOfWeek { get; init; } = MedicationScheduleDays.EveryDay;
    [Required, StringLength(100)] public string TimeZoneId { get; init; } = "Asia/Baku";
    public bool RemindersEnabled { get; init; } = true;
    [Range(15, 1440)] public int GracePeriodMinutes { get; init; } = 120;
}

public sealed class RecordMedicationDoseRequest
{
    public Guid ScheduleId { get; init; }
    public DateTime ScheduledFor { get; init; }
    public MedicationDoseStatus Status { get; init; }
    public Guid? ClientEventId { get; init; }
    [StringLength(500)] public string? Note { get; init; }
}

public sealed record DoseScheduleResponse(
    Guid Id,
    decimal DoseAmount,
    int TimeOfDayMinutes,
    MedicationScheduleDays DaysOfWeek,
    string TimeZoneId,
    bool RemindersEnabled,
    int GracePeriodMinutes,
    DateTime? NextReminderAt)
{
    public static DoseScheduleResponse From(MedicationDoseSchedule item) => new(
        item.Id, item.DoseAmount, item.TimeOfDayMinutes, item.DaysOfWeek,
        item.TimeZoneId, item.RemindersEnabled, item.GracePeriodMinutes, item.NextReminderAt);
}

public sealed record TrackedMedicationResponse(
    Guid Id,
    Guid? MedicationId,
    Guid? PrescriptionItemId,
    PatientMedicationSource Source,
    PatientMedicationStatus Status,
    string DisplayName,
    string? GenericName,
    string? Strength,
    string? Form,
    string Dosage,
    string? Instructions,
    DateTime StartDate,
    DateTime? EndDate,
    decimal SupplyTotal,
    decimal SupplyRemaining,
    string SupplyUnit,
    decimal LowSupplyThreshold,
    bool IsLowSupply,
    DateTime? LastRefilledAt,
    IReadOnlyList<DoseScheduleResponse> Schedules)
{
    public static TrackedMedicationResponse From(PatientMedication item) => new(
        item.Id, item.MedicationId, item.PrescriptionItemId, item.Source, item.Status,
        item.DisplayName, item.GenericName, item.Strength, item.Form, item.Dosage,
        item.Instructions, item.StartDate, item.EndDate, item.SupplyTotal,
        item.SupplyRemaining, item.SupplyUnit, item.LowSupplyThreshold,
        item.SupplyRemaining <= item.LowSupplyThreshold, item.LastRefilledAt,
        item.Schedules.OrderBy(schedule => schedule.TimeOfDayMinutes)
            .Select(DoseScheduleResponse.From).ToList());
}

public sealed record MedicationDoseResponse(
    Guid Id,
    Guid ScheduleId,
    Guid PatientMedicationId,
    DateTime ScheduledFor,
    MedicationDoseStatus Status,
    DateTime? ReminderSentAt,
    DateTime? RecordedAt,
    decimal SupplyDeducted,
    string? Note)
{
    public static MedicationDoseResponse From(MedicationDoseOccurrence item) => new(
        item.Id, item.ScheduleId, item.PatientMedicationId, item.ScheduledFor,
        item.Status, item.ReminderSentAt, item.RecordedAt, item.SupplyDeducted, item.Note);
}

public sealed record ScheduledMedicationDoseResponse(
    Guid? OccurrenceId,
    Guid ScheduleId,
    Guid PatientMedicationId,
    string MedicationName,
    string Dosage,
    decimal DoseAmount,
    string SupplyUnit,
    DateTime ScheduledFor,
    MedicationDoseStatus Status,
    DateTime? RecordedAt);

public sealed record MedicationAdherenceEntryResponse(
    Guid OccurrenceId,
    Guid PatientMedicationId,
    string MedicationName,
    string Dosage,
    DateTime ScheduledFor,
    MedicationDoseStatus Status,
    DateTime? RecordedAt,
    decimal SupplyDeducted,
    string? Note);

public sealed record MedicationAdherenceHistoryResponse(
    DateTime From,
    DateTime To,
    int Taken,
    int Skipped,
    int Missed,
    int AwaitingResponse,
    IReadOnlyList<MedicationAdherenceEntryResponse> Items);

public sealed class CollectPharmacyPickupRequest
{
    [Range(typeof(decimal), "0.001", "1000000")]
    public decimal QuantityCollected { get; init; }
}

public sealed record PharmacyPickupResponse(
    Guid Id,
    Guid RefillRequestId,
    Guid PrescriptionItemId,
    Guid PharmacyId,
    string PharmacyName,
    string MedicationName,
    decimal QuantityCollected,
    PharmacyPickupStatus Status,
    DateTime CollectedAt)
{
    public static PharmacyPickupResponse From(PharmacyPickup item) => new(
        item.Id, item.RefillRequestId, item.PrescriptionItemId, item.PharmacyId,
        item.Pharmacy?.Name ?? string.Empty,
        item.PrescriptionItem?.Medication?.BrandName ?? string.Empty,
        item.QuantityCollected, item.Status, item.CollectedAt);
}

public sealed record PatientRefillStatusEventResponse(
    Guid Id,
    PatientRefillRequestStatus Status,
    string? Note,
    DateTime? EstimatedReadyAt,
    DateTime OccurredAt)
{
    public static PatientRefillStatusEventResponse From(PatientRefillStatusEvent item) => new(
        item.Id, item.Status, item.Note, item.EstimatedReadyAt, item.OccurredAt);
}

public sealed class ReplaceLabAnalytesRequest
{
    [MaxLength(200)] public List<LabAnalyteInput> Analytes { get; init; } = [];
    [StringLength(4000)] public string? Findings { get; init; }
}

public sealed class LabAnalyteInput
{
    [Required, StringLength(80)] public string Code { get; init; } = string.Empty;
    [Required, StringLength(200)] public string Name { get; init; } = string.Empty;
    public decimal? NumericValue { get; init; }
    [StringLength(500)] public string? TextValue { get; init; }
    [StringLength(80)] public string? Unit { get; init; }
    public decimal? ReferenceLow { get; init; }
    public decimal? ReferenceHigh { get; init; }
    [StringLength(300)] public string? ReferenceText { get; init; }
    public LabAnalyteFlag? Flag { get; init; }
    public int SortOrder { get; init; }
    public DateTime? ObservedAt { get; init; }
}

public sealed record LabAnalyteResponse(
    Guid Id,
    string Code,
    string Name,
    decimal? NumericValue,
    string? TextValue,
    string? Unit,
    decimal? ReferenceLow,
    decimal? ReferenceHigh,
    string? ReferenceText,
    LabAnalyteFlag Flag,
    int SortOrder,
    DateTime? ObservedAt)
{
    public static LabAnalyteResponse From(MedicalTestAnalyteResult item) => new(
        item.Id, item.Code, item.Name, item.NumericValue, item.TextValue, item.Unit,
        item.ReferenceLow, item.ReferenceHigh, item.ReferenceText, item.Flag,
        item.SortOrder, item.ObservedAt);
}

public sealed record StructuredLabReportResponse(
    Guid MedicalTestId,
    Guid PatientId,
    MedicalTestType TestType,
    MedicalTestStatus Status,
    string? Findings,
    DateTime? PerformedAt,
    DateTime? ResultsAt,
    IReadOnlyList<LabAnalyteResponse> Analytes);

public sealed record PatientNotificationResponse(
    Guid Id,
    PatientNotificationCategory Category,
    string Title,
    string Body,
    string? ActionRoute,
    string? DataJson,
    DateTime? ReadAt,
    DateTime CreatedAt)
{
    public static PatientNotificationResponse From(PatientInboxNotification item) => new(
        item.Id, item.Category, item.Title, item.Body, item.ActionRoute,
        item.DataJson, item.ReadAt, item.CreatedAt);
}

public sealed record PatientNotificationPageResponse(
    IReadOnlyList<PatientNotificationResponse> Items,
    int Page,
    int PageSize,
    int Total,
    int UnreadCount);

public sealed record ReminderProcessingResponse(
    int RemindersCreated,
    int DosesMarkedMissed,
    DateTime ProcessedAt);
