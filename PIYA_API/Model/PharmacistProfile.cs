namespace PIYA_API.Model;

/// <summary>
/// Pharmacist license status
/// </summary>
public enum PharmacistLicenseStatus
{
    Active = 1,
    Expired = 2,
    Suspended = 3,
    Revoked = 4,
    Pending = 5
}

/// <summary>
/// Extended profile for users with Pharmacist role
/// </summary>
public class PharmacistProfile
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Reference to User entity (Role = Pharmacist)
    /// </summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    /// <summary>
    /// Pharmacy license number
    /// </summary>
    public required string LicenseNumber { get; set; }
    
    /// <summary>
    /// License issuing authority
    /// </summary>
    public string? LicenseAuthority { get; set; }
    
    /// <summary>
    /// License issue date
    /// </summary>
    public DateTime? LicenseIssueDate { get; set; }
    
    /// <summary>
    /// License expiration date
    /// </summary>
    public DateTime? LicenseExpiryDate { get; set; }
    
    /// <summary>
    /// Current license status
    /// </summary>
    public PharmacistLicenseStatus LicenseStatus { get; set; } = PharmacistLicenseStatus.Active;
    
    /// <summary>
    /// Last license verification date
    /// </summary>
    public DateTime? LastVerificationDate { get; set; }
    
    /// <summary>
    /// Next scheduled verification date
    /// </summary>
    public DateTime? NextVerificationDate { get; set; }
    
    /// <summary>
    /// Verification notes/history
    /// </summary>
    public string? VerificationNotes { get; set; }
    
    /// <summary>
    /// Years of pharmacy experience
    /// </summary>
    public int YearsOfExperience { get; set; }
    
    /// <summary>
    /// Pharmacy education details
    /// </summary>
    public List<string> Education { get; set; } = [];
    
    /// <summary>
    /// Certifications and qualifications
    /// </summary>
    public List<string> Certifications { get; set; } = [];
    
    /// <summary>
    /// Languages spoken
    /// </summary>
    public List<string> Languages { get; set; } = [];
    
    /// <summary>
    /// Specialization areas (e.g., Clinical Pharmacy, Pediatric Pharmacy)
    /// </summary>
    public List<string> Specializations { get; set; } = [];
    
    /// <summary>
    /// Professional biography
    /// </summary>
    public string? Biography { get; set; }
    
    /// <summary>
    /// Primary pharmacy ID (main workplace)
    /// </summary>
    public Guid? PrimaryPharmacyId { get; set; }
    public Pharmacy? PrimaryPharmacy { get; set; }
    
    /// <summary>
    /// Whether accepting consultations
    /// </summary>
    public bool AcceptingConsultations { get; set; } = true;
    
    /// <summary>
    /// Total number of consultations provided
    /// </summary>
    public int TotalConsultations { get; set; } = 0;
    
    /// <summary>
    /// Average rating (1-5 stars)
    /// </summary>
    public decimal? AverageRating { get; set; }
    
    /// <summary>
    /// Total number of ratings
    /// </summary>
    public int TotalRatings { get; set; } = 0;
    
    /// <summary>
    /// Automatic license expiry reminders enabled
    /// </summary>
    public bool EnableExpiryReminders { get; set; } = true;
    
    /// <summary>
    /// Days before expiry to send first reminder
    /// </summary>
    public int ReminderDaysBeforeExpiry { get; set; } = 30;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
