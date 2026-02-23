using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class SearchService(
    IPharmacyService pharmacyService,
    ICoordinatesService coordinatesService,
    PharmacyApiDbContext dbContext,
    IInventoryService inventoryService,
    IPrescriptionService prescriptionService,
    IMedicationService medicationService,
    ILogger<SearchService> logger) : ISearchService
{
    private readonly IPharmacyService _pharmacyService = pharmacyService;
    private readonly ICoordinatesService _coordinatesService = coordinatesService;
    private readonly PharmacyApiDbContext _dbContext = dbContext;
    private readonly IInventoryService _inventoryService = inventoryService;
    private readonly IPrescriptionService _prescriptionService = prescriptionService;
    private readonly IMedicationService _medicationService = medicationService;
    private readonly ILogger<SearchService> _logger = logger;

    public async Task<List<Pharmacy>> SearchByCity(Coordinates coordinates)
    {
        // This would typically use reverse geocoding to get city name
        // Then search pharmacies in that city
        // For now, we'll search by proximity (10km radius)
        var allPharmacies = await _dbContext.Pharmacies
            .Include(p => p.Coordinates)
            .Include(p => p.Company)
            .ToListAsync();

        var pharmaciesInCity = new List<Pharmacy>();

        foreach (var pharmacy in allPharmacies)
        {
            var distance = await _coordinatesService.CalculateDistance(coordinates, pharmacy.Coordinates);
            // Consider pharmacies within 10km as same city
            if (distance <= 10000) // 10km in meters
            {
                pharmaciesInCity.Add(pharmacy);
            }
        }

        return pharmaciesInCity;
    }

    public async Task<List<Pharmacy>> SearchByCountry(Coordinates coordinates)
    {
        // This would typically use reverse geocoding to get country name
        // For demonstration, we'll return all pharmacies in the same country string
        // In production, implement proper geocoding
        var allPharmacies = await _dbContext.Pharmacies
            .Include(p => p.Coordinates)
            .Include(p => p.Company)
            .ToListAsync();

        // Simple proximity-based country search (within 1000km)
        var pharmaciesInCountry = new List<Pharmacy>();

        foreach (var pharmacy in allPharmacies)
        {
            var distance = await _coordinatesService.CalculateDistance(coordinates, pharmacy.Coordinates);
            // Consider pharmacies within 1000km as same country (rough approximation)
            if (distance <= 1000000) // 1000km in meters
            {
                pharmaciesInCountry.Add(pharmacy);
            }
        }

        return pharmaciesInCountry;
    }

    public async Task<List<Pharmacy>> SearchByRadius(Coordinates coordinates, int radius)
    {
        var allPharmacies = await _dbContext.Pharmacies
            .Include(p => p.Coordinates)
            .Include(p => p.Company)
            .ToListAsync();

        var pharmaciesInRadius = new List<Pharmacy>();

        foreach (var pharmacy in allPharmacies)
        {
            var distance = await _coordinatesService.CalculateDistance(coordinates, pharmacy.Coordinates);
            if (distance <= radius)
            {
                pharmaciesInRadius.Add(pharmacy);
            }
        }

        // Sort by distance (closest first)
        return pharmaciesInRadius
            .OrderBy(p => _coordinatesService.CalculateDistance(coordinates, p.Coordinates).Result)
            .ToList();
    }

    public async Task<List<PharmacySearchResult>> SearchByMedicationAsync(Guid medicationId, Coordinates? userLocation = null, int? radiusKm = null)
    {
        try
        {
            _logger.LogInformation("Searching pharmacies with medication {MedicationId}", medicationId);

            // Get all pharmacies with this medication in stock
            var inventoryItems = await _inventoryService.GetPharmaciesWithMedicationAsync(medicationId, minimumQuantity: 1);
            
            var results = new List<PharmacySearchResult>();

            foreach (var inventory in inventoryItems)
            {
                var pharmacy = await _dbContext.Pharmacies
                    .Include(p => p.Coordinates)
                    .Include(p => p.Company)
                    .FirstOrDefaultAsync(p => p.Id == inventory.PharmacyId);

                if (pharmacy == null) continue;

                double? distance = null;
                if (userLocation != null && pharmacy.Coordinates != null)
                {
                    var distanceMeters = await _coordinatesService.CalculateDistance(userLocation, pharmacy.Coordinates);
                    distance = distanceMeters / 1000.0; // Convert to km

                    // Skip if outside radius
                    if (radiusKm.HasValue && distance > radiusKm.Value)
                    {
                        continue;
                    }
                }

                var medication = await _medicationService.GetByIdAsync(medicationId);

                var result = new PharmacySearchResult
                {
                    Pharmacy = pharmacy,
                    DistanceKm = distance,
                    TotalMedicationsRequested = 1,
                    MedicationsInStock = 1,
                    StockMatchPercentage = 100m,
                    CanFulfillCompletely = true,
                    AvailableMedications =
                    [
                        new MedicationStock
                        {
                            MedicationId = medicationId,
                            MedicationName = medication?.BrandName ?? "Unknown",
                            QuantityAvailable = inventory.QuantityInStock,
                            ExpirationDate = inventory.ExpirationDate,
                            Price = inventory.Price
                        }
                    ]
                };

                results.Add(result);
            }

            // Sort by distance if location provided
            if (userLocation != null)
            {
                results = results.OrderBy(r => r.DistanceKm ?? double.MaxValue).ToList();
            }

            _logger.LogInformation("Found {Count} pharmacies with medication {MedicationId}", results.Count, medicationId);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching pharmacies by medication {MedicationId}", medicationId);
            throw;
        }
    }

    public async Task<List<PharmacySearchResult>> SearchByMultipleMedicationsAsync(List<Guid> medicationIds, Coordinates? userLocation = null, int? radiusKm = null)
    {
        try
        {
            _logger.LogInformation("Searching pharmacies with {Count} medications", medicationIds.Count);

            if (medicationIds == null || medicationIds.Count == 0)
            {
                return [];
            }

            // Get all pharmacies
            var allPharmacies = await _dbContext.Pharmacies
                .Include(p => p.Coordinates)
                .Include(p => p.Company)
                .ToListAsync();

            var results = new List<PharmacySearchResult>();

            foreach (var pharmacy in allPharmacies)
            {
                // Calculate distance first to filter by radius
                double? distance = null;
                if (userLocation != null && pharmacy.Coordinates != null)
                {
                    var distanceMeters = await _coordinatesService.CalculateDistance(userLocation, pharmacy.Coordinates);
                    distance = distanceMeters / 1000.0; // Convert to km

                    // Skip if outside radius
                    if (radiusKm.HasValue && distance > radiusKm.Value)
                    {
                        continue;
                    }
                }

                // Check stock availability for all medications
                var availableMeds = new List<MedicationStock>();
                var missingMedIds = new List<Guid>();

                foreach (var medId in medicationIds)
                {
                    var inventory = await _dbContext.PharmacyInventories
                        .FirstOrDefaultAsync(i => i.PharmacyId == pharmacy.Id && 
                                                  i.MedicationId == medId && 
                                                  i.QuantityInStock > 0);

                    if (inventory != null)
                    {
                        var medication = await _medicationService.GetByIdAsync(medId);
                        availableMeds.Add(new MedicationStock
                        {
                            MedicationId = medId,
                            MedicationName = medication?.BrandName ?? "Unknown",
                            QuantityAvailable = inventory.QuantityInStock,
                            ExpirationDate = inventory.ExpirationDate,
                            Price = inventory.Price
                        });
                    }
                    else
                    {
                        missingMedIds.Add(medId);
                    }
                }

                // Only include pharmacies that have at least one medication
                if (availableMeds.Count > 0)
                {
                    var stockMatchPercentage = (decimal)availableMeds.Count / medicationIds.Count * 100m;

                    var result = new PharmacySearchResult
                    {
                        Pharmacy = pharmacy,
                        DistanceKm = distance,
                        TotalMedicationsRequested = medicationIds.Count,
                        MedicationsInStock = availableMeds.Count,
                        StockMatchPercentage = stockMatchPercentage,
                        CanFulfillCompletely = missingMedIds.Count == 0,
                        AvailableMedications = availableMeds,
                        MissingMedicationIds = missingMedIds
                    };

                    results.Add(result);
                }
            }

            // Sort by stock match percentage (descending), then distance (ascending)
            results = results
                .OrderByDescending(r => r.StockMatchPercentage)
                .ThenBy(r => r.DistanceKm ?? double.MaxValue)
                .ToList();

            _logger.LogInformation("Found {Count} pharmacies with partial/full stock", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching pharmacies by multiple medications");
            throw;
        }
    }

    public async Task<List<PharmacySearchResult>> SearchPharmaciesWithFullPrescriptionStockAsync(Guid prescriptionId, Coordinates? userLocation = null, int? radiusKm = null)
    {
        try
        {
            _logger.LogInformation("Searching pharmacies with full prescription stock for prescription {PrescriptionId}", prescriptionId);

            // Get prescription items
            var prescription = await _prescriptionService.GetByIdAsync(prescriptionId) ?? throw new InvalidOperationException($"Prescription {prescriptionId} not found");
            var prescriptionItems = await _dbContext.PrescriptionItems
                .Where(pi => pi.PrescriptionId == prescriptionId)
                .ToListAsync();

            if (prescriptionItems.Count == 0)
            {
                _logger.LogWarning("Prescription {PrescriptionId} has no items", prescriptionId);
                return [];
            }

            var medicationIds = prescriptionItems.Select(pi => pi.MedicationId).ToList();

            // Use multi-medication search
            var allResults = await SearchByMultipleMedicationsAsync(medicationIds, userLocation, radiusKm);

            // Filter to only pharmacies that can fulfill completely
            var fullStockResults = allResults.Where(r => r.CanFulfillCompletely).ToList();

            _logger.LogInformation("Found {Count} pharmacies with full prescription stock", fullStockResults.Count);
            return fullStockResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching pharmacies with full prescription stock");
            throw;
        }
    }

    public async Task<List<PharmacySearchResult>> SearchAndSortByDistanceAndStockAsync(List<Guid> medicationIds, Coordinates userLocation, int maxRadiusKm = 50)
    {
        try
        {
            _logger.LogInformation("Searching and sorting pharmacies by distance and stock availability");

            // Get all results within radius
            var results = await SearchByMultipleMedicationsAsync(medicationIds, userLocation, maxRadiusKm);

            // Calculate composite score
            // Score formula: (StockMatchPercentage * 0.6) + ((1 - NormalizedDistance) * 0.4) * 100
            // This gives 60% weight to stock availability and 40% to proximity

            if (results.Count == 0)
            {
                return results;
            }

            var maxDistance = results.Max(r => r.DistanceKm ?? 0);
            if (maxDistance == 0) maxDistance = 1; // Prevent division by zero

            foreach (var result in results)
            {
                var distance = result.DistanceKm ?? maxDistance;
                var normalizedDistance = distance / maxDistance; // 0 to 1, where 0 is closest
                
                // Composite score: higher is better
                // Stock match: 0-100, Distance factor: 0-100 (inverted so closer = higher)
                var stockScore = result.StockMatchPercentage * 0.6m;
                var distanceScore = (1 - (decimal)normalizedDistance) * 40m;
                
                result.CompositeScore = stockScore + distanceScore;
            }

            // Sort by composite score (descending)
            results = results.OrderByDescending(r => r.CompositeScore).ToList();

            _logger.LogInformation("Sorted {Count} pharmacies by composite score", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sorting pharmacies by distance and stock");
            throw;
        }
    }
}

