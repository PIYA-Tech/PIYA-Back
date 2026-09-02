namespace PIYA_API.Model;

public enum CareCircleRole
{
    Family = 1,
    Caregiver = 2,
    EmergencyContact = 3
}

[Flags]
public enum CareCircleScope
{
    None = 0,
    Appointments = 1,
    Medications = 2,
    CareTimeline = 4,
    EmergencyProfile = 8,
    Documents = 16
}

public enum CareCircleInvitationStatus
{
    Pending = 1,
    Accepted = 2,
    Declined = 3,
    Revoked = 4,
    Expired = 5
}

public enum CareCircleMemberStatus
{
    Active = 1,
    Revoked = 2,
    Left = 3,
    Expired = 4
}

public enum CareCircleConsentStatus
{
    Active = 1,
    Revoked = 2,
    Expired = 3
}

/// <summary>
/// Patient-created invitation. Only a SHA-256 token hash is retained; the
/// plaintext token is returned once to the patient for secure sharing.
/// </summary>
public sealed class CareCircleInvitation
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string InviteeEmailNormalized { get; set; } = string.Empty;
    public CareCircleRole Role { get; set; }
    public CareCircleScope RequestedScopes { get; set; }
    public CareCircleInvitationStatus Status { get; set; } = CareCircleInvitationStatus.Pending;
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>When the one-time invitation token stops being valid.</summary>
    public DateTime ExpiresAt { get; set; }
    /// <summary>When accepted record-sharing consent should expire.</summary>
    public DateTime AccessExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? DeclinedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RespondedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A relationship accepted by the invited user.</summary>
public sealed class CareCircleMember
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid MemberUserId { get; set; }
    public Guid InvitationId { get; set; }
    public CareCircleRole Role { get; set; }
    public CareCircleMemberStatus Status { get; set; } = CareCircleMemberStatus.Active;
    public DateTime JoinedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public DateTime? LeftAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Immutable consent grant for a member. Changing access revokes the current
/// row and appends a new grant, preserving the consent history.
/// </summary>
public sealed class CareCircleConsent
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid MemberId { get; set; }
    public CareCircleScope Scopes { get; set; }
    public CareCircleConsentStatus Status { get; set; } = CareCircleConsentStatus.Active;
    public DateTime GrantedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
