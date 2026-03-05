namespace PIYA_API.Model;

/// <summary>
/// Hospital entity for medical facilities
/// </summary>
public class Hospital
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public required string City { get; set; }
    public required string Country { get; set; }
    public required string PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    
    /// <summary>
    /// Hospital departments (e.g., Cardiology, Pediatrics)
    /// </summary>
    public List<string> Departments { get; set; } = [];
    
    /// <summary>
    /// Emergency contact number
    /// </summary>
    public string? EmergencyContact { get; set; }
    
    /// <summary>
    /// Whether the hospital is currently accepting patients
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Coordinates for geolocation
    /// </summary>
    public Coordinates? Coordinates { get; set; }
    
    /// <summary>
    /// Operating hours (JSON format)
    /// </summary>
    public string? OperatingHours { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<Appointment> Appointments { get; set; } = [];
}
