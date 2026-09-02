using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>Central consent check for endpoints that expose shared patient data.</summary>
public interface ICareCircleAccessService
{
    Task<bool> HasScopeAsync(
        Guid patientId,
        Guid memberUserId,
        CareCircleScope requiredScope,
        CancellationToken cancellationToken = default);
}
