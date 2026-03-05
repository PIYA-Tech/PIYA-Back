using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

public interface ISearchService
{
    public Task<List<Pharmacy>> SearchByCountry(Coordinates coordinates);
    public Task<List<Pharmacy>> SearchByCity(Coordinates coordinates);
    public Task<List<Pharmacy>> SearchByRadius(Coordinates coordinates, int radius);
    
    /// <summary>
    /// Search pharmacies by medication availability (single medication)
    /// </summary>
    Task<List<PharmacySearchResult>> SearchByMedicationAsync(Guid medicationId, Coordinates? userLocation = null, int? radiusKm = null);
    
    /// <summary>
    /// Search pharmacies that have ALL specified medications in stock (multi-medication match)
    /// </summary>
    Task<List<PharmacySearchResult>> SearchByMultipleMedicationsAsync(List<Guid> medicationIds, Coordinates? userLocation = null, int? radiusKm = null);
    
    /// <summary>
    /// Filter pharmacies that can fulfill entire prescription (all items in stock)
    /// </summary>
    Task<List<PharmacySearchResult>> SearchPharmaciesWithFullPrescriptionStockAsync(Guid prescriptionId, Coordinates? userLocation = null, int? radiusKm = null);
    
    /// <summary>
    /// Sort pharmacies by distance and stock availability score
    /// Returns pharmacies sorted by composite score (distance + stock match percentage)
    /// </summary>
    Task<List<PharmacySearchResult>> SearchAndSortByDistanceAndStockAsync(List<Guid> medicationIds, Coordinates userLocation, int maxRadiusKm = 50);
}

/// <summary>
/// Enhanced search result with inventory and distance information
/// </summary>
public class PharmacySearchResult
{
    public Pharmacy Pharmacy { get; set; } = null!;
    public double? DistanceKm { get; set; }
    public List<MedicationStock> AvailableMedications { get; set; } = [];
    public int TotalMedicationsRequested { get; set; }
    public int MedicationsInStock { get; set; }
    public decimal StockMatchPercentage { get; set; }
    public bool CanFulfillCompletely { get; set; }
    public List<Guid> MissingMedicationIds { get; set; } = [];
    public decimal CompositeScore { get; set; } // Higher is better
}

/// <summary>
/// Medication stock information for search results
/// </summary>
public class MedicationStock
{
    public Guid MedicationId { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public int QuantityAvailable { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public decimal? Price { get; set; }
}
