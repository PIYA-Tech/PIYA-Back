namespace PIYA_API.Model;

/// <summary>
/// Medical specialization categories
/// </summary>
public enum MedicalSpecialization
{
    GeneralPractice = 1,
    Cardiology = 2,
    Dermatology = 3,
    Endocrinology = 4,
    Gastroenterology = 5,
    Neurology = 6,
    Obstetrics = 7,
    Gynecology = 8,
    Oncology = 9,
    Ophthalmology = 10,
    Orthopedics = 11,
    Otolaryngology = 12,
    Pediatrics = 13,
    Psychiatry = 14,
    Pulmonology = 15,
    Radiology = 16,
    Surgery = 17,
    Urology = 18,
    Other = 99
}

/// <summary>
/// Doctor availability status
/// </summary>
public enum DoctorAvailabilityStatus
{
    Offline = 0,
    Online = 1,
    Busy = 2,
    OnBreak = 3,
    OnCall = 4
}

/// <summary>
/// Extended profile for users with Doctor role
/// </summary>
public class DoctorProfile
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Reference to User entity (Role = Doctor)
    /// </summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    /// <summary>
    /// Medical license number
    /// </summary>
    public required string LicenseNumber { get; set; }
    
    /// <summary>
    /// License issuing authority
    /// </summary>
    public string? LicenseAuthority { get; set; }
    
    /// <summary>
    /// License expiration date
    /// </summary>
    public DateTime? LicenseExpiryDate { get; set; }
    
    /// <summary>
    /// Primary medical specialization
    /// </summary>
    public MedicalSpecialization Specialization { get; set; }
    
    /// <summary>
    /// Additional specializations
    /// </summary>
    public List<MedicalSpecialization> AdditionalSpecializations { get; set; } = [];
    
    /// <summary>
    /// Years of medical practice
    /// </summary>
    public int YearsOfExperience { get; set; }
    
    /// <summary>
    /// Medical certifications and qualifications
    /// </summary>
    public List<string> Certifications { get; set; } = [];
    
    /// <summary>
    /// Education details (medical school, residency, etc.)
    /// </summary>
    public List<string> Education { get; set; } = [];
    
    /// <summary>
    /// Languages spoken by the doctor
    /// </summary>
    public List<string> Languages { get; set; } = [];
    
    /// <summary>
    /// Professional biography
    /// </summary>
    public string? Biography { get; set; }
    
    /// <summary>
    /// Consultation fee (in local currency)
    /// </summary>
    public decimal? ConsultationFee { get; set; }
    
    /// <summary>
    /// Whether the doctor is accepting new patients
    /// </summary>
    public bool AcceptingNewPatients { get; set; } = true;
    
    /// <summary>
    /// Current availability status (Online/Offline/Busy)
    /// </summary>
    public DoctorAvailabilityStatus CurrentStatus { get; set; } = DoctorAvailabilityStatus.Offline;
    
    /// <summary>
    /// Last time the doctor was online
    /// </summary>
    public DateTime? LastOnlineAt { get; set; }
    
    /// <summary>
    /// Associated hospitals (many-to-many relationship)
    /// </summary>
    public List<Guid> HospitalIds { get; set; } = [];
    
    /// <summary>
    /// Working hours configuration (JSON format)
    /// Example: { "Monday": [{"start": "09:00", "end": "17:00"}], ... }
    /// </summary>
    public string? WorkingHours { get; set; }
    
    /// <summary>
    /// Average appointment duration in minutes
    /// </summary>
    public int AverageAppointmentDuration { get; set; } = 30;
    
    /// <summary>
    /// Total number of patients treated
    /// </summary>
    public int TotalPatientsTreated { get; set; } = 0;
    
    /// <summary>
    /// Average rating (1-5 stars)
    /// </summary>
    public decimal? AverageRating { get; set; }
    
    /// <summary>
    /// Total number of ratings
    /// </summary>
    public int TotalRatings { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Working hours for a specific day
/// </summary>
public class WorkingHoursSlot
{
    public string DayOfWeek { get; set; } = string.Empty; // Monday, Tuesday, etc.
    public List<TimeSlot> Slots { get; set; } = [];
}

/// <summary>
/// Time slot for working hours
/// </summary>
public class TimeSlot
{
    public string Start { get; set; } = string.Empty; // HH:mm format
    public string End { get; set; } = string.Empty; // HH:mm format
}
