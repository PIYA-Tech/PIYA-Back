using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Boundary for a contracted identity/insurance provider. Implementations own
/// provider credentials and webhook validation; PIYA stores only references
/// and lifecycle state, never raw verification documents.
/// </summary>
public interface IVerificationProviderGateway
{
    VerificationProviderCapability GetCapability(PatientVerificationKind kind);

    Task<VerificationProviderResult> StartAsync(
        VerificationProviderStartContext context,
        CancellationToken cancellationToken = default);

    Task<VerificationProviderResult> RefreshAsync(
        PatientVerificationKind kind,
        string providerReference,
        CancellationToken cancellationToken = default);
}

public sealed record VerificationProviderCapability(
    bool IsConnected,
    string? ProviderName,
    string Message);

public sealed record VerificationProviderStartContext(
    Guid PatientId,
    PatientVerificationKind Kind,
    string? CountryCode,
    string? DocumentType,
    string? InsuranceIssuer,
    string? PolicyReferenceLastFour);

public sealed record VerificationProviderResult(
    PatientVerificationStatus Status,
    string? ProviderName = null,
    string? ProviderReference = null,
    string? ActionUrl = null,
    string? StatusReasonCode = null,
    DateTime? ExpiresAt = null,
    VerifiedIdentity? Identity = null);

// Transient decision data used only to match the account. Never returned to the
// app or persisted as a second copy of the provider's document/person payload.
public sealed record VerifiedIdentity(Guid SubjectId, string FirstName, string LastName, DateOnly DateOfBirth);
