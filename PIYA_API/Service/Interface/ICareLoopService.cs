using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

public interface ICareLoopService
{
    /// <summary>Materialize due/overdue task state for active patient workflows.</summary>
    Task RefreshPatientAsync(Guid patientId, CancellationToken cancellationToken = default);

    Task<CareLoopTask?> CompleteTaskAsync(
        Guid patientId,
        Guid taskId,
        string? patientResponse,
        CancellationToken cancellationToken = default);

    Task<CareLoopTask?> SkipTaskAsync(
        Guid patientId,
        Guid taskId,
        string? reason,
        CancellationToken cancellationToken = default);
}
