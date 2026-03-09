namespace PIYA_API.Model;

/// <summary>
/// Type of medical test ordered in a referral
/// </summary>
public enum MedicalTestType
{
    ECG            = 1,
    Echocardiogram = 2,
    BloodPanel     = 3,
    MRI            = 4,
    CTScan         = 5,
    XRay           = 6,
    Ultrasound     = 7,
    UrineTest      = 8,
    StressTest     = 9,
    Biopsy         = 10,
    Other          = 99
}

/// <summary>
/// Lifecycle status of a single medical test
/// </summary>
public enum MedicalTestStatus
{
    Ordered      = 1,
    InProgress   = 2,
    ResultsReady = 3,
    Reviewed     = 4,
    Cancelled    = 5
}

/// <summary>
/// A single medical test ordered as part of a referral
/// </summary>
public class MedicalTest
{
    public Guid Id { get; set; }

    /// <summary>
    /// Parent referral
    /// </summary>
    public Guid ReferralId { get; set; }
    public Referral Referral { get; set; } = null!;

    /// <summary>
    /// Appointment where the test was actually performed (may differ from the referral result appointment)
    /// </summary>
    public Guid? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }

    /// <summary>
    /// Doctor who ordered the test (the referring doctor)
    /// </summary>
    public Guid OrderedByDoctorId { get; set; }
    public User OrderedByDoctor { get; set; } = null!;

    /// <summary>
    /// Doctor who performed / interpreted the test
    /// </summary>
    public Guid? PerformedByDoctorId { get; set; }
    public User? PerformedByDoctor { get; set; }

    public MedicalTestType TestType { get; set; }
    public MedicalTestStatus Status { get; set; } = MedicalTestStatus.Ordered;

    /// <summary>
    /// Order notes / instructions from the referring doctor
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Result / findings written by the performing doctor
    /// </summary>
    public string? Findings { get; set; }

    public DateTime? PerformedAt { get; set; }
    public DateTime? ResultsAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Documents (lab reports, images, etc.) attached to this test result
    /// </summary>
    public ICollection<MedicalDocument> Documents { get; set; } = [];
}
