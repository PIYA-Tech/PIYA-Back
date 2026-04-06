using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;

namespace PIYA_API.Tests.Unit;

public class PharmacyInventoryServiceTests : IDisposable
{
    private readonly PharmacyApiDbContext _context;
    private readonly InventoryService _service;
    private readonly Guid _pharmacyId = Guid.NewGuid();
    private readonly Guid _medicationId = Guid.NewGuid();

    public PharmacyInventoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new PharmacyApiDbContext(options);

        // Seed required related entities
        _context.Pharmacies.Add(new Pharmacy
        {
            Id = _pharmacyId,
            Name = "Test Pharmacy",
            Address = "123 Main St",
            City = "Baku",
            Country = "Azerbaijan",
            PhoneNumber = "0000000000",
            IsActive = true,
            Coordinates = new Coordinates { Latitude = 40.4093, Longitude = 49.8671 },
            Company = new PharmacyCompany { Name = "Test Company" }
        });
        _context.Medications.Add(new Medication
        {
            Id = _medicationId,
            BrandName = "Paracetamol",
            GenericName = "Paracetamol",
            Form = "Tablet",
            Strength = "500mg",
        });
        _context.SaveChanges();

        _service = new InventoryService(
            _context,
            Mock.Of<IAuditService>(),
            Mock.Of<ILogger<InventoryService>>());
    }

    [Fact]
    public async Task AddOrUpdateInventory_NewItem_CreatesRecord()
    {
        var inventory = MakeInventory(quantity: 100);

        var result = await _service.AddOrUpdateInventoryAsync(inventory);

        result.Id.Should().NotBe(Guid.Empty);
        result.QuantityInStock.Should().Be(100);
    }

    [Fact]
    public async Task AddOrUpdateInventory_ExistingItem_UpdatesQuantity()
    {
        var inventory = MakeInventory(quantity: 50);
        await _service.AddOrUpdateInventoryAsync(inventory);

        var updated = MakeInventory(quantity: 75);
        var result = await _service.AddOrUpdateInventoryAsync(updated);

        result.QuantityInStock.Should().Be(75);
        _context.PharmacyInventories
            .Count(i => i.PharmacyId == _pharmacyId && i.MedicationId == _medicationId)
            .Should().Be(1);
    }

    [Fact]
    public async Task IsInStock_WhenEnoughQuantity_ReturnsTrue()
    {
        await _service.AddOrUpdateInventoryAsync(MakeInventory(quantity: 20));

        var result = await _service.IsInStockAsync(_pharmacyId, _medicationId, requiredQuantity: 10);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsInStock_WhenNotEnoughQuantity_ReturnsFalse()
    {
        await _service.AddOrUpdateInventoryAsync(MakeInventory(quantity: 5));

        var result = await _service.IsInStockAsync(_pharmacyId, _medicationId, requiredQuantity: 10);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetLowStockItems_WhenBelowMinimum_ReturnsItem()
    {
        await _service.AddOrUpdateInventoryAsync(MakeInventory(quantity: 3, minimumStockLevel: 10));

        var lowStock = await _service.GetLowStockItemsAsync(_pharmacyId);

        lowStock.Should().ContainSingle();
        lowStock[0].MedicationId.Should().Be(_medicationId);
    }

    [Fact]
    public async Task GetLowStockItems_WhenAboveMinimum_ReturnsEmpty()
    {
        await _service.AddOrUpdateInventoryAsync(MakeInventory(quantity: 50, minimumStockLevel: 10));

        var lowStock = await _service.GetLowStockItemsAsync(_pharmacyId);

        lowStock.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableStock_WhenExists_ReturnsQuantity()
    {
        await _service.AddOrUpdateInventoryAsync(MakeInventory(quantity: 42));

        var stock = await _service.GetAvailableStockAsync(_pharmacyId, _medicationId);

        stock.Should().Be(42);
    }

    [Fact]
    public async Task GetAvailableStock_WhenNoRecord_ReturnsZero()
    {
        var stock = await _service.GetAvailableStockAsync(_pharmacyId, _medicationId);

        stock.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var inv = await _service.AddOrUpdateInventoryAsync(MakeInventory(quantity: 10));

        var deleted = await _service.DeleteAsync(inv.Id);

        deleted.Should().BeTrue();
        var stored = await _service.GetByIdAsync(inv.Id);
        stored.Should().BeNull();
    }

    [Fact]
    public async Task PharmacyInventory_IsLowStock_CorrectWhenBelowMinimum()
    {
        var inv = MakeInventory(quantity: 2, minimumStockLevel: 5);

        inv.IsLowStock().Should().BeTrue();
    }

    [Fact]
    public async Task PharmacyInventory_IsLowStock_FalseWhenAboveMinimum()
    {
        var inv = MakeInventory(quantity: 10, minimumStockLevel: 5);

        inv.IsLowStock().Should().BeFalse();
    }

    // ── helpers ──────────────────────────────────────────────

    private PharmacyInventory MakeInventory(int quantity = 100, int minimumStockLevel = 10) => new()
    {
        PharmacyId = _pharmacyId,
        MedicationId = _medicationId,
        QuantityInStock = quantity,
        MinimumStockLevel = minimumStockLevel,
        ReorderQuantity = 50,
        Price = 2.50m,
        Currency = "AZN",
        IsAvailable = true
    };

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
