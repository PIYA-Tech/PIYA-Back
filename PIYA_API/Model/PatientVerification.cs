namespace PIYA_API.Model;

/// <summary>The real-world subject a provider is being asked to verify.</summary>
public enum PatientVerificationKind
{
    Identity = 1,
    Insurance = 2
}

/// <summary>
/// Provider-controlled verification lifecycle. PIYA never promotes a case to
/// <see cref="Verified"/> without a connected provider reporting that state.
/// </summary>
public enum PatientVerificationStatus
{
    NotConnected = 1,
    Pending = 2,
    RequiresAction = 3,
    Verified = 4,
    Rejected = 5,
    Expired = 6,
    Cancelled = 7
}

/// <summary>
/// A provider-backed patient verification case. Raw identity documents,
/// insurance numbers, and provider secrets must remain with the provider and
/// are deliberately not persisted here.
/// </summary>
public sealed class PatientVerification
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public PatientVerificationKind Kind { get; set; }
    public PatientVerificationStatus Status { get; set; } = PatientVerificationStatus.NotConnected;
    public string? ProviderName { get; set; }
    public string? ProviderReference { get; set; }
    public string? ActionUrl { get; set; }
    public string? CountryCode { get; set; }
    public string? DocumentType { get; set; }
    public string? InsuranceIssuer { get; set; }
    public string? PolicyReferenceLastFour { get; set; }
    public string? StatusReasonCode { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
