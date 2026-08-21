namespace PIYA_API.Model;

public enum DirectoryFacilityKind
{
    Hospital,
    Clinic,
    Polyclinic,
    MedicalCenter,
    DiagnosticCenter,
    Laboratory,
    Pharmacy,
    Other
}

public enum FacilityOwnershipType
{
    Unknown,
    Public,
    Private
}

public enum FacilityVerificationStatus
{
    Discovered,
    RegistryVerified,
    OwnerVerified,
    Onboarded,
    Rejected
}

public enum FacilityClaimStatus
{
    Pending,
    Approved,
    Rejected
}

public enum FacilityImportStatus
{
    Running,
    Succeeded,
    Failed
}

public enum FacilityDuplicateStatus
{
    Pending,
    Merged,
    Distinct,
    Dismissed
}

/// <summary>
/// Canonical public directory entry. A directory entry is deliberately separate
/// from an operational Hospital or Pharmacy: being listed never grants access to
/// PIYA clinical workflows.
/// </summary>
public sealed class DirectoryFacility
{
    public Guid Id { get; set; }
    public DirectoryFacilityKind Kind { get; set; }
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public string? LegalName { get; set; }
    public string? Address { get; set; }
    public string? NormalizedAddress { get; set; }
    public string City { get; set; } = "Baku";
    public string Country { get; set; } = "Azerbaijan";
    public string? District { get; set; }
    public List<string> PhoneNumbers { get; set; } = [];
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? OperatingHours { get; set; }
    public List<string> Services { get; set; } = [];
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public FacilityOwnershipType Ownership { get; set; }
    public FacilityVerificationStatus VerificationStatus { get; set; }
    public string? LicenseNumber { get; set; }
    public string? TaxId { get; set; }
    public string? PrimarySourceName { get; set; }
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? ParentFacilityId { get; set; }
    public DirectoryFacility? ParentFacility { get; set; }
    public ICollection<DirectoryFacility> ChildFacilities { get; set; } = [];
    public Guid? OperationalHospitalId { get; set; }
    public Hospital? OperationalHospital { get; set; }
    public Guid? OperationalPharmacyId { get; set; }
    public Pharmacy? OperationalPharmacy { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<FacilitySourceRecord> Sources { get; set; } = [];
    public ICollection<FacilityClaim> Claims { get; set; } = [];
}

/// <summary>Raw provenance record linking one external source row to a facility.</summary>
public sealed class FacilitySourceRecord
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public DirectoryFacility Facility { get; set; } = null!;
    public required string SourceName { get; set; }
    public required string ExternalId { get; set; }
    public required string SourceUrl { get; set; }
    public string? DataLicense { get; set; }
    public required string PayloadHash { get; set; }
    public string? RawName { get; set; }
    public string? RawAddress { get; set; }
    public string? RawPayload { get; set; }
    public bool IsCurrent { get; set; } = true;
    public DateTime? SourceLastModifiedAt { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}

public sealed class FacilityClaim
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public DirectoryFacility Facility { get; set; } = null!;
    public Guid ClaimedByUserId { get; set; }
    public User ClaimedByUser { get; set; } = null!;
    public required string OrganizationName { get; set; }
    public string? LicenseNumber { get; set; }
    public string? TaxId { get; set; }
    public required string ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? EvidenceNotes { get; set; }
    public FacilityClaimStatus Status { get; set; } = FacilityClaimStatus.Pending;
    public string? ReviewNotes { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class FacilityImportRun
{
    public Guid Id { get; set; }
    public required string SourceName { get; set; }
    public FacilityImportStatus Status { get; set; } = FacilityImportStatus.Running;
    public bool DryRun { get; set; }
    public int RecordsRead { get; set; }
    public int RecordsCreated { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsMatched { get; set; }
    public int DuplicateCandidates { get; set; }
    public int RecordsSkipped { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public sealed class FacilityDuplicateCandidate
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public DirectoryFacility Facility { get; set; } = null!;
    public Guid PossibleDuplicateId { get; set; }
    public DirectoryFacility PossibleDuplicate { get; set; } = null!;
    public int ConfidenceScore { get; set; }
    public required string Reason { get; set; }
    public FacilityDuplicateStatus Status { get; set; } = FacilityDuplicateStatus.Pending;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
