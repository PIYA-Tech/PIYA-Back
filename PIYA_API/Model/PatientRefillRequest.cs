namespace PIYA_API.Model;

/// <summary>
/// Lifecycle of a patient-initiated prescription refill request.
/// </summary>
public enum PatientRefillRequestStatus
{
    Pending = 1,
    Accepted = 2,
    Ready = 3,
    Collected = 4,
    Declined = 5,
    Cancelled = 6
}

/// <summary>
/// A request from a patient to a PIYA-connected pharmacy to prepare one
/// unfulfilled item from an active digital prescription.
/// </summary>
public class PatientRefillRequest
{
    public Guid Id { get; set; }

    public Guid PatientId { get; set; }
    public User Patient { get; set; } = null!;

    public Guid PrescriptionId { get; set; }
    public Prescription Prescription { get; set; } = null!;

    public Guid PrescriptionItemId { get; set; }
    public PrescriptionItem PrescriptionItem { get; set; } = null!;

    public Guid PharmacyId { get; set; }
    public Pharmacy Pharmacy { get; set; } = null!;

    public PatientRefillRequestStatus Status { get; set; } = PatientRefillRequestStatus.Pending;
    public bool AutoRefill { get; set; }
    public DateTime? EstimatedReadyAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
