namespace PIYA_API.DTOs;

/// <summary>
/// Request body for admin hospital-assignment endpoints.
/// </summary>
public sealed class AssignHospitalsRequest
{
    /// <summary>
    /// Full list of hospital IDs to assign to the doctor.
    /// Replaces the current list entirely.
    /// Pass an empty array to remove the doctor from all hospitals.
    /// </summary>
    public List<Guid> HospitalIds { get; set; } = [];
}
