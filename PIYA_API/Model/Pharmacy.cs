namespace PIYA_API.Model;

public class Pharmacy
{
    public Guid Id { get; set; }
    public required string Country { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public string? City { get; set; }
    
    /// <summary>
    /// Primary contact phone number
    /// </summary>
    public string? PhoneNumber { get; set; }
    
    /// <summary>
    /// Emergency contact number
    /// </summary>
    public string? EmergencyContact { get; set; }
    
    /// <summary>
    /// Email address for the pharmacy
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Website URL
    /// </summary>
    public string? Website { get; set; }
    
    /// <summary>
    /// Operating hours in JSON format
    /// Example: {"Monday": "08:00-20:00", "Tuesday": "08:00-20:00", ...}
    /// </summary>
    public string? OperatingHours { get; set; }
    
    /// <summary>
    /// Services offered by the pharmacy
    /// Example: ["Prescription Filling", "Vaccination", "Consultation", "Home Delivery"]
    /// </summary>
    public List<string> Services { get; set; } = [];
    
    /// <summary>
    /// Whether the pharmacy is currently active/open for business
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Whether the pharmacy offers 24-hour service
    /// </summary>
    public bool Is24Hours { get; set; } = false;
    
    /// <summary>
    /// Average rating (0-5)
    /// </summary>
    public decimal AverageRating { get; set; } = 0;
    
    /// <summary>
    /// Total number of ratings received
    /// </summary>
    public int TotalRatings { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User? Manager { get; set; }
    public List<User>? Staff { get; set; }
    public required Coordinates Coordinates { get; set; }
    public required PharmacyCompany Company { get; set; }
    public ICollection<PharmacyRating> Ratings { get; set; } = [];
}
