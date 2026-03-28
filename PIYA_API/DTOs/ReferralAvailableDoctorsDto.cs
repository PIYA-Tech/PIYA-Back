namespace PIYA_API.DTOs;

/// <summary>
/// Response for GET /api/referral/{id}/available-doctors.
/// Carries enough context for the client to distinguish between
/// two different empty-list scenarios:
///
///   • specialtyRegistered=false  → nobody in the system practices this specialty yet
///   • specialtyRegistered=true, allAtCapacity=true → doctors exist but all have
///     AcceptingNewPatients = false
///
/// This prevents the confusing "empty list with no explanation" UX.
/// </summary>
public sealed class ReferralAvailableDoctorsDto
{
    /// <summary>Doctors that match the specialty AND are accepting new patients.</summary>
    public List<AvailableDoctorDto> Doctors { get; init; } = [];

    /// <summary>
    /// Total number of registered doctors with the required specialty,
    /// regardless of capacity. 0 means the specialty is entirely absent from
    /// the platform.
    /// </summary>
    public int TotalInSpecialty { get; init; }

    /// <summary>
    /// True when at least one doctor with the required specialty exists in the system.
    /// False means the platform has no doctors registered under this specialty at all.
    /// </summary>
    public bool SpecialtyRegistered => TotalInSpecialty > 0;

    /// <summary>
    /// True when the specialty is registered but every doctor has
    /// AcceptingNewPatients = false (Doctors list will be empty in this case).
    /// </summary>
    public bool AllAtCapacity => SpecialtyRegistered && Doctors.Count == 0;

    /// <summary>The specialty code the search was performed against.</summary>
    public string Specialty { get; init; } = string.Empty;
}

/// <summary>Slim projection of a doctor user for referral doctor-picker UI.</summary>
public sealed class AvailableDoctorDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string Specialization { get; init; } = string.Empty;
}
