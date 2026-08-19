namespace PIYA_API.DTOs;

/// <summary>
/// DTO for creating or updating a pharmacy.
/// Accepts { lat, lng } for coordinates instead of the full Coordinates entity,
/// and omits navigation properties (Company, Staff, etc.) that are managed separately.
/// </summary>
public class PharmacyUpsertDto
{
    /// <summary>The owning pharmacy company. Required when creating a pharmacy.</summary>
    public Guid? CompanyId { get; set; }
    public required string Name { get; set; }
    public required string Country { get; set; }
    public required string Address { get; set; }
    public string? City { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? EmergencyContact { get; set; }
    public List<string>? Services { get; set; }
    public string? OperatingHours { get; set; }
    public bool? IsActive { get; set; }
    public bool? Is24Hours { get; set; }
    public CoordinatesDto? Coordinates { get; set; }
}
