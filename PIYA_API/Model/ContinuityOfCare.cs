namespace PIYA_API.Model;

public enum ConsultationSummaryStatus
{
    Draft = 1,
    Published = 2
}

/// <summary>Clinician-authored, patient-visible summary of a completed visit.</summary>
public sealed class ConsultationSummary
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public ConsultationSummaryStatus Status { get; set; } = ConsultationSummaryStatus.Draft;
    public string Summary { get; set; } = string.Empty;
    public string? Diagnosis { get; set; }
    public string? CareInstructions { get; set; }
    public string? WarningSigns { get; set; }
    public string? PatientMessage { get; set; }
    public string? LastAmendmentReason { get; set; }
    public int Version { get; set; } = 1;
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum FollowUpStatus
{
    Recommended = 1,
    Scheduled = 2,
    Declined = 3,
    Cancelled = 4,
    Completed = 5
}

/// <summary>A clinician recommendation that the patient can schedule directly.</summary>
public sealed class FollowUpPlan
{
    public Guid Id { get; set; }
    public Guid ConsultationSummaryId { get; set; }
    public Guid SourceAppointmentId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid HospitalId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime EarliestAt { get; set; }
    public DateTime LatestAt { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public FollowUpStatus Status { get; set; } = FollowUpStatus.Recommended;
    public Guid? ScheduledAppointmentId { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? DeclinedAt { get; set; }
    public string? DeclineReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum CareLoopStatus
{
    Draft = 1,
    Active = 2,
    Paused = 3,
    Completed = 4,
    Cancelled = 5
}

public enum CareLoopTaskType
{
    MedicationCheckIn = 1,
    SymptomCheck = 2,
    TestReminder = 3,
    AppointmentReminder = 4,
    RefillReview = 5,
    FollowUpBooking = 6,
    Education = 7
}

public enum CareLoopTaskStatus
{
    Scheduled = 1,
    Available = 2,
    Completed = 3,
    Skipped = 4,
    Overdue = 5,
    Cancelled = 6
}

/// <summary>
/// A clinician-authored plan whose timing and task lifecycle are automated by
/// PIYA. It is never presented as autonomous diagnosis or medical advice.
/// </summary>
public sealed class CareLoopWorkflow
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? ConsultationSummaryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Source { get; set; } = "ClinicianAuthored";
    public CareLoopStatus Status { get; set; } = CareLoopStatus.Draft;
    public DateTime? ActivatedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CareLoopTask
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public CareLoopTaskType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public DateTime DueAt { get; set; }
    public CareLoopTaskStatus Status { get; set; } = CareLoopTaskStatus.Scheduled;
    public string? PatientResponse { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? SkippedAt { get; set; }
    public string? SkipReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
