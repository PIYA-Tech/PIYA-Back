using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

public interface IDoctorProfileService
{
    /// <summary>
    /// Create a doctor profile for a user
    /// </summary>
    Task<DoctorProfile> CreateProfileAsync(DoctorProfile profile);
    
    /// <summary>
    /// Get doctor profile by user ID
    /// </summary>
    Task<DoctorProfile?> GetByUserIdAsync(Guid userId);
    
    /// <summary>
    /// Get doctor profile by profile ID
    /// </summary>
    Task<DoctorProfile?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Update doctor profile
    /// </summary>
    Task<DoctorProfile> UpdateProfileAsync(DoctorProfile profile);
    
    /// <summary>
    /// Delete doctor profile
    /// </summary>
    Task<bool> DeleteProfileAsync(Guid id);
    
    /// <summary>
    /// Search doctors by specialization
    /// </summary>
    Task<List<DoctorProfile>> SearchBySpecializationAsync(MedicalSpecialization specialization);
    
    /// <summary>
    /// Search doctors by hospital
    /// </summary>
    Task<List<DoctorProfile>> GetDoctorsByHospitalAsync(Guid hospitalId);
    
    /// <summary>
    /// Get doctors accepting new patients
    /// </summary>
    Task<List<DoctorProfile>> GetAvailableDoctorsAsync(MedicalSpecialization? specialization = null);
    
    /// <summary>
    /// Update doctor availability status
    /// </summary>
    Task<bool> UpdateAvailabilityStatusAsync(Guid userId, DoctorAvailabilityStatus status);
    
    /// <summary>
    /// Set doctor as online
    /// </summary>
    Task<bool> SetOnlineAsync(Guid userId);
    
    /// <summary>
    /// Set doctor as offline
    /// </summary>
    Task<bool> SetOfflineAsync(Guid userId);
    
    /// <summary>
    /// Get doctor's working hours
    /// </summary>
    Task<List<WorkingHoursSlot>?> GetWorkingHoursAsync(Guid userId);
    
    /// <summary>
    /// Update doctor's working hours
    /// </summary>
    Task<bool> UpdateWorkingHoursAsync(Guid userId, List<WorkingHoursSlot> workingHours);
    
    /// <summary>
    /// Check if doctor is available at specific date/time
    /// </summary>
    Task<bool> IsAvailableAtAsync(Guid userId, DateTime dateTime);

    /// <summary>
    /// Admin: replace the full set of hospitals for a doctor profile
    /// </summary>
    Task<DoctorProfile> AssignHospitalsAsync(Guid doctorProfileId, List<Guid> hospitalIds);
}
