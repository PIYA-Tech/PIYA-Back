namespace PIYA_API.Model;

/// <summary>
/// Appointment reminder notifications
/// </summary>
public class AppointmentReminder
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// The appointment this reminder is for
    /// </summary>
    public Guid AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    
    /// <summary>
    /// User receiving the reminder (usually the patient)
    /// </summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    /// <summary>
    /// When to send the reminder
    /// </summary>
    public DateTime ReminderTime { get; set; }
    
    /// <summary>
    /// How far in advance (minutes before appointment)
    /// </summary>
    public int MinutesBeforeAppointment { get; set; }
    
    /// <summary>
    /// Reminder delivery methods
    /// </summary>
    public List<ReminderDeliveryMethod> DeliveryMethods { get; set; } = [];
    
    /// <summary>
    /// Whether the reminder has been sent
    /// </summary>
    public bool IsSent { get; set; } = false;
    
    /// <summary>
    /// When the reminder was actually sent
    /// </summary>
    public DateTime? SentAt { get; set; }
    
    /// <summary>
    /// Delivery status for each method
    /// </summary>
    public string? DeliveryStatus { get; set; }
    
    /// <summary>
    /// Custom reminder message (optional)
    /// </summary>
    public string? CustomMessage { get; set; }
    
    /// <summary>
    /// Number of retry attempts if delivery failed
    /// </summary>
    public int RetryCount { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Prescription refill reminder notifications
/// </summary>
public class PrescriptionRefillReminder
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// The original prescription
    /// </summary>
    public Guid PrescriptionId { get; set; }
    public Prescription Prescription { get; set; } = null!;
    
    /// <summary>
    /// Patient receiving the reminder
    /// </summary>
    public Guid PatientId { get; set; }
    public User Patient { get; set; } = null!;
    
    /// <summary>
    /// When to send the reminder
    /// </summary>
    public DateTime ReminderDate { get; set; }
    
    /// <summary>
    /// Estimated refill date (based on medication duration)
    /// </summary>
    public DateTime EstimatedRefillDate { get; set; }
    
    /// <summary>
    /// How many days before refill to remind
    /// </summary>
    public int DaysBeforeRefill { get; set; }
    
    /// <summary>
    /// Reminder delivery methods
    /// </summary>
    public List<ReminderDeliveryMethod> DeliveryMethods { get; set; } = [];
    
    /// <summary>
    /// Whether the reminder has been sent
    /// </summary>
    public bool IsSent { get; set; } = false;
    
    /// <summary>
    /// When the reminder was sent
    /// </summary>
    public DateTime? SentAt { get; set; }
    
    /// <summary>
    /// Whether the patient acknowledged/dismissed the reminder
    /// </summary>
    public bool IsAcknowledged { get; set; } = false;
    
    /// <summary>
    /// Whether the patient has refilled (new prescription created)
    /// </summary>
    public bool IsRefilled { get; set; } = false;
    
    /// <summary>
    /// New prescription ID if refilled
    /// </summary>
    public Guid? RefillPrescriptionId { get; set; }
    
    /// <summary>
    /// Specific medication items to refill (if not all)
    /// </summary>
    public List<Guid> MedicationItemIds { get; set; } = [];
    
    /// <summary>
    /// Number of retry attempts if delivery failed
    /// </summary>
    public int RetryCount { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Methods for delivering reminders
/// </summary>
public enum ReminderDeliveryMethod
{
    Email,
    SMS,
    PushNotification,
    InApp
}
