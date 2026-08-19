namespace PIYA_API.DTOs;

/// <summary>
/// Explicit medication write contract. Navigation properties and server-managed
/// identifiers/timestamps are intentionally excluded.
/// </summary>
public class MedicationUpsertDto
{
    public required string BrandName { get; set; }
    public required string GenericName { get; set; }
    public List<string> ActiveIngredients { get; set; } = [];
    public string? AtcCode { get; set; }
    public required string Form { get; set; }
    public required string Strength { get; set; }
    public string? Manufacturer { get; set; }
    public bool RequiresPrescription { get; set; } = true;
    public bool IsControlledSubstance { get; set; }
    public List<Guid> GenericAlternatives { get; set; } = [];
    public string? Usage { get; set; }
    public string? SideEffects { get; set; }
    public string? Contraindications { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string Country { get; set; } = "Azerbaijan";
    public string? Barcode { get; set; }
}
