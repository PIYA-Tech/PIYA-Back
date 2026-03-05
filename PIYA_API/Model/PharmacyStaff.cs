namespace PIYA_API.Model;

/// <summary>
/// Represents the relationship between a pharmacist and a pharmacy
/// Allows tracking which pharmacists work at which pharmacies
/// </summary>
public class PharmacyStaff
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// The pharmacy where the staff member works
    /// </summary>
    public Guid PharmacyId { get; set; }
    public Pharmacy Pharmacy { get; set; } = null!;
    
    /// <summary>
    /// The user (Pharmacist or PharmacyManager)
    /// </summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    /// <summary>
    /// Role at this specific pharmacy (Staff, Manager)
    /// </summary>
    public PharmacyStaffRole Role { get; set; }
    
    /// <summary>
    /// When the staff member was assigned to this pharmacy
    /// </summary>
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the assignment ends (null = indefinite)
    /// </summary>
    public DateTime? AssignmentEndsAt { get; set; }
    
    /// <summary>
    /// Whether the staff member is currently active at this pharmacy
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Work schedule in JSON format (optional)
    /// Example: {"Monday": "9:00-17:00", "Tuesday": "9:00-17:00"}
    /// </summary>
    public string? WorkSchedule { get; set; }
    
    /// <summary>
    /// Permissions specific to this pharmacy
    /// </summary>
    public List<string> Permissions { get; set; } = [];
    
    /// <summary>
    /// Notes about this staff assignment
    /// </summary>
    public string? Notes { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Role of a staff member at a specific pharmacy
/// </summary>
public enum PharmacyStaffRole
{
    /// <summary>
    /// Regular pharmacy staff member
    /// </summary>
    Staff = 1,
    
    /// <summary>
    /// Pharmacy manager with elevated permissions
    /// </summary>
    Manager = 2,
    
    /// <summary>
    /// Inventory manager responsible for stock
    /// </summary>
    InventoryManager = 3,
    
    /// <summary>
    /// Temporary/Part-time staff
    /// </summary>
    PartTime = 4
}
