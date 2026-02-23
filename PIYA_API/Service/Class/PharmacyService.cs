using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class PharmacyService(PharmacyApiDbContext dbContext) : IPharmacyService
{
    public async Task<Pharmacy?> GetById(Guid id)
    {
        var pharmacy = await dbContext.Pharmacies
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

    public Task<Pharmacy> Create(Pharmacy pharmacy)
    {
        dbContext.Pharmacies.Add(pharmacy);
        dbContext.SaveChanges();
        return Task.FromResult(pharmacy);
    }

    public Task Delete(Guid id)
    {
        var pharmacy = dbContext.Pharmacies.Find(id) ?? throw new Exception("Pharmacy not found");
        dbContext.Pharmacies.Remove(pharmacy);
        dbContext.SaveChanges();
        return Task.CompletedTask;
    }
    
    public Task Update(Pharmacy pharmacy)
    {
        var existingPharmacy = dbContext.Pharmacies.Find(pharmacy.Id) ?? throw new Exception("Pharmacy not found");
        existingPharmacy.Name = pharmacy.Name;
        existingPharmacy.Address = pharmacy.Address;
        existingPharmacy.Country = pharmacy.Country;
        existingPharmacy.Coordinates = pharmacy.Coordinates;
        dbContext.SaveChanges();
        return Task.CompletedTask;
    }
}
