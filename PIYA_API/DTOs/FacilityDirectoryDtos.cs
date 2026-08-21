using PIYA_API.Model;

namespace PIYA_API.DTOs;

public class DirectoryFacilityPublicDto
{
    public Guid Id { get; set; }
    public DirectoryFacilityKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string? District { get; set; }
    public List<string> PhoneNumbers { get; set; } = [];
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? OperatingHours { get; set; }
    public List<string> Services { get; set; } = [];
    public CoordinatesDto? Coordinates { get; set; }
    public FacilityOwnershipType Ownership { get; set; }
    public FacilityVerificationStatus VerificationStatus { get; set; }
    public bool IsOnboarded { get; set; }
    public string? PrimarySourceName { get; set; }
    public string Attribution { get; set; } = string.Empty;
    public DateTime? LastVerifiedAt { get; set; }
}

public sealed class DirectoryFacilityPageDto
{
    public List<DirectoryFacilityPublicDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}

public sealed class AdminDirectoryFacilityDto : DirectoryFacilityPublicDto
{
    public string? LegalName { get; set; }
    public string? LicenseNumber { get; set; }
    public string? TaxId { get; set; }
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; }
    public Guid? OperationalHospitalId { get; set; }
    public Guid? OperationalPharmacyId { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<FacilitySourceDto> Sources { get; set; } = [];
}

public sealed class AdminDirectoryFacilityPageDto
{
    public List<AdminDirectoryFacilityDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
}

public sealed class FacilitySourceDto
{
    public Guid Id { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string? DataLicense { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime LastSeenAt { get; set; }
}

public sealed class FacilityDirectoryStatsDto
{
    public int PublishedFacilities { get; set; }
    public int RegistryVerified { get; set; }
    public int Onboarded { get; set; }
    public int PendingClaims { get; set; }
    public int PendingDuplicates { get; set; }
    public Dictionary<string, int> CountsByKind { get; set; } = [];
    public Dictionary<string, int> CountsBySource { get; set; } = [];
    public DateTime? LastSuccessfulSyncAt { get; set; }
}

public sealed class FacilityClaimRequest
{
    public required string OrganizationName { get; set; }
    public string? LicenseNumber { get; set; }
    public string? TaxId { get; set; }
    public required string ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? EvidenceNotes { get; set; }
}

public sealed class FacilityClaimDto
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public Guid ClaimedByUserId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public string? LicenseNumber { get; set; }
    public string? TaxId { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? EvidenceNotes { get; set; }
    public FacilityClaimStatus Status { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public sealed class ReviewFacilityClaimRequest
{
    public FacilityClaimStatus Status { get; set; }
    public string? ReviewNotes { get; set; }
}

public sealed class UpdateDirectoryFacilityRequest
{
    public DirectoryFacilityKind? Kind { get; set; }
    public string? Name { get; set; }
    public string? LegalName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? District { get; set; }
    public List<string>? PhoneNumbers { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? OperatingHours { get; set; }
    public List<string>? Services { get; set; }
    public CoordinatesDto? Coordinates { get; set; }
    public FacilityOwnershipType? Ownership { get; set; }
    public FacilityVerificationStatus? VerificationStatus { get; set; }
    public string? LicenseNumber { get; set; }
    public string? TaxId { get; set; }
    public bool? IsPublished { get; set; }
    public bool? IsActive { get; set; }
    public Guid? OperationalHospitalId { get; set; }
    public Guid? OperationalPharmacyId { get; set; }
}

public sealed class FacilitySyncRequest
{
    public bool DryRun { get; set; } = true;
    public List<string>? Sources { get; set; }
}

public sealed class FacilityImportRunDto
{
    public Guid Id { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public FacilityImportStatus Status { get; set; }
    public bool DryRun { get; set; }
    public int RecordsRead { get; set; }
    public int RecordsCreated { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsMatched { get; set; }
    public int DuplicateCandidates { get; set; }
    public int RecordsSkipped { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class FacilityDuplicateDto
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public string FacilityName { get; set; } = string.Empty;
    public Guid PossibleDuplicateId { get; set; }
    public string PossibleDuplicateName { get; set; } = string.Empty;
    public int ConfidenceScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public FacilityDuplicateStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ReviewDuplicateRequest
{
    public bool Merge { get; set; }
}
