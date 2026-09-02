namespace PIYA_API.Model;

public enum PatientMedicationSource
{
    PatientEntered = 1,
    Prescription = 2,
    CareTeam = 3
}

public enum PatientMedicationStatus
{
    Active = 1,
    Paused = 2,
    Completed = 3,
    Archived = 4
}

[Flags]
public enum MedicationScheduleDays
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
    EveryDay = Monday | Tuesday | Wednesday | Thursday | Friday | Saturday | Sunday
}

public enum MedicationDoseStatus
{
    Scheduled = 1,
    ReminderSent = 2,
    Taken = 3,
    Skipped = 4,
    Missed = 5
}

/// <summary>A medication the patient actively tracks in PIYA.</summary>
public sealed class PatientMedication
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public User Patient { get; set; } = null!;
    public Guid? MedicationId { get; set; }
    public Medication? Medication { get; set; }
    public Guid? PrescriptionItemId { get; set; }
    public PrescriptionItem? PrescriptionItem { get; set; }
    public PatientMedicationSource Source { get; set; }
    public PatientMedicationStatus Status { get; set; } = PatientMedicationStatus.Active;
    public required string DisplayName { get; set; }
    public string? GenericName { get; set; }
    public string? Strength { get; set; }
    public string? Form { get; set; }
    public required string Dosage { get; set; }
    public string? Instructions { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal SupplyTotal { get; set; }
    public decimal SupplyRemaining { get; set; }
    public required string SupplyUnit { get; set; }
    public decimal LowSupplyThreshold { get; set; }
    public DateTime? LastRefilledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<MedicationDoseSchedule> Schedules { get; set; } = [];
    public ICollection<MedicationDoseOccurrence> DoseOccurrences { get; set; } = [];
}

/// <summary>A local-time dose rule. NextReminderAt makes reminder processing bounded and indexable.</summary>
public sealed class MedicationDoseSchedule
{
    public Guid Id { get; set; }
    public Guid PatientMedicationId { get; set; }
    public PatientMedication PatientMedication { get; set; } = null!;
    public decimal DoseAmount { get; set; }
    public int TimeOfDayMinutes { get; set; }
    public MedicationScheduleDays DaysOfWeek { get; set; } = MedicationScheduleDays.EveryDay;
    public required string TimeZoneId { get; set; }
    public bool RemindersEnabled { get; set; } = true;
    public int GracePeriodMinutes { get; set; } = 120;
    public DateTime? NextReminderAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<MedicationDoseOccurrence> Occurrences { get; set; } = [];
}

/// <summary>One scheduled dose and its immutable patient adherence outcome.</summary>
public sealed class MedicationDoseOccurrence
{
    public Guid Id { get; set; }
    public Guid ScheduleId { get; set; }
    public MedicationDoseSchedule Schedule { get; set; } = null!;
    public Guid PatientMedicationId { get; set; }
    public PatientMedication PatientMedication { get; set; } = null!;
    public Guid PatientId { get; set; }
    public User Patient { get; set; } = null!;
    public DateTime ScheduledFor { get; set; }
    public MedicationDoseStatus Status { get; set; } = MedicationDoseStatus.Scheduled;
    public DateTime? ReminderSentAt { get; set; }
    public DateTime? RecordedAt { get; set; }
    public decimal SupplyDeducted { get; set; }
    public Guid? ClientEventId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum PharmacyPickupStatus
{
    Collected = 1,
    Reversed = 2
}

/// <summary>Durable proof that a ready refill was handed over by a connected pharmacy.</summary>
public sealed class PharmacyPickup
{
    public Guid Id { get; set; }
    public Guid RefillRequestId { get; set; }
    public PatientRefillRequest RefillRequest { get; set; } = null!;
    public Guid PatientId { get; set; }
    public User Patient { get; set; } = null!;
    public Guid PharmacyId { get; set; }
    public Pharmacy Pharmacy { get; set; } = null!;
    public Guid PrescriptionItemId { get; set; }
    public PrescriptionItem PrescriptionItem { get; set; } = null!;
    public Guid CollectedByUserId { get; set; }
    public User CollectedByUser { get; set; } = null!;
    public decimal QuantityCollected { get; set; }
    public PharmacyPickupStatus Status { get; set; } = PharmacyPickupStatus.Collected;
    public DateTime CollectedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Append-only lifecycle history for a patient refill request.</summary>
public sealed class PatientRefillStatusEvent
{
    public Guid Id { get; set; }
    public Guid RefillRequestId { get; set; }
    public PatientRefillRequest RefillRequest { get; set; } = null!;
    public PatientRefillRequestStatus Status { get; set; }
    public Guid ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;
    public string? Note { get; set; }
    public DateTime? EstimatedReadyAt { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public enum LabAnalyteFlag
{
    Unknown = 0,
    Normal = 1,
    Low = 2,
    High = 3,
    Abnormal = 4,
    Critical = 5
}

/// <summary>A structured observation within an existing MedicalTest result.</summary>
public sealed class MedicalTestAnalyteResult
{
    public Guid Id { get; set; }
    public Guid MedicalTestId { get; set; }
    public MedicalTest MedicalTest { get; set; } = null!;
    public required string Code { get; set; }
    public required string Name { get; set; }
    public decimal? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public string? Unit { get; set; }
    public decimal? ReferenceLow { get; set; }
    public decimal? ReferenceHigh { get; set; }
    public string? ReferenceText { get; set; }
    public LabAnalyteFlag Flag { get; set; }
    public int SortOrder { get; set; }
    public DateTime? ObservedAt { get; set; }
    public Guid EnteredByUserId { get; set; }
    public User EnteredByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum PatientNotificationCategory
{
    MedicationReminder = 1,
    Refill = 2,
    Pickup = 3,
    LabResult = 4,
    Appointment = 5,
    Security = 6,
    System = 7
}

/// <summary>A persistent, user-owned notification for the in-app inbox.</summary>
public sealed class PatientInboxNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public PatientNotificationCategory Category { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? ActionRoute { get; set; }
    public string? DataJson { get; set; }
    public string? DedupeKey { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
