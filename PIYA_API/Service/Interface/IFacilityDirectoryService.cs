using PIYA_API.DTOs;
using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

public interface IFacilityDirectoryService
{
    Task<DirectoryFacilityPageDto> GetPublishedAsync(
        DirectoryFacilityKind? kind,
        string? city,
        string? query,
        bool verifiedOnly,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<DirectoryFacilityPublicDto?> GetPublishedByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
    Task<AdminDirectoryFacilityPageDto> GetAdminAsync(
        DirectoryFacilityKind? kind,
        FacilityVerificationStatus? status,
        string? source,
        string? query,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<AdminDirectoryFacilityDto?> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
    Task<AdminDirectoryFacilityDto> UpdateAsync(
        Guid id,
        UpdateDirectoryFacilityRequest request,
        CancellationToken cancellationToken = default);
    Task<FacilityClaimDto> SubmitClaimAsync(
        Guid facilityId,
        Guid userId,
        FacilityClaimRequest request,
        CancellationToken cancellationToken = default);
    Task<List<FacilityClaimDto>> GetClaimsAsync(
        FacilityClaimStatus? status,
        CancellationToken cancellationToken = default);
    Task<FacilityClaimDto> ReviewClaimAsync(
        Guid claimId,
        Guid reviewerId,
        ReviewFacilityClaimRequest request,
        CancellationToken cancellationToken = default);
    Task<List<FacilityDuplicateDto>> GetDuplicatesAsync(
        FacilityDuplicateStatus? status,
        CancellationToken cancellationToken = default);
    Task ReviewDuplicateAsync(
        Guid duplicateId,
        Guid reviewerId,
        bool merge,
        CancellationToken cancellationToken = default);
    Task<List<FacilityImportRunDto>> GetImportRunsAsync(
        int count,
        CancellationToken cancellationToken = default);
    Task<FacilityDirectoryStatsDto> GetStatsAsync(
        CancellationToken cancellationToken = default);
}

public interface IFacilityDirectorySyncService
{
    IReadOnlyCollection<string> AvailableSources { get; }
    Task<List<FacilityImportRunDto>> SyncAsync(
        bool dryRun,
        IReadOnlyCollection<string>? requestedSources = null,
        CancellationToken cancellationToken = default);
}
