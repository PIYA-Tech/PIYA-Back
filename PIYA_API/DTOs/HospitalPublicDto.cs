namespace PIYA_API.DTOs;

/// <summary>
/// Publicly safe hospital data — omits EmergencyContact and other internal fields.
/// Used by the anonymous GET endpoints so callers cannot harvest emergency contact numbers.
/// </summary>
public class HospitalPublicDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Website { get; set; }
    public List<string> Departments { get; set; } = [];
    public bool IsActive { get; set; }
    public CoordinatesDto? Coordinates { get; set; }
    public string? OperatingHours { get; set; }
}
