using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

public interface IHospitalService
{
    /// <summary>
    /// Get all hospitals
    /// </summary>
    Task<List<Hospital>> GetAllAsync();
    
    /// <summary>
    /// Get hospital by ID
    /// </summary>
    Task<Hospital?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Search hospitals by city
    /// </summary>
    Task<List<Hospital>> GetByCityAsync(string city);
    
    /// <summary>
    /// Search hospitals by department
    /// </summary>
    Task<List<Hospital>> GetByDepartmentAsync(string department);
    
    /// <summary>
    /// Get active hospitals
    /// </summary>
    Task<List<Hospital>> GetActiveHospitalsAsync();
    
    /// <summary>
    /// Create a new hospital
    /// </summary>
    Task<Hospital> CreateAsync(Hospital hospital);
    
    /// <summary>
    /// Update hospital information
    /// </summary>
    Task<Hospital> UpdateAsync(Hospital hospital);
    
    /// <summary>
    /// Delete a hospital
    /// </summary>
    Task<bool> DeleteAsync(Guid id);
    
    /// <summary>
    /// Deactivate a hospital (soft delete)
    /// </summary>
    Task<bool> DeactivateAsync(Guid id);
    
    /// <summary>
    /// Activate a hospital
    /// </summary>
    Task<bool> ActivateAsync(Guid id);
    
    /// <summary>
    /// Get doctors working at a specific hospital
    /// </summary>
    Task<List<DoctorProfile>> GetDoctorsByHospitalAsync(Guid hospitalId);

    /// <summary>
    /// Get the hospital assigned to a given director (HospitalDirector role).
    /// Returns null if no hospital is assigned.
    /// </summary>
    Task<Hospital?> GetByDirectorAsync(Guid directorId);

    /// <summary>
    /// Assign or unassign a director to a hospital.
    /// Pass null directorId to unassign.
    /// </summary>
    Task<Hospital> AssignDirectorAsync(Guid hospitalId, Guid? directorId);
}
