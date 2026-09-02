using PIYA_API.Model;

namespace PIYA_API.DTOs;

public sealed record VerificationCapabilityResponse(
    PatientVerificationKind Kind,
    bool IsConnected,
    string? ProviderName,
    string Message);

public sealed record StartPatientVerificationRequest(
    bool PrivacyNoticeAccepted,
    string? CountryCode = null,
    string? DocumentType = null,
    string? InsuranceIssuer = null,
    string? PolicyReferenceLastFour = null);

public sealed record PatientVerificationResponse(
    Guid Id,
    PatientVerificationKind Kind,
    PatientVerificationStatus Status,
    string? ProviderName,
    string? ActionUrl,
    string? CountryCode,
    string? DocumentType,
    string? InsuranceIssuer,
    string? PolicyReferenceLastFour,
    string? StatusReasonCode,
    DateTime? SubmittedAt,
    DateTime? VerifiedAt,
    DateTime? ExpiresAt,
    DateTime UpdatedAt)
{
    public static PatientVerificationResponse From(PatientVerification value) => new(
        value.Id, value.Kind, value.Status, value.ProviderName, value.ActionUrl,
        value.CountryCode, value.DocumentType, value.InsuranceIssuer,
        value.PolicyReferenceLastFour, value.StatusReasonCode, value.SubmittedAt,
        value.VerifiedAt, value.ExpiresAt, value.UpdatedAt);
}

public sealed record CreateCareCircleInvitationRequest(
    string InviteeEmail,
    CareCircleRole Role,
    CareCircleScope Scopes,
    DateTime? ExpiresAt = null);

public sealed record CareCircleInvitationCreatedResponse(
    CareCircleInvitationResponse Invitation,
    string InvitationToken);

public sealed record CareCircleInvitationResponse(
    Guid Id,
    string InviteeEmail,
    CareCircleRole Role,
    CareCircleScope Scopes,
    CareCircleInvitationStatus Status,
    DateTime InvitationExpiresAt,
    DateTime AccessExpiresAt,
    DateTime CreatedAt);

public sealed record AcceptCareCircleInvitationRequest(string Token, bool TermsAccepted);

public sealed record DeclineCareCircleInvitationRequest(string Token);

public sealed record UpdateCareCircleMemberRequest(
    CareCircleRole Role,
    CareCircleScope Scopes,
    DateTime ExpiresAt);

public sealed record RevokeCareCircleMemberRequest(string? Reason = null);

public sealed record CareCircleMemberResponse(
    Guid Id,
    Guid PatientId,
    Guid MemberUserId,
    string DisplayName,
    string Email,
    CareCircleRole Role,
    CareCircleScope Scopes,
    CareCircleMemberStatus Status,
    DateTime JoinedAt,
    DateTime ExpiresAt);

public sealed record UpsertConsultationSummaryRequest(
    string Summary,
    string? Diagnosis = null,
    string? CareInstructions = null,
    string? WarningSigns = null,
    string? PatientMessage = null);

public sealed record AmendConsultationSummaryRequest(
    string Summary,
    string AmendmentReason,
    string? Diagnosis = null,
    string? CareInstructions = null,
    string? WarningSigns = null,
    string? PatientMessage = null);

public sealed record ConsultationSummaryResponse(
    Guid Id,
    Guid AppointmentId,
    Guid PatientId,
    Guid DoctorId,
    string DoctorName,
    string HospitalName,
    DateTime AppointmentAt,
    ConsultationSummaryStatus Status,
    string Summary,
    string? Diagnosis,
    string? CareInstructions,
    string? WarningSigns,
    string? PatientMessage,
    string? LastAmendmentReason,
    int Version,
    DateTime? PublishedAt,
    DateTime UpdatedAt);

public sealed record CreateFollowUpPlanRequest(
    string Reason,
    DateTime EarliestAt,
    DateTime LatestAt,
    int DurationMinutes = 30);

public sealed record ScheduleFollowUpRequest(DateTime ScheduledAt);

public sealed record DeclineFollowUpRequest(string? Reason = null);

public sealed record FollowUpPlanResponse(
    Guid Id,
    Guid ConsultationSummaryId,
    Guid SourceAppointmentId,
    Guid PatientId,
    Guid DoctorId,
    Guid HospitalId,
    string DoctorName,
    string HospitalName,
    string Reason,
    DateTime EarliestAt,
    DateTime LatestAt,
    int DurationMinutes,
    FollowUpStatus Status,
    Guid? ScheduledAppointmentId,
    DateTime? ScheduledAt,
    string? DeclineReason,
    DateTime UpdatedAt);

public sealed record CreateCareLoopTaskRequest(
    CareLoopTaskType Type,
    string Title,
    string? Instructions,
    DateTime DueAt);

public sealed record CreateCareLoopWorkflowRequest(
    Guid? AppointmentId,
    Guid? ConsultationSummaryId,
    string Title,
    string? Description,
    IReadOnlyList<CreateCareLoopTaskRequest> Tasks);

public sealed record CompleteCareLoopTaskRequest(string? PatientResponse = null);

public sealed record SkipCareLoopTaskRequest(string? Reason = null);

public sealed record CareLoopTaskResponse(
    Guid Id,
    CareLoopTaskType Type,
    string Title,
    string? Instructions,
    DateTime DueAt,
    CareLoopTaskStatus Status,
    string? PatientResponse,
    DateTime? CompletedAt,
    DateTime? SkippedAt,
    string? SkipReason);

public sealed record CareLoopWorkflowResponse(
    Guid Id,
    Guid PatientId,
    Guid DoctorId,
    string DoctorName,
    Guid? AppointmentId,
    Guid? ConsultationSummaryId,
    string Title,
    string? Description,
    string Source,
    CareLoopStatus Status,
    DateTime? ActivatedAt,
    DateTime? CompletedAt,
    DateTime UpdatedAt,
    IReadOnlyList<CareLoopTaskResponse> Tasks);
