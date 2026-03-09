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

    // ── Pure Haversine — no DB, no async overhead ────────────────────────────
    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000; // Earth radius in metres
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    // ── Shared fetch: all pharmacies with Coordinates + Company in one query ─
    private Task<List<Pharmacy>> LoadPharmaciesAsync() =>
        _dbContext.Pharmacies
            .AsNoTracking()
            .Include(p => p.Coordinates)
            .Include(p => p.Company)
            .ToListAsync();

    public async Task<List<Pharmacy>> SearchByCity(Coordinates coordinates)
    {
        var all = await LoadPharmaciesAsync();
        return all
            .Where(p => p.Coordinates != null
                && HaversineMeters(coordinates.Latitude, coordinates.Longitude,
                                   p.Coordinates.Latitude, p.Coordinates.Longitude) <= 10_000)
            .ToList();
    }

    public async Task<List<Pharmacy>> SearchByCountry(Coordinates coordinates)
    {
        var all = await LoadPharmaciesAsync();
        return all
            .Where(p => p.Coordinates != null
                && HaversineMeters(coordinates.Latitude, coordinates.Longitude,
                                   p.Coordinates.Latitude, p.Coordinates.Longitude) <= 1_000_000)
            .ToList();
    }

    public async Task<List<Pharmacy>> SearchByRadius(Coordinates coordinates, int radius)
    {
        var all = await LoadPharmaciesAsync();
        return all
            .Where(p => p.Coordinates != null
                && HaversineMeters(coordinates.Latitude, coordinates.Longitude,
                                   p.Coordinates.Latitude, p.Coordinates.Longitude) <= radius)
            .OrderBy(p => HaversineMeters(coordinates.Latitude, coordinates.Longitude,
                                          p.Coordinates!.Latitude, p.Coordinates.Longitude))
            .ToList();
    }

    public async Task<List<PharmacySearchResult>> SearchByMedicationAsync(
        Guid medicationId, Coordinates? userLocation = null, int? radiusKm = null)
    {
        try
        {
            _logger.LogInformation("Searching pharmacies with medication {MedicationId}", medicationId);

            // One query: all pharmacies carrying this medication in stock
            var inventoryItems = await _inventoryService.GetPharmaciesWithMedicationAsync(medicationId, minimumQuantity: 1);
            if (inventoryItems.Count == 0) return [];

            var pharmacyIds = inventoryItems.Select(i => i.PharmacyId).ToHashSet();

            // One query for all relevant pharmacies
            var pharmacies = await _dbContext.Pharmacies
                .AsNoTracking()
                .Include(p => p.Coordinates)
                .Include(p => p.Company)
                .Where(p => pharmacyIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

            // One query for the medication name
            var medication = await _medicationService.GetByIdAsync(medicationId);

            var results = new List<PharmacySearchResult>();

            foreach (var inventory in inventoryItems)
            {
                if (!pharmacies.TryGetValue(inventory.PharmacyId, out var pharmacy)) continue;

                double? distanceKm = null;
                if (userLocation != null && pharmacy.Coordinates != null)
                {
                    distanceKm = HaversineMeters(
                        userLocation.Latitude, userLocation.Longitude,
                        pharmacy.Coordinates.Latitude, pharmacy.Coordinates.Longitude) / 1000.0;

                    if (radiusKm.HasValue && distanceKm > radiusKm.Value) continue;
                }

                results.Add(new PharmacySearchResult
                {
                    Pharmacy = pharmacy,
                    DistanceKm = distanceKm,
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
                });
            }

            if (userLocation != null)
                results = [.. results.OrderBy(r => r.DistanceKm ?? double.MaxValue)];

            _logger.LogInformation("Found {Count} pharmacies with medication {MedicationId}", results.Count, medicationId);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching pharmacies by medication {MedicationId}", medicationId);
            throw;
        }
    }

    public async Task<List<PharmacySearchResult>> SearchByMultipleMedicationsAsync(
        List<Guid> medicationIds, Coordinates? userLocation = null, int? radiusKm = null)
    {
        try
        {
            _logger.LogInformation("Searching pharmacies with {Count} medications", medicationIds.Count);

            if (medicationIds == null || medicationIds.Count == 0) return [];

            // ── One query: all pharmacies ──────────────────────────────────────────
            var allPharmacies = await LoadPharmaciesAsync();

            // ── One query: all relevant inventory rows (all meds × all pharmacies) ─
            var medIdSet = medicationIds.ToHashSet();
            var allInventory = await _dbContext.PharmacyInventories
                .AsNoTracking()
                .Where(i => medIdSet.Contains(i.MedicationId) && i.QuantityInStock > 0)
                .ToListAsync();

            // Index: pharmacyId → (medicationId → inventory row)
            var inventoryByPharmacy = allInventory
                .GroupBy(i => i.PharmacyId)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(i => i.MedicationId));

            // ── One query: all medication names ───────────────────────────────────
            var medications = await _dbContext.Medications
                .AsNoTracking()
                .Where(m => medIdSet.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id);

            var results = new List<PharmacySearchResult>();

            foreach (var pharmacy in allPharmacies)
            {
                // Distance filter
                double? distanceKm = null;
                if (userLocation != null && pharmacy.Coordinates != null)
                {
                    distanceKm = HaversineMeters(
                        userLocation.Latitude, userLocation.Longitude,
                        pharmacy.Coordinates.Latitude, pharmacy.Coordinates.Longitude) / 1000.0;

                    if (radiusKm.HasValue && distanceKm > radiusKm.Value) continue;
                }

                // Check stock — pure in-memory dictionary lookups (O(1) per med)
                var pharmacyInventory = inventoryByPharmacy.GetValueOrDefault(pharmacy.Id)
                    ?? new Dictionary<Guid, PharmacyInventory>();

                var availableMeds = new List<MedicationStock>();
                var missingMedIds = new List<Guid>();

                foreach (var medId in medicationIds)
                {
                    if (pharmacyInventory.TryGetValue(medId, out var inv))
                    {
                        availableMeds.Add(new MedicationStock
                        {
                            MedicationId = medId,
                            MedicationName = medications.TryGetValue(medId, out var med)
                                ? med.BrandName : "Unknown",
                            QuantityAvailable = inv.QuantityInStock,
                            ExpirationDate = inv.ExpirationDate,
                            Price = inv.Price
                        });
                    }
                    else
                    {
                        missingMedIds.Add(medId);
                    }
                }

                if (availableMeds.Count == 0) continue;

                results.Add(new PharmacySearchResult
                {
                    Pharmacy = pharmacy,
                    DistanceKm = distanceKm,
                    TotalMedicationsRequested = medicationIds.Count,
                    MedicationsInStock = availableMeds.Count,
                    StockMatchPercentage = (decimal)availableMeds.Count / medicationIds.Count * 100m,
                    CanFulfillCompletely = missingMedIds.Count == 0,
                    AvailableMedications = availableMeds,
                    MissingMedicationIds = missingMedIds
                });
            }

            results = [.. results
                .OrderByDescending(r => r.StockMatchPercentage)
                .ThenBy(r => r.DistanceKm ?? double.MaxValue)];

            _logger.LogInformation("Found {Count} pharmacies with partial/full stock", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching pharmacies by multiple medications");
            throw;
        }
    }

    public async Task<List<PharmacySearchResult>> SearchPharmaciesWithFullPrescriptionStockAsync(
        Guid prescriptionId, Coordinates? userLocation = null, int? radiusKm = null)
    {
        try
        {
            _logger.LogInformation("Searching pharmacies with full prescription stock for {PrescriptionId}", prescriptionId);

            var prescription = await _prescriptionService.GetByIdAsync(prescriptionId)
                ?? throw new InvalidOperationException($"Prescription {prescriptionId} not found");

            var medicationIds = await _dbContext.PrescriptionItems
                .AsNoTracking()
                .Where(pi => pi.PrescriptionId == prescriptionId)
                .Select(pi => pi.MedicationId)
                .ToListAsync();

            if (medicationIds.Count == 0)
            {
                _logger.LogWarning("Prescription {PrescriptionId} has no items", prescriptionId);
                return [];
            }

            var allResults = await SearchByMultipleMedicationsAsync(medicationIds, userLocation, radiusKm);
            var fullStock = allResults.Where(r => r.CanFulfillCompletely).ToList();

            _logger.LogInformation("Found {Count} pharmacies with full prescription stock", fullStock.Count);
            return fullStock;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching pharmacies with full prescription stock");
            throw;
        }
    }

    public async Task<List<PharmacySearchResult>> SearchAndSortByDistanceAndStockAsync(
        List<Guid> medicationIds, Coordinates userLocation, int maxRadiusKm = 50)
    {
        try
        {
            _logger.LogInformation("Searching and sorting pharmacies by distance and stock availability");

            var results = await SearchByMultipleMedicationsAsync(medicationIds, userLocation, maxRadiusKm);
            if (results.Count == 0) return results;

            var maxDistance = results.Max(r => r.DistanceKm ?? 0);
            if (maxDistance == 0) maxDistance = 1;

            foreach (var result in results)
            {
                var dist = result.DistanceKm ?? maxDistance;
                var normalizedDistance = dist / maxDistance;
                result.CompositeScore =
                    result.StockMatchPercentage * 0.6m
                    + (1 - (decimal)normalizedDistance) * 40m;
            }

            results = [.. results.OrderByDescending(r => r.CompositeScore)];

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
