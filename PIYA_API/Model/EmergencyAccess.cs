namespace PIYA_API.Model;

/// <summary>
/// Patient-maintained information intentionally curated for urgent care. The share
/// token is never stored in plaintext and sharing is opt-in and time limited.
/// </summary>
public class EmergencyHealthProfile
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public User Patient { get; set; } = null!;
    public string? BloodType { get; set; }
    public string? Allergies { get; set; }
    public string? ChronicConditions { get; set; }
    public string? CurrentMedications { get; set; }
    public string? EmergencyContacts { get; set; }
    public string? AdditionalNotes { get; set; }
    public bool IsSharingEnabled { get; set; }
    public string? ShareTokenHash { get; set; }
    public DateTime? ShareTokenExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Auditable, revocable access window issued after a licensed doctor presents a
/// patient-controlled emergency token and records a clinical reason.
/// </summary>
public class EmergencyAccessGrant
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public User Patient { get; set; } = null!;
    public Guid RequesterId { get; set; }
    public User Requester { get; set; } = null!;
    public required string Reason { get; set; }
    public string? FacilityName { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
}
