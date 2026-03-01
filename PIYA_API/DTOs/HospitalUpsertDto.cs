namespace PIYA_API.DTOs;

/// <summary>
/// DTO for creating or updating a hospital.
/// Uses camelCase-friendly property names and a flat CoordinatesDto
/// so the frontend can send { lat, lng } without needing to know
/// about the internal Coordinates entity.
/// </summary>
public class HospitalUpsertDto
{
    public required string Name { get; set; }
    public required string Address { get; set; }
    public required string City { get; set; }
    public required string Country { get; set; }
    public required string PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public List<string>? Departments { get; set; }
    public string? EmergencyContact { get; set; }
    public CoordinatesDto? Coordinates { get; set; }
    public string? OperatingHours { get; set; }
}

public class CoordinatesDto
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}
