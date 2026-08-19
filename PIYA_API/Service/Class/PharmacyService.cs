using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class PharmacyService(PharmacyApiDbContext dbContext) : IPharmacyService
{
    public Task<List<Pharmacy>> GetAll()
    {
        // Public endpoint — only load the fields mapped by ToPublicDto().
        // Manager and Staff are not included in the public DTO so there is no
        // reason to eager-load them here; omitting them avoids unnecessary JOINs.
        return dbContext.Pharmacies
            .Include(p => p.Coordinates)
            .Include(p => p.Company)
            .ToListAsync();
    }

    public async Task<Pharmacy?> GetById(Guid id)
    {
        var pharmacy = await dbContext.Pharmacies
            .Include(p => p.Coordinates)
            .Include(p => p.Company)
            .Include(p => p.Manager)
            .Include(p => p.Staff)
            .FirstOrDefaultAsync(p => p.Id == id);
        return pharmacy ?? null;
    }

    public Task<List<Pharmacy>> GetByCompany(Guid companyId)
    {
        var pharmacies = dbContext.Pharmacies
            .Include(p => p.Company)
            .Include(p => p.Manager)
            .Include(p => p.Staff)
            .Where(p => p.Company.Id == companyId)
            .ToListAsync();
        return pharmacies;
    }

    public async Task<Pharmacy> Create(Pharmacy pharmacy)
    {
        dbContext.Pharmacies.Add(pharmacy);
        await dbContext.SaveChangesAsync();
        return pharmacy;
    }

    public async Task Delete(Guid id)
    {
        var pharmacy = await dbContext.Pharmacies.FindAsync(id)
            ?? throw new KeyNotFoundException("Pharmacy not found");
        dbContext.Pharmacies.Remove(pharmacy);
        await dbContext.SaveChangesAsync();
    }

    public async Task<Pharmacy> Update(Pharmacy pharmacy)
    {
        var existingPharmacy = await dbContext.Pharmacies
            .Include(p => p.Coordinates)
            .Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.Id == pharmacy.Id)
            ?? throw new KeyNotFoundException("Pharmacy not found");
        existingPharmacy.Name = pharmacy.Name;
        existingPharmacy.Address = pharmacy.Address;
        existingPharmacy.City = pharmacy.City;
        existingPharmacy.Country = pharmacy.Country;
        existingPharmacy.PhoneNumber = pharmacy.PhoneNumber;
        existingPharmacy.Email = pharmacy.Email;
        existingPharmacy.Website = pharmacy.Website;
        existingPharmacy.EmergencyContact = pharmacy.EmergencyContact;
        existingPharmacy.Services = pharmacy.Services;
        existingPharmacy.OperatingHours = pharmacy.OperatingHours;
        existingPharmacy.IsActive = pharmacy.IsActive;
        existingPharmacy.Is24Hours = pharmacy.Is24Hours;
        existingPharmacy.Company = pharmacy.Company;
        existingPharmacy.UpdatedAt = DateTime.UtcNow;

        // Update coordinates in-place to avoid inserting a duplicate Coordinates row
        if (pharmacy.Coordinates != null)
        {
            if (existingPharmacy.Coordinates != null)
            {
                existingPharmacy.Coordinates.Latitude  = pharmacy.Coordinates.Latitude;
                existingPharmacy.Coordinates.Longitude = pharmacy.Coordinates.Longitude;
            }
            else
            {
                existingPharmacy.Coordinates = pharmacy.Coordinates;
            }
        }

        await dbContext.SaveChangesAsync();
        return existingPharmacy;
    }
}
