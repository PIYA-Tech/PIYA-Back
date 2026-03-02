using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class MedicationService(
    PharmacyApiDbContext context,
    IAuditService auditService,
    ILogger<MedicationService> logger) : IMedicationService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly ILogger<MedicationService> _logger = logger;

    public async Task<Medication> CreateAsync(Medication medication)
    {
        medication.Id = Guid.NewGuid();
        medication.CreatedAt = DateTime.UtcNow;
        medication.UpdatedAt = DateTime.UtcNow;

        _context.Medications.Add(medication);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "CreateMedication",
            "Medication",
            medication.Id.ToString(),
            null,
            $"Medication created: {medication.BrandName}"
        );

        return medication;
    }

    public async Task<Medication?> GetByIdAsync(Guid id)
    {
        return await _context.Medications.FindAsync(id);
    }

    public async Task<List<Medication>> GetAllAsync()
    {
        return await _context.Medications
            .Where(m => m.IsAvailable)
            .OrderBy(m => m.BrandName)
            .ToListAsync();
    }

    public async Task<(List<Medication> Items, int TotalCount)> GetAllAdminAsync(
        string? search,
        bool? requiresPrescription,
        bool? isAvailable,
        int page,
        int pageSize)
    {
        var query = _context.Medications.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.ToLower();
            query = query.Where(m =>
                m.BrandName.ToLower().Contains(q) ||
                m.GenericName.ToLower().Contains(q) ||
                m.Form.ToLower().Contains(q) ||
                (m.Manufacturer != null && m.Manufacturer.ToLower().Contains(q)));
        }

        if (requiresPrescription.HasValue)
            query = query.Where(m => m.RequiresPrescription == requiresPrescription.Value);

        if (isAvailable.HasValue)
            query = query.Where(m => m.IsAvailable == isAvailable.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(m => m.BrandName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<List<Medication>> SearchByNameAsync(string searchTerm)
    {
        searchTerm = searchTerm.ToLower();
        
        return await _context.Medications
            .Where(m => m.IsAvailable)
            .Where(m => 
                m.BrandName.ToLower().Contains(searchTerm) ||
                m.GenericName.ToLower().Contains(searchTerm))
            .OrderBy(m => m.BrandName)
            .ToListAsync();
    }

    public async Task<List<Medication>> SearchByIngredientAsync(string ingredient)
    {
        ingredient = ingredient.ToLower();
        
        return await _context.Medications
            .Where(m => m.IsAvailable)
            .Where(m => m.ActiveIngredients.Any(i => i.ToLower().Contains(ingredient)))
            .OrderBy(m => m.BrandName)
            .ToListAsync();
    }

    public async Task<List<Medication>> GetByAtcCodeAsync(string atcCode)
    {
        return await _context.Medications
            .Where(m => m.IsAvailable)
            .Where(m => m.AtcCode == atcCode)
            .OrderBy(m => m.BrandName)
            .ToListAsync();
    }

    public async Task<List<Medication>> GetGenericAlternativesAsync(Guid medicationId)
    {
        var medication = await GetByIdAsync(medicationId);
        if (medication == null || medication.GenericAlternatives.Count == 0)
        {
            return [];
        }

        return await _context.Medications
            .Where(m => medication.GenericAlternatives.Contains(m.Id))
            .Where(m => m.IsAvailable)
            .OrderBy(m => m.BrandName)
            .ToListAsync();
    }

    public async Task<Medication> UpdateAsync(Medication medication)
    {
        medication.UpdatedAt = DateTime.UtcNow;
        
        _context.Medications.Update(medication);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "UpdateMedication",
            "Medication",
            medication.Id.ToString(),
            null,
            $"Medication updated: {medication.BrandName}"
        );

        return medication;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var medication = await GetByIdAsync(id);
        if (medication == null)
        {
            return false;
        }

        // Soft delete - mark as unavailable
        medication.IsAvailable = false;
        medication.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "DeleteMedication",
            "Medication",
            id.ToString(),
            null,
            $"Medication marked as unavailable: {medication.BrandName}"
        );

        return true;
    }

    public async Task<bool> RequiresPrescriptionAsync(Guid id)
    {
        var medication = await GetByIdAsync(id);
        return medication?.RequiresPrescription ?? true; // Default to true for safety
    }

    public async Task<List<Medication>> GetByFormAsync(string form)
    {
        return await _context.Medications
            .Where(m => m.IsAvailable)
            .Where(m => m.Form == form)
            .OrderBy(m => m.BrandName)
            .ToListAsync();
    }

    public async Task<List<Medication>> GetAvailableInCountryAsync(string country)
    {
        return await _context.Medications
            .Where(m => m.IsAvailable)
            .Where(m => m.Country == country)
            .OrderBy(m => m.BrandName)
            .ToListAsync();
    }
}
