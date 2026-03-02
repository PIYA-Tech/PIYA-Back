using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Service for managing medication master database
/// </summary>
public interface IMedicationService
{
    /// <summary>
    /// Create a new medication
    /// </summary>
    Task<Medication> CreateAsync(Medication medication);
    
    /// <summary>
    /// Get medication by ID
    /// </summary>
    Task<Medication?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Get all medications (available only — for public/patient use)
    /// </summary>
    Task<List<Medication>> GetAllAsync();

    /// <summary>
    /// Get all medications for admin (no availability filter, supports search/filter/paging)
    /// </summary>
    Task<(List<Medication> Items, int TotalCount)> GetAllAdminAsync(
        string? search,
        bool? requiresPrescription,
        bool? isAvailable,
        int page,
        int pageSize);
    
    /// <summary>
    /// Search medications by name (brand or generic)
    /// </summary>
    Task<List<Medication>> SearchByNameAsync(string searchTerm);
    
    /// <summary>
    /// Search medications by active ingredient
    /// </summary>
    Task<List<Medication>> SearchByIngredientAsync(string ingredient);
    
    /// <summary>
    /// Get medications by ATC code
    /// </summary>
    Task<List<Medication>> GetByAtcCodeAsync(string atcCode);
    
    /// <summary>
    /// Get generic alternatives for a medication
    /// </summary>
    Task<List<Medication>> GetGenericAlternativesAsync(Guid medicationId);
    
    /// <summary>
    /// Update medication
    /// </summary>
    Task<Medication> UpdateAsync(Medication medication);
    
    /// <summary>
    /// Delete medication
    /// </summary>
    Task<bool> DeleteAsync(Guid id);
    
    /// <summary>
    /// Check if medication requires prescription
    /// </summary>
    Task<bool> RequiresPrescriptionAsync(Guid medicationId);
    
    /// <summary>
    /// Get medications by form (tablet, syrup, etc.)
    /// </summary>
    Task<List<Medication>> GetByFormAsync(string form);
    
    /// <summary>
    /// Get available medications in Azerbaijan
    /// </summary>
    Task<List<Medication>> GetAvailableInCountryAsync(string country = "Azerbaijan");
}
