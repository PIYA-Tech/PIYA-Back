using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class DoctorProfileService(PharmacyApiDbContext context, ILogger<DoctorProfileService> logger) : IDoctorProfileService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly ILogger<DoctorProfileService> _logger = logger;

    public async Task<DoctorProfile> CreateProfileAsync(DoctorProfile profile)
    {
        try
        {
            // Check if profile already exists
            var existing = await _context.DoctorProfiles.FirstOrDefaultAsync(dp => dp.UserId == profile.UserId);
            if (existing != null)
            {
                throw new InvalidOperationException($"Doctor profile already exists for user {profile.UserId}");
            }

            // Validate user exists and has Doctor role
            var user = await _context.Users.FindAsync(profile.UserId) ?? throw new InvalidOperationException($"User {profile.UserId} not found");
            if (user.Role != UserRole.Doctor)
            {
                throw new InvalidOperationException($"User {profile.UserId} is not a doctor");
            }

            profile.Id = Guid.NewGuid();
            profile.CreatedAt = DateTime.UtcNow;
            profile.UpdatedAt = DateTime.UtcNow;
            profile.CurrentStatus = DoctorAvailabilityStatus.Offline;

            _context.DoctorProfiles.Add(profile);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created doctor profile {ProfileId} for user {UserId}", profile.Id, profile.UserId);
            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating doctor profile for user {UserId}", profile.UserId);
            throw;
        }
    }

    public async Task<DoctorProfile?> GetByUserIdAsync(Guid userId)
    {
        try
        {
            return await _context.DoctorProfiles
                .FirstOrDefaultAsync(dp => dp.UserId == userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting doctor profile for user {UserId}", userId);
            throw;
        }
    }

    public async Task<DoctorProfile?> GetByIdAsync(Guid id)
    {
        try
        {
            return await _context.DoctorProfiles.FindAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting doctor profile {ProfileId}", id);
            throw;
        }
    }

    public async Task<DoctorProfile> UpdateProfileAsync(DoctorProfile profile)
    {
        try
        {
            var existing = await _context.DoctorProfiles.FindAsync(profile.Id) ?? throw new InvalidOperationException($"Doctor profile {profile.Id} not found");

            // Update fields
            existing.LicenseNumber = profile.LicenseNumber;
            existing.LicenseAuthority = profile.LicenseAuthority;
            existing.LicenseExpiryDate = profile.LicenseExpiryDate;
            existing.Specialization = profile.Specialization;
            existing.AdditionalSpecializations = profile.AdditionalSpecializations;
            existing.YearsOfExperience = profile.YearsOfExperience;
            existing.Certifications = profile.Certifications;
            existing.Education = profile.Education;
            existing.Languages = profile.Languages;
            existing.Biography = profile.Biography;
            existing.ConsultationFee = profile.ConsultationFee;
            existing.AcceptingNewPatients = profile.AcceptingNewPatients;
            existing.HospitalIds = profile.HospitalIds;
            existing.WorkingHours = profile.WorkingHours;
            existing.AverageAppointmentDuration = profile.AverageAppointmentDuration;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated doctor profile {ProfileId}", profile.Id);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating doctor profile {ProfileId}", profile.Id);
            throw;
        }
    }

    public async Task<bool> DeleteProfileAsync(Guid id)
    {
        try
        {
            var profile = await _context.DoctorProfiles.FindAsync(id);
            if (profile == null)
            {
                return false;
            }

            _context.DoctorProfiles.Remove(profile);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted doctor profile {ProfileId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting doctor profile {ProfileId}", id);
            throw;
        }
    }

    public async Task<List<DoctorProfile>> SearchBySpecializationAsync(MedicalSpecialization specialization)
    {
        try
        {
            return await _context.DoctorProfiles
                .Where(dp => dp.Specialization == specialization || 
                            (dp.AdditionalSpecializations != null && dp.AdditionalSpecializations.Contains(specialization)))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching doctors by specialization {Specialization}", specialization);
            throw;
        }
    }

    public async Task<List<DoctorProfile>> GetDoctorsByHospitalAsync(Guid hospitalId)
    {
        try
        {
            return await _context.DoctorProfiles
                .Where(dp => dp.HospitalIds != null && dp.HospitalIds.Contains(hospitalId))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting doctors by hospital {HospitalId}", hospitalId);
            throw;
        }
    }

    public async Task<List<DoctorProfile>> GetAvailableDoctorsAsync(MedicalSpecialization? specialization = null)
    {
        try
        {
            var query = _context.DoctorProfiles
                .Where(dp => dp.AcceptingNewPatients == true);

            if (specialization.HasValue)
            {
                query = query.Where(dp => dp.Specialization == specialization || 
                                         (dp.AdditionalSpecializations != null && dp.AdditionalSpecializations.Contains(specialization.Value)));
            }

            return await query.ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available doctors");
            throw;
        }
    }

    public async Task<bool> UpdateAvailabilityStatusAsync(Guid userId, DoctorAvailabilityStatus status)
    {
        try
        {
            var profile = await _context.DoctorProfiles.FirstOrDefaultAsync(dp => dp.UserId == userId);
            if (profile == null)
            {
                return false;
            }

            profile.CurrentStatus = status;
            if (status == DoctorAvailabilityStatus.Online)
            {
                profile.LastOnlineAt = DateTime.UtcNow;
            }
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated availability status for doctor {UserId} to {Status}", userId, status);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating availability status for doctor {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> SetOnlineAsync(Guid userId)
    {
        return await UpdateAvailabilityStatusAsync(userId, DoctorAvailabilityStatus.Online);
    }

    public async Task<bool> SetOfflineAsync(Guid userId)
    {
        return await UpdateAvailabilityStatusAsync(userId, DoctorAvailabilityStatus.Offline);
    }

    public async Task<List<WorkingHoursSlot>?> GetWorkingHoursAsync(Guid userId)
    {
        try
        {
            var profile = await _context.DoctorProfiles.FirstOrDefaultAsync(dp => dp.UserId == userId);
            if (profile == null || string.IsNullOrEmpty(profile.WorkingHours))
            {
                return null;
            }

            return JsonSerializer.Deserialize<List<WorkingHoursSlot>>(profile.WorkingHours);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting working hours for doctor {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> UpdateWorkingHoursAsync(Guid userId, List<WorkingHoursSlot> workingHours)
    {
        try
        {
            var profile = await _context.DoctorProfiles.FirstOrDefaultAsync(dp => dp.UserId == userId);
            if (profile == null)
            {
                return false;
            }

            // Validate working hours
            foreach (var slot in workingHours)
            {
                // DayOfWeek is a string like "Monday", "Tuesday", etc.
                if (string.IsNullOrWhiteSpace(slot.DayOfWeek))
                {
                    throw new ArgumentException("Day of week cannot be empty");
                }
                
                foreach (var timeSlot in slot.Slots)
                {
                    // Parse time strings (HH:mm format)
                    if (!TimeOnly.TryParse(timeSlot.Start, out var startTime) ||
                        !TimeOnly.TryParse(timeSlot.End, out var endTime))
                    {
                        throw new ArgumentException($"Invalid time format for {slot.DayOfWeek}");
                    }
                    
                    if (startTime >= endTime)
                    {
                        throw new ArgumentException($"Invalid time slot for {slot.DayOfWeek}: start time must be before end time");
                    }
                }
            }

            profile.WorkingHours = JsonSerializer.Serialize(workingHours);
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated working hours for doctor {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating working hours for doctor {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> IsAvailableAtAsync(Guid userId, DateTime dateTime)
    {
        try
        {
            var profile = await _context.DoctorProfiles.FirstOrDefaultAsync(dp => dp.UserId == userId);
            if (profile == null || !profile.AcceptingNewPatients)
            {
                return false;
            }

            // Check if doctor is online or available
            if (profile.CurrentStatus != DoctorAvailabilityStatus.Online && 
                profile.CurrentStatus != DoctorAvailabilityStatus.OnCall)
            {
                return false;
            }

            // Get working hours
            var workingHours = await GetWorkingHoursAsync(userId);
            if (workingHours == null || workingHours.Count == 0)
            {
                return false;
            }

            // Check if the datetime falls within working hours
            var dayName = dateTime.DayOfWeek.ToString(); // Monday, Tuesday, etc.
            var timeOnly = TimeOnly.FromDateTime(dateTime);

            var daySlot = workingHours.FirstOrDefault(wh => wh.DayOfWeek.Equals(dayName, StringComparison.OrdinalIgnoreCase));
            if (daySlot == null)
            {
                return false;
            }

            foreach (var timeSlot in daySlot.Slots)
            {
                // Parse time strings (HH:mm format)
                if (TimeOnly.TryParse(timeSlot.Start, out var startTime) &&
                    TimeOnly.TryParse(timeSlot.End, out var endTime))
                {
                    if (timeOnly >= startTime && timeOnly <= endTime)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking availability for doctor {UserId} at {DateTime}", userId, dateTime);
            throw;
        }
    }
}
