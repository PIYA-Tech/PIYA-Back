using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using PIYA_API.Service.Class;
using PIYA_API.Data;
using PIYA_API.Model;
using Microsoft.EntityFrameworkCore;

namespace PIYA_API.Tests.Unit;

/// <summary>
/// Unit tests for CoordinatesService
/// </summary>
public class CoordinatesServiceTests : IDisposable
{
    private readonly PharmacyApiDbContext _context;
    private readonly CoordinatesService _coordinatesService;

    public CoordinatesServiceTests()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new PharmacyApiDbContext(options);
    _coordinatesService = new CoordinatesService(_context);
    }
    [Fact]
    public async Task CalculateDistance_SameLocation_ReturnsZero()
    {
        // Arrange
        var coords = new Coordinates { Latitude = 40.4093, Longitude = 49.8671 };

        // Act
    var distance = await _coordinatesService.CalculateDistance(coords, coords);

        // Assert (distance in meters)
        distance.Should().Be(0);
    }

    [Fact]
    public async Task CalculateDistance_BakuToTbilisi_ReturnsCorrectDistance()
    {
        // Arrange - Baku coordinates
        var baku = new Coordinates { Latitude = 40.4093, Longitude = 49.8671 };
        var tbilisi = new Coordinates { Latitude = 41.7151, Longitude = 44.8271 };

        // Act
    var distanceMeters = await _coordinatesService.CalculateDistance(baku, tbilisi);

        // Assert ~470 km -> 470000 meters (allow 50 km tolerance)
        distanceMeters.Should().BeInRange(470000 - 50000, 470000 + 50000);
    }

    [Fact]
    public async Task CalculateDistance_NewYorkToLondon_ReturnsCorrectDistance()
    {
        var ny = new Coordinates { Latitude = 40.7128, Longitude = -74.0060 };
        var london = new Coordinates { Latitude = 51.5074, Longitude = -0.1278 };

    var distanceMeters = await _coordinatesService.CalculateDistance(ny, london);

        // ~5570 km -> 5,570,000 meters
        distanceMeters.Should().BeInRange(5570000 - 100000, 5570000 + 100000);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(90, 0, -90, 0, 20015)] // North pole to South pole ~20015 km
    public async Task CalculateDistance_EdgeCases_ReturnsExpectedDistance(
        double lat1, double lng1, double lat2, double lng2, double expectedDistanceKm)
    {
        var a = new Coordinates { Latitude = lat1, Longitude = lng1 };
        var b = new Coordinates { Latitude = lat2, Longitude = lng2 };

    var distanceMeters = await _coordinatesService.CalculateDistance(a, b);

        var expectedMeters = (int)(expectedDistanceKm * 1000);
        // allow large tolerance for pole distances
        var tolerance = 500000;
        distanceMeters.Should().BeInRange(expectedMeters - tolerance, expectedMeters + tolerance);
    }

    [Fact]
    public async Task GetCountry_ExistingPharmacy_ReturnsCountry()
    {
        // Arrange - create a pharmacy with coordinates and country/city
        var pharmacy = new Pharmacy
        {
            Id = Guid.NewGuid(),
            Name = "Test Pharmacy",
            Address = "123 Test St",
            Country = "Azerbaijan",
            City = "Baku",
            Coordinates = new Coordinates { Id = Guid.NewGuid(), Latitude = 40.4093, Longitude = 49.8671 },
            Company = new PharmacyCompany { Id = Guid.NewGuid(), Name = "TestCo" }
        };
        await _context.Pharmacies.AddAsync(pharmacy);
        await _context.SaveChangesAsync();

        // Act
        var result = await _coordinatesService.GetCountry(new Coordinates { Latitude = 40.4093, Longitude = 49.8671 });

        // Assert
        result.Should().Be("Azerbaijan");
    }

    [Fact]
    public async Task GetCity_ExistingPharmacy_ReturnsCity()
    {
        // Arrange
        var pharmacy = new Pharmacy
        {
            Id = Guid.NewGuid(),
            Name = "Test Pharmacy",
            Address = "123 Test St",
            Country = "Azerbaijan",
            City = "Ganja",
            Coordinates = new Coordinates { Id = Guid.NewGuid(), Latitude = 40.6828, Longitude = 46.3606 },
            Company = new PharmacyCompany { Id = Guid.NewGuid(), Name = "TestCo" }
        };
        await _context.Pharmacies.AddAsync(pharmacy);
        await _context.SaveChangesAsync();

        // Act
        var result = await _coordinatesService.GetCity(new Coordinates { Latitude = 40.6828, Longitude = 46.3606 });

        // Assert
        result.Should().Be("Ganja");
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
