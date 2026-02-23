using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class HospitalService(PharmacyApiDbContext context, ILogger<HospitalService> logger) : IHospitalService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly ILogger<HospitalService> _logger = logger;

    public async Task<List<Hospital>> GetAllAsync()
    {
        try
        {
            return await _context.Hospitals.ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all hospitals");
            throw;
        }
    }

    public async Task<Hospital?> GetByIdAsync(Guid id)
    {
        try
        {
            return await _context.Hospitals.FindAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting hospital {HospitalId}", id);
            throw;
        }
    }

    public async Task<List<Hospital>> GetByCityAsync(string city)
    {
        try
        {
            return await _context.Hospitals
                .Where(h => h.City.ToLower() == city.ToLower())
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting hospitals in city {City}", city);
            throw;
        }
    }

    public async Task<List<Hospital>> GetByDepartmentAsync(string department)
    {
        try
        {
            return await _context.Hospitals
                .Where(h => h.Departments != null && h.Departments.Contains(department))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting hospitals by department {Department}", department);
            throw;
        }
    }

    public async Task<List<Hospital>> GetActiveHospitalsAsync()
    {
        try
        {
            return await _context.Hospitals
                .Where(h => h.IsActive)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active hospitals");
            throw;
        }
    }

    public async Task<Hospital> CreateAsync(Hospital hospital)
    {
        try
        {
            hospital.Id = Guid.NewGuid();
            hospital.CreatedAt = DateTime.UtcNow;
            hospital.UpdatedAt = DateTime.UtcNow;
            hospital.IsActive = true;

            _context.Hospitals.Add(hospital);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created hospital {HospitalId} - {Name}", hospital.Id, hospital.Name);
            return hospital;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating hospital {Name}", hospital.Name);
            throw;
        }
    }

    public async Task<Hospital> UpdateAsync(Hospital hospital)
    {
        try
        {
            var existing = await _context.Hospitals.FindAsync(hospital.Id) ?? throw new InvalidOperationException($"Hospital {hospital.Id} not found");
            existing.Name = hospital.Name;
            existing.Address = hospital.Address;
            existing.City = hospital.City;
            existing.Country = hospital.Country;
            existing.PhoneNumber = hospital.PhoneNumber;
            existing.Email = hospital.Email;
            existing.Website = hospital.Website;
            existing.Departments = hospital.Departments;
            existing.EmergencyContact = hospital.EmergencyContact;
            existing.Coordinates = hospital.Coordinates;
            existing.OperatingHours = hospital.OperatingHours;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated hospital {HospitalId}", hospital.Id);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating hospital {HospitalId}", hospital.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            var hospital = await _context.Hospitals.FindAsync(id);
            if (hospital == null)
            {
                return false;
            }

            _context.Hospitals.Remove(hospital);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted hospital {HospitalId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting hospital {HospitalId}", id);
            throw;
        }
    }

    public async Task<bool> DeactivateAsync(Guid id)
    {
        try
        {
            var hospital = await _context.Hospitals.FindAsync(id);
            if (hospital == null)
            {
                return false;
            }

            hospital.IsActive = false;
            hospital.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deactivated hospital {HospitalId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating hospital {HospitalId}", id);
            throw;
        }
    }

    public async Task<bool> ActivateAsync(Guid id)
    {
        try
        {
            var hospital = await _context.Hospitals.FindAsync(id);
            if (hospital == null)
            {
                return false;
            }

            hospital.IsActive = true;
            hospital.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Activated hospital {HospitalId}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating hospital {HospitalId}", id);
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
            _logger.LogError(ex, "Error getting doctors for hospital {HospitalId}", hospitalId);
            throw;
        }
    }
}
