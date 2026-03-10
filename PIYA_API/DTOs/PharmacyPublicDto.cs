namespace PIYA_API.DTOs;

/// <summary>
/// Publicly safe pharmacy data — omits EmergencyContact, Manager/Staff nav props, internal fields.
/// </summary>
public class PharmacyPublicDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? OperatingHours { get; set; }
    public List<string> Services { get; set; } = [];
    public bool IsActive { get; set; }
    public bool Is24Hours { get; set; }
    public decimal AverageRating { get; set; }
    public int TotalRatings { get; set; }
    public CoordinatesDto? Coordinates { get; set; }
    public string? CompanyName { get; set; }
}
