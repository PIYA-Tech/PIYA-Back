namespace PIYA_API.Model;

/// <summary>
/// How this referral was initiated
/// </summary>
public enum ReferralOrigin
{
    Appointment = 1,  // Created from a scheduled appointment
    Emergency   = 2,  // Walk-in / ambulance — no prior appointment
    Internal    = 3,  // Internally forwarded from another referral (chain)
}

/// <summary>
/// Status of a referral
/// </summary>
public enum ReferralStatus
{
    Pending    = 1,   // Created, waiting for patient to pick a doctor (if no doctor assigned) or waiting for referred doctor to accept
    Scheduled  = 2,   // Appointment auto-created
    Accepted   = 3,   // Referred doctor accepted
    Completed  = 4,   // Referred doctor filled in result notes
    Declined   = 5,   // Referred doctor declined — patient may reassign
    Cancelled  = 6    // Referring doctor or patient cancelled
}

/// <summary>
/// Urgency level for a referral
/// </summary>
public enum ReferralUrgency
{
    Routine   = 1,
    Urgent    = 2,
    Emergency = 3
}

/// <summary>
/// A referral from one doctor (or from a general practitioner) to a specialist or external provider.
/// </summary>
public class Referral
{
    public Guid Id { get; set; }

    // ── Referring side ───────────────────────────────────────────────────────

    /// <summary>
    /// Doctor who issued this referral
    /// </summary>
    public Guid ReferringDoctorId { get; set; }
    public User ReferringDoctor { get; set; } = null!;

    /// <summary>
    /// How the referral was initiated.
    /// </summary>
    public ReferralOrigin Origin { get; set; } = ReferralOrigin.Appointment;

    /// <summary>
    /// The appointment from which this referral was created.
    /// Null for Emergency/walk-in referrals (no prior appointment exists).
    /// </summary>
    public Guid? SourceAppointmentId { get; set; }
    public Appointment? SourceAppointment { get; set; }

    /// <summary>
    /// Parent referral when this is a forward/chain referral (specialist refers on to another specialist).
    /// Null for first-level referrals.
    /// </summary>
    public Guid? ParentReferralId { get; set; }
    public Referral? ParentReferral { get; set; }

    /// <summary>
    /// Child referrals forwarded from this one (the referral chain).
    /// </summary>
    public ICollection<Referral> ChildReferrals { get; set; } = [];

    // ── Patient ──────────────────────────────────────────────────────────────

    public Guid PatientId { get; set; }
    public User Patient { get; set; } = null!;

    // ── Referred-to side ─────────────────────────────────────────────────────

    /// <summary>
    /// Required specialty — the patient will book with a doctor of this specialty.
    /// </summary>
    public MedicalSpecialization ReferredToSpecialty { get; set; }

    /// <summary>
    /// Specific doctor to refer to. Null = patient picks from the filtered list.
    /// </summary>
    public Guid? ReferredToDoctorId { get; set; }
    public User? ReferredToDoctor { get; set; }

    /// <summary>
    /// The appointment created/linked as a result of this referral.
    /// </summary>
    public Guid? ResultAppointmentId { get; set; }
    public Appointment? ResultAppointment { get; set; }

    // ── Status & urgency ─────────────────────────────────────────────────────

    public ReferralStatus Status { get; set; } = ReferralStatus.Pending;
    public ReferralUrgency Urgency { get; set; } = ReferralUrgency.Routine;

    // ── Content ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Patient-visible reason for referral
    /// </summary>
    public required string Reason { get; set; }

    /// <summary>
    /// Doctor-only clinical notes (not shown to patient in the letter)
    /// </summary>
    public string? ClinicalNotes { get; set; }

    /// <summary>
    /// Notes added by the receiving doctor when marking the referral complete
    /// </summary>
    public string? ResultNotes { get; set; }

    // ── External provider ────────────────────────────────────────────────────

    /// <summary>
    /// True when the referral is to an external provider outside the system
    /// </summary>
    public bool IsExternal { get; set; } = false;

    public string? ExternalProviderName { get; set; }
    public string? ExternalProviderContact { get; set; }

    // ── Timestamps ───────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // ── Navigation ───────────────────────────────────────────────────────────

    public ICollection<MedicalTest> Tests { get; set; } = [];
}
