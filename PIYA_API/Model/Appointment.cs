namespace PIYA_API.Model;

/// <summary>
/// Appointment status enumeration
/// </summary>
public enum AppointmentStatus
{
    Scheduled = 1,
    Confirmed = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5,
    NoShow = 6,
    Rescheduled = 7
}

/// <summary>
/// Patient-Doctor appointment booking
/// </summary>
public class Appointment
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Patient who booked the appointment
    /// </summary>
    public Guid PatientId { get; set; }
    public User Patient { get; set; } = null!;
    
    /// <summary>
    /// Doctor for the appointment
    /// </summary>
    public Guid DoctorId { get; set; }
    public User Doctor { get; set; } = null!;
    
    /// <summary>
    /// Hospital where appointment takes place
    /// </summary>
    public Guid HospitalId { get; set; }
    public Hospital Hospital { get; set; } = null!;
    
    /// <summary>
    /// Scheduled date and time
    /// </summary>
    public required DateTime ScheduledAt { get; set; }
    
    /// <summary>
    /// Appointment duration in minutes
    /// </summary>
    public int DurationMinutes { get; set; } = 30;
    
    /// <summary>
    /// Appointment status
    /// </summary>
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;
    
    /// <summary>
    /// Reason for visit
    /// </summary>
    public string? Reason { get; set; }
    
    /// <summary>
    /// Notes from doctor about the appointment
    /// </summary>
    public string? AppointmentNotes { get; set; }
    
    /// <summary>
    /// When the appointment was actually started
    /// </summary>
    public DateTime? ActualStartTime { get; set; }
    
    /// <summary>
    /// When the appointment was completed
    /// </summary>
    public DateTime? ActualEndTime { get; set; }
    
    /// <summary>
    /// Cancellation reason
    /// </summary>
    public string? CancellationReason { get; set; }
    
    /// <summary>
    /// Cancelled by (Patient/Doctor/Admin)
    /// </summary>
    public Guid? CancelledBy { get; set; }
    
    /// <summary>
    /// When the cancellation occurred
    /// </summary>
    public DateTime? CancelledAt { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<Prescription> Prescriptions { get; set; } = [];
    public ICollection<DoctorNote> DoctorNotes { get; set; } = [];

    /// <summary>
    /// Referral that originated this appointment (null for directly booked appointments)
    /// </summary>
    public Guid? ReferralId { get; set; }
    public Referral? Referral { get; set; }

    /// <summary>
    /// When this appointment is rescheduled, points to the newly created replacement appointment.
    /// Allows clients to follow the full reschedule chain (Status == Rescheduled → navigate here).
    /// Null unless Status == Rescheduled.
    /// </summary>
    public Guid? RescheduledToId { get; set; }
    public Appointment? RescheduledTo { get; set; }
}
