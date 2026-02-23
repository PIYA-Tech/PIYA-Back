using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class PharmacyCompanyService(PharmacyApiDbContext context, IAuditService auditService) : IPharmacyCompanyService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;

    public async Task<PharmacyCompany?> GetByIdAsync(Guid id)
    {
        return await _context.PharmacyCompanies
            .Include(c => c.Pharmacies)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<List<PharmacyCompany>> GetAllAsync()
    {
        return await _context.PharmacyCompanies
            .Include(c => c.Pharmacies)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<PharmacyCompany> CreateAsync(PharmacyCompany company)
    {
        company.Id = Guid.NewGuid();
        _context.PharmacyCompanies.Add(company);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "CreatePharmacyCompany",
            "PharmacyCompany",
            company.Id.ToString(),
            null,
            $"Created pharmacy company: {company.Name}"
        );

        return company;
    }

    public async Task<PharmacyCompany> UpdateAsync(PharmacyCompany company)
    {
        var existing = await _context.PharmacyCompanies.FindAsync(company.Id) ?? throw new InvalidOperationException("Pharmacy company not found");
        existing.Name = company.Name;
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "UpdatePharmacyCompany",
            "PharmacyCompany",
            company.Id.ToString(),
            null,
            $"Updated pharmacy company: {company.Name}"
        );

        return existing;
    }

    public async Task DeleteAsync(Guid id)
    {
        var company = await _context.PharmacyCompanies.FindAsync(id) ?? throw new InvalidOperationException("Pharmacy company not found");

        // Check if company has pharmacies
        var hasPharmacies = await _context.Pharmacies
            .Include(p => p.Company)
            .AnyAsync(p => p.Company.Id == id);
        if (hasPharmacies)
        {
            throw new InvalidOperationException("Cannot delete company with associated pharmacies");
        }

        _context.PharmacyCompanies.Remove(company);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "DeletePharmacyCompany",
            "PharmacyCompany",
            id.ToString(),
            null,
            $"Deleted pharmacy company: {company.Name}"
        );
    }

    public async Task<List<Pharmacy>> GetCompanyPharmaciesAsync(Guid companyId)
    {
        return await _context.Pharmacies
            .Include(p => p.Company)
            .Include(p => p.Coordinates)
            .Where(p => p.Company.Id == companyId)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<int> GetPharmacyCountAsync(Guid companyId)
    {
        return await _context.Pharmacies
            .Include(p => p.Company)
            .CountAsync(p => p.Company.Id == companyId);
    }
}
