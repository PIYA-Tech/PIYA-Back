using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Safe production default. It makes the unconfigured state visible to clients
/// and can never accidentally claim that a patient is verified.
/// </summary>
public sealed class NotConnectedVerificationProviderGateway : IVerificationProviderGateway
{
    private const string Message =
        "PIYA has not connected a verification provider yet. No verification was performed.";

    public VerificationProviderCapability GetCapability(PatientVerificationKind kind) =>
        new(false, null, Message);

    public Task<VerificationProviderResult> StartAsync(
        VerificationProviderStartContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new VerificationProviderResult(
            PatientVerificationStatus.NotConnected,
            StatusReasonCode: "provider_not_connected"));

    public Task<VerificationProviderResult> RefreshAsync(
        PatientVerificationKind kind,
        string providerReference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new VerificationProviderResult(
            PatientVerificationStatus.NotConnected,
            StatusReasonCode: "provider_not_connected"));
}
