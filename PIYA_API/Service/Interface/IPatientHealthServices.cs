using PIYA_API.DTOs;
using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

public interface IPatientMedicationService
{
    Task<IReadOnlyList<TrackedMedicationResponse>> GetAsync(Guid patientId, bool includeArchived = false);
    Task<TrackedMedicationResponse?> GetAsync(Guid patientId, Guid medicationId);
    Task<TrackedMedicationResponse> CreateAsync(Guid patientId, CreateTrackedMedicationRequest request);
    Task<TrackedMedicationResponse> UpdateAsync(Guid patientId, Guid medicationId, UpdateTrackedMedicationRequest request);
    Task<DoseScheduleResponse> AddScheduleAsync(Guid patientId, Guid medicationId, UpsertDoseScheduleRequest request);
    Task<DoseScheduleResponse> UpdateScheduleAsync(Guid patientId, Guid scheduleId, UpsertDoseScheduleRequest request);
    Task DeleteScheduleAsync(Guid patientId, Guid scheduleId);
    Task<IReadOnlyList<ScheduledMedicationDoseResponse>> GetDosesForDateAsync(
        Guid patientId, DateOnly localDate, string timeZoneId);
    Task<MedicationAdherenceHistoryResponse> GetDoseHistoryAsync(
        Guid patientId, DateTime from, DateTime to, MedicationDoseStatus? status = null);
    Task<MedicationDoseResponse> RecordDoseAsync(Guid patientId, RecordMedicationDoseRequest request);
}

public interface IPatientNotificationInboxService
{
    Task<PatientInboxNotification> EnqueueAsync(
        Guid userId,
        PatientNotificationCategory category,
        string title,
        string body,
        string? actionRoute = null,
        IReadOnlyDictionary<string, string>? data = null,
        string? dedupeKey = null,
        CancellationToken cancellationToken = default);
    Task<PatientNotificationPageResponse> GetPageAsync(
        Guid userId, int page, int pageSize, bool unreadOnly, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ArchiveAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
}

public interface IMedicationReminderProcessor
{
    Task<ReminderProcessingResponse> ProcessDueAsync(
        DateTime? now = null, CancellationToken cancellationToken = default);
}

public interface IPatientPickupService
{
    Task<IReadOnlyList<PharmacyPickupResponse>> GetPatientHistoryAsync(Guid patientId);
    Task<IReadOnlyList<PharmacyPickupResponse>> GetPharmacyHistoryAsync(Guid pharmacyId, Guid actorId, bool isAdmin);
    Task<PharmacyPickupResponse> CollectAsync(
        Guid refillRequestId, Guid actorId, bool isAdmin, decimal quantityCollected);
}

public interface IStructuredLabResultService
{
    Task<IReadOnlyList<StructuredLabReportResponse>> GetPatientReportsAsync(Guid patientId);
    Task<StructuredLabReportResponse?> GetPatientReportAsync(Guid patientId, Guid medicalTestId);
    Task<StructuredLabReportResponse> ReplaceAnalytesAsync(
        Guid medicalTestId,
        Guid actorId,
        bool isSuperAdmin,
        ReplaceLabAnalytesRequest request);
}

public sealed class PatientHealthNotFoundException(string message) : Exception(message);
public sealed class PatientHealthConflictException(string message) : Exception(message);
