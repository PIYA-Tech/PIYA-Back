using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PIYA_API.DTOs;
using PIYA_API.Hubs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class PharmacyController(
    ISearchService searchService,
    IPharmacyService pharmacyService,
    IHubContext<PharmacyHub> pharmacyHub,
    ILogger<PharmacyController> logger) : ControllerBase
{
    private readonly ISearchService _searchService = searchService;
    private readonly IPharmacyService _pharmacyService = pharmacyService;
    private readonly IHubContext<PharmacyHub> _pharmacyHub = pharmacyHub;
    private readonly ILogger<PharmacyController> _logger = logger;

    /// <summary>
    /// Get all pharmacies (public)
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var pharmacies = await _pharmacyService.GetAll();
        var dtos = pharmacies.Select(ToPublicDto).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPharmacy(Guid id)
    {
        var pharmacy = await _pharmacyService.GetById(id);
        if (pharmacy == null)
        {
            return NotFound("Pharmacy not found.");
        }
        return Ok(ToPublicDto(pharmacy));
    }

    [HttpPost("create")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreatePharmacy([FromBody] PharmacyUpsertDto dto)
    {
        var pharmacy = DtoToPharmacy(dto);
        var createdPharmacy = await _pharmacyService.Create(pharmacy);
        await _pharmacyHub.Clients.Group("pharmacies")
            .SendAsync("PharmacyCreated", createdPharmacy);
        return CreatedAtAction(nameof(GetPharmacy), new { id = createdPharmacy.Id }, createdPharmacy);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdatePharmacy(Guid id, [FromBody] PharmacyUpsertDto dto)
    {
        try
        {
            var pharmacy = DtoToPharmacy(dto);
            pharmacy.Id = id;
            var updated = await _pharmacyService.Update(pharmacy);
            await _pharmacyHub.Clients.Group("pharmacies")
                .SendAsync("PharmacyUpdated", updated);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Pharmacy not found" });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> DeletePharmacy(Guid id)
    {
        try
        {
            await _pharmacyService.Delete(id);
            await _pharmacyHub.Clients.Group("pharmacies")
                .SendAsync("PharmacyDeleted", id);
            return Ok(new { message = "Pharmacy deleted successfully" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Pharmacy not found" });
        }
    }

    [HttpGet("searchByCountry")]
    [AllowAnonymous]
    public async Task<IActionResult> SearchByCountry([FromQuery] Coordinates coordinates)
    {
        var pharmacies = await _searchService.SearchByCountry(coordinates);
        return Ok(pharmacies ?? []);
    }
    [HttpGet("searchByCity")]
    [AllowAnonymous]
    public async Task<IActionResult> SearchByCity([FromQuery] Coordinates coordinates)
    {
        var pharmacies = await _searchService.SearchByCity(coordinates);
        return Ok(pharmacies ?? []);
    }
    [HttpGet("searchByRadius")]
    [AllowAnonymous]
    public async Task<IActionResult> SearchByRadius([FromQuery] Coordinates coordinates, [FromQuery] int radius)
    {
        var pharmacies = await _searchService.SearchByRadius(coordinates, radius);
        return Ok(pharmacies ?? []);
    }

    /// <summary>
    /// Search pharmacies by single medication availability
    /// </summary>
    [HttpGet("search/by-medication/{medicationId}")]
    [AllowAnonymous]
    public async Task<IActionResult> SearchByMedication(
        Guid medicationId,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] int? radiusKm = null)
    {
        try
        {
            Coordinates? userLocation = null;
            if (latitude.HasValue && longitude.HasValue)
            {
                userLocation = new Coordinates
                {
                    Latitude = latitude.Value,
                    Longitude = longitude.Value
                };
            }

            var results = await _searchService.SearchByMedicationAsync(medicationId, userLocation, radiusKm);

            return Ok(new
            {
                totalResults = results?.Count ?? 0,
                searchCriteria = new
                {
                    medicationId,
                    radiusKm,
                    hasLocation = userLocation != null
                },
                pharmacies = results ?? []
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search pharmacies by medication");
            return StatusCode(500, new { error = "Failed to search pharmacies by medication", details = ex.Message });
        }
    }

    /// <summary>
    /// Search pharmacies that have ALL specified medications in stock
    /// </summary>
    [HttpPost("search/by-multiple-medications")]
    [AllowAnonymous]
    public async Task<IActionResult> SearchByMultipleMedications([FromBody] MultipleMedicationsSearchRequest request)
    {
        try
        {
            if (request.MedicationIds == null || request.MedicationIds.Count == 0)
            {
                return BadRequest(new { error = "At least one medication ID is required" });
            }

            Coordinates? userLocation = null;
            if (request.Latitude.HasValue && request.Longitude.HasValue)
            {
                userLocation = new Coordinates
                {
                    Latitude = request.Latitude.Value,
                    Longitude = request.Longitude.Value
                };
            }

            var results = await _searchService.SearchByMultipleMedicationsAsync(
                request.MedicationIds, 
                userLocation, 
                request.RadiusKm);

            var pharmaciesWithFullStock = results?.Count(r => r.CanFulfillCompletely) ?? 0;

            return Ok(new
            {
                totalResults = results?.Count ?? 0,
                pharmaciesWithFullStock,
                pharmaciesWithPartialStock = (results?.Count ?? 0) - pharmaciesWithFullStock,
                searchCriteria = new
                {
                    medicationCount = request.MedicationIds.Count,
                    radiusKm = request.RadiusKm,
                    hasLocation = userLocation != null
                },
                pharmacies = results ?? []
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search pharmacies by multiple medications");
            return StatusCode(500, new { error = "Failed to search pharmacies by multiple medications", details = ex.Message });
        }
    }

    /// <summary>
    /// Search pharmacies that can fulfill an entire prescription
    /// </summary>
    /// <param name="prescriptionId">The prescription ID</param>
    /// <param name="latitude">User's latitude (optional)</param>
    /// <param name="longitude">User's longitude (optional)</param>
    /// <param name="radiusKm">Maximum distance in kilometers (optional)</param>
    /// <returns>Pharmacies that have ALL medications from the prescription in stock</returns>
    [HttpGet("search/by-prescription/{prescriptionId}")]
    public async Task<IActionResult> SearchByPrescription(
        Guid prescriptionId,
        [FromQuery] double? latitude = null,
        [FromQuery] double? longitude = null,
        [FromQuery] int? radiusKm = null)
    {
        try
        {
            Coordinates? userLocation = null;
            if (latitude.HasValue && longitude.HasValue)
            {
                userLocation = new Coordinates
                {
                    Latitude = latitude.Value,
                    Longitude = longitude.Value
                };
            }

            var results = await _searchService.SearchPharmaciesWithFullPrescriptionStockAsync(
                prescriptionId, 
                userLocation, 
                radiusKm);

            return Ok(new
            {
                totalResults = results?.Count ?? 0,
                prescriptionId,
                searchCriteria = new
                {
                    radiusKm,
                    hasLocation = userLocation != null
                },
                pharmacies = results ?? []
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search pharmacies by prescription {PrescriptionId}", prescriptionId);
            return StatusCode(500, new { error = "Failed to search pharmacies by prescription", details = ex.Message });
        }
    }

    /// <summary>
    /// Advanced search with composite scoring (distance + stock availability)
    /// </summary>
    /// <param name="request">Search request with medications and location</param>
    /// <returns>Pharmacies sorted by composite score (60% stock match + 40% proximity)</returns>
    [HttpPost("search/smart")]
    public async Task<IActionResult> SmartSearch([FromBody] SmartSearchRequest request)
    {
        try
        {
            if (request.MedicationIds == null || request.MedicationIds.Count == 0)
            {
                return BadRequest(new { error = "At least one medication ID is required" });
            }

            if (!request.Latitude.HasValue || !request.Longitude.HasValue)
            {
                return BadRequest(new { error = "User location (latitude and longitude) is required for smart search" });
            }

            var userLocation = new Coordinates
            {
                Latitude = request.Latitude.Value,
                Longitude = request.Longitude.Value
            };

            var maxRadius = request.MaxRadiusKm ?? 50;

            var results = await _searchService.SearchAndSortByDistanceAndStockAsync(
                request.MedicationIds, 
                userLocation, 
                maxRadius);

            return Ok(new
            {
                totalResults = results?.Count ?? 0,
                algorithm = "Composite Score: 60% stock availability + 40% proximity",
                searchCriteria = new
                {
                    medicationCount = request.MedicationIds.Count,
                    maxRadiusKm = maxRadius,
                    userLocation = new { request.Latitude, request.Longitude }
                },
                pharmacies = (results ?? []).Select(r => new
                {
                    r.Pharmacy,
                    r.DistanceKm,
                    r.StockMatchPercentage,
                    r.CompositeScore,
                    r.CanFulfillCompletely,
                    r.MedicationsInStock,
                    r.TotalMedicationsRequested,
                    r.AvailableMedications,
                    r.MissingMedicationIds
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform smart pharmacy search");
            return StatusCode(500, new { error = "Failed to perform smart search", details = ex.Message });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PharmacyPublicDto ToPublicDto(Pharmacy p) => new()
    {
        Id             = p.Id,
        Name           = p.Name,
        Country        = p.Country,
        Address        = p.Address,
        City           = p.City,
        PhoneNumber    = p.PhoneNumber,
        Email          = p.Email,
        Website        = p.Website,
        OperatingHours = p.OperatingHours,
        Services       = p.Services,
        IsActive       = p.IsActive,
        Is24Hours      = p.Is24Hours,
        AverageRating  = p.AverageRating,
        TotalRatings   = p.TotalRatings,
        CompanyName    = p.Company?.Name,
        Coordinates    = p.Coordinates is { } c
            ? new CoordinatesDto { Lat = c.Latitude, Lng = c.Longitude }
            : null,
    };

    private static Pharmacy DtoToPharmacy(PharmacyUpsertDto dto) => new()
    {
        Name             = dto.Name,
        Country          = dto.Country,
        Address          = dto.Address,
        City             = dto.City,
        PhoneNumber      = dto.PhoneNumber,
        Email            = dto.Email,
        Website          = dto.Website,
        EmergencyContact = dto.EmergencyContact,
        Services         = dto.Services ?? [],
        OperatingHours   = dto.OperatingHours,
        // Coordinates is required on the entity; create a stub if none provided
        Coordinates      = dto.Coordinates is { } c
            ? new Coordinates { Latitude = c.Lat, Longitude = c.Lng }
            : new Coordinates { Latitude = 0, Longitude = 0 },
        // Company is required on the entity; the service's Update() ignores it,
        // and Create() should be preceded by a company association — stub for now.
        Company          = new PIYA_API.Model.PharmacyCompany { Name = string.Empty },
    };
}

// DTOs for request bodies
public class MultipleMedicationsSearchRequest
{
    public List<Guid> MedicationIds { get; set; } = [];
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusKm { get; set; }
}

public class SmartSearchRequest
{
    public List<Guid> MedicationIds { get; set; } = [];
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? MaxRadiusKm { get; set; }
}
