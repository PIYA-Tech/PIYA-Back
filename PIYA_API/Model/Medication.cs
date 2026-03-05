namespace PIYA_API.Model;

/// <summary>
/// Medication master database (Azerbaijan pharmaceutical registry)
/// </summary>
public class Medication
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Brand/trade name
    /// </summary>
    public required string BrandName { get; set; }
    
    /// <summary>
    /// Generic/scientific name
    /// </summary>
    public required string GenericName { get; set; }
    
    /// <summary>
    /// Active ingredients
    /// </summary>
    public List<string> ActiveIngredients { get; set; } = [];
    
    /// <summary>
    /// ATC (Anatomical Therapeutic Chemical) classification code
    /// </summary>
    public string? AtcCode { get; set; }
    
    /// <summary>
    /// Medication form (tablet, capsule, syrup, injection, etc.)
    /// </summary>
    public required string Form { get; set; }
    
    /// <summary>
    /// Strength/concentration (e.g., "500mg", "10mg/ml")
    /// </summary>
    public required string Strength { get; set; }
    
    /// <summary>
    /// Manufacturer name
    /// </summary>
    public string? Manufacturer { get; set; }
    
    /// <summary>
    /// Whether prescription is required
    /// </summary>
    public bool RequiresPrescription { get; set; } = true;
    
    /// <summary>
    /// Whether medication is controlled substance
    /// </summary>
    public bool IsControlledSubstance { get; set; } = false;
    
    /// <summary>
    /// Generic alternatives (medication IDs)
    /// </summary>
    public List<Guid> GenericAlternatives { get; set; } = [];
    
    /// <summary>
    /// Usage/indications
    /// </summary>
    public string? Usage { get; set; }
    
    /// <summary>
    /// Side effects
    /// </summary>
    public string? SideEffects { get; set; }
    
    /// <summary>
    /// Contraindications
    /// </summary>
    public string? Contraindications { get; set; }
    
    /// <summary>
    /// Whether the medication is currently available in Azerbaijan
    /// </summary>
    public bool IsAvailable { get; set; } = true;
    
    /// <summary>
    /// Country of origin
    /// </summary>
    public string Country { get; set; } = "Azerbaijan";
    
    /// <summary>
    /// Barcode/SKU for inventory tracking
    /// </summary>
    public string? Barcode { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<PharmacyInventory> InventoryRecords { get; set; } = [];
}
