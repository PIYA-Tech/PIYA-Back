using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Controllers;
using System.Security.Claims;
using System.Text.Json;
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
        result.QuantityInStock.Should().Be(0);
        result.ReportedQuantity.Should().Be(100);
    }

    [Fact]
    public async Task AddOrUpdateInventory_ExistingItem_UpdatesQuantity()
    {
        var inventory = MakeInventory(quantity: 50);
        await _service.AddOrUpdateInventoryAsync(inventory);

        var updated = MakeInventory(quantity: 75);
        var result = await _service.AddOrUpdateInventoryAsync(updated);

        result.QuantityInStock.Should().Be(0);
        result.ReportedQuantity.Should().Be(75);
        _context.PharmacyInventories
            .Count(i => i.PharmacyId == _pharmacyId && i.MedicationId == _medicationId)
            .Should().Be(1);
    }

    [Fact]
    public async Task IsInStock_WhenEnoughQuantity_ReturnsTrue()
    {
        await AddVerifiedStock(20);

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
        await AddVerifiedStock(50);

        var lowStock = await _service.GetLowStockItemsAsync(_pharmacyId);

        lowStock.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableStock_WhenExists_ReturnsQuantity()
    {
        await AddVerifiedStock(42);

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

    [Fact]
    public async Task QuantityOnlyWritesNeverCreateDispensableStock()
    {
        var item = await _service.AddOrUpdateInventoryAsync(MakeInventory(20));
        await _service.UpdateStockAsync(item.Id, 30);
        await _service.IncreaseStockAsync(_pharmacyId, _medicationId, 5);
        item.ReportedQuantity.Should().Be(35);
        (await _service.GetAvailableStockAsync(_pharmacyId, _medicationId)).Should().Be(0);
        _context.InventoryBatches.Should().BeEmpty();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DecreaseStockAsync(_pharmacyId, _medicationId, 1));
    }

    [Fact]
    public async Task ReportAdjustmentCannotEraseOrRecreateVerifiedBatchStock()
    {
        var item = await AddVerifiedStock(10);
        await _service.UpdateStockAsync(item.Id, 0);
        await _service.DecreaseStockAsync(_pharmacyId, _medicationId, 1);
        item.QuantityInStock.Should().Be(9);
        item.ReportedQuantity.Should().Be(0);
        await _service.UpdateStockAsync(item.Id, 100);
        (await _service.GetAvailableStockAsync(_pharmacyId, _medicationId)).Should().Be(9);
        _context.InventoryBatches.Single().Quantity.Should().Be(9);
    }

    [Fact]
    public async Task ExpiredUnknownAndUnnamedLotsNeverAppearAsAvailable()
    {
        var item = await _service.AddOrUpdateInventoryAsync(MakeInventory(99));
        _context.InventoryBatches.AddRange(
            new InventoryBatch { Id = Guid.NewGuid(), PharmacyInventoryId = item.Id, BatchNumber = "expired", Quantity = 8, ExpirationDate = DateTime.UtcNow.AddDays(-1) },
            new InventoryBatch { Id = Guid.NewGuid(), PharmacyInventoryId = item.Id, BatchNumber = "unknown", Quantity = 9 },
            new InventoryBatch { Id = Guid.NewGuid(), PharmacyInventoryId = item.Id, BatchNumber = " ", Quantity = 10, ExpirationDate = DateTime.UtcNow.AddDays(1) });
        item.QuantityInStock = 99; // Deliberately stale legacy cache.
        await _context.SaveChangesAsync();
        (await _service.GetAvailableStockAsync(_pharmacyId, _medicationId)).Should().Be(0);
        (await _service.GetByIdAsync(item.Id))!.QuantityInStock.Should().Be(0);
        (await _service.GetPharmaciesWithMedicationAsync(_medicationId)).Should().BeEmpty();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DecreaseStockAsync(_pharmacyId, _medicationId, 1));
    }

    [Fact]
    public async Task ExpiryImmediatelyChangesReadsWithoutCleanupWorker()
    {
        var item = await AddVerifiedStock(10);
        _context.InventoryBatches.Single().ExpirationDate = DateTime.UtcNow.AddSeconds(-1);
        await _context.SaveChangesAsync();
        (await _service.GetByIdAsync(item.Id))!.QuantityInStock.Should().Be(0);
        (await _service.GetPharmacyInventoryAsync(_pharmacyId)).Single().QuantityInStock.Should().Be(0);
        (await _service.IsInStockAsync(_pharmacyId, _medicationId)).Should().BeFalse();
        await _service.RemoveExpiredBatchesAsync();
        item.QuantityInStock.Should().Be(0);
    }

    [Fact]
    public async Task BatchCreationRequiresNamedKnownFutureExpiryAndRejectsDuplicateLot()
    {
        var item = await _service.AddOrUpdateInventoryAsync(MakeInventory(3));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.AddBatchAsync(new InventoryBatch { PharmacyInventoryId = item.Id, BatchNumber = "lot", Quantity = 3 }));
        var batch = new InventoryBatch { PharmacyInventoryId = item.Id, BatchNumber = "lot", Quantity = 3, ExpirationDate = DateTime.UtcNow.AddDays(30) };
        await _service.AddBatchAsync(batch);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddBatchAsync(new InventoryBatch { PharmacyInventoryId = item.Id, BatchNumber = "lot", Quantity = 3, ExpirationDate = DateTime.UtcNow.AddDays(30) }));
        _context.InventoryBatches.Should().ContainSingle();
    }

    [Fact]
    public async Task LotCorrectionAndReviewRequireCurrentVersionAndPreserveHistory()
    {
        var item = await _service.AddOrUpdateInventoryAsync(MakeInventory(7));
        var batch = new InventoryBatch { Id = Guid.NewGuid(), PharmacyInventoryId = item.Id, BatchNumber = "legacy", Quantity = 7 };
        _context.InventoryBatches.Add(batch);
        await _context.SaveChangesAsync();
        var actor = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AcknowledgeReconciliationAsync(item.Id, item.Version, "Reviewed", actor));
        await _service.CorrectBatchAsync(batch.Id, new(item.Version, "actual-lot", 7, DateTime.UtcNow.AddDays(30), true, "Read expiry from existing package"), actor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CorrectBatchAsync(batch.Id, new(0, "actual-lot", 7, DateTime.UtcNow.AddDays(30), true, "stale"), actor));
        await _service.AcknowledgeReconciliationAsync(item.Id, item.Version, "Counted seven eligible units", actor);
        item.RequiresReconciliation.Should().BeFalse();
        _context.InventoryHistories.Should().Contain(h => h.PerformedByUserId == actor && h.Notes!.Contains("Previous:"));
    }

    [Fact]
    public async Task ReconciliationCannotClearDuplicateRowsEvenWhenBothCountsMatch()
    {
        var first = await AddVerifiedStock(7);
        var second = MakeInventory(7);
        second.Id = Guid.NewGuid();
        second.ReportedQuantity = 7;
        second.RequiresReconciliation = true;
        _context.PharmacyInventories.Add(second);
        await _context.SaveChangesAsync();
        await _service.AddBatchAsync(new InventoryBatch { PharmacyInventoryId = second.Id,
            BatchNumber = "second-legacy-row-lot", Quantity = 7, ExpirationDate = DateTime.UtcNow.AddDays(30) });
        var historyCount = await _context.InventoryHistories.CountAsync();
        foreach (var item in new[] { first, second })
        {
            var version = item.Version;
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.AcknowledgeReconciliationAsync(item.Id, version, "Counts match", Guid.NewGuid()));
            failure.Message.Should().Contain("Duplicate inventory records");
            item.RequiresReconciliation.Should().BeTrue();
            item.Version.Should().Be(version);
        }
        (await _context.InventoryHistories.CountAsync()).Should().Be(historyCount);
        (await _context.InventoryBatches.SumAsync(batch => batch.Quantity)).Should().Be(14);
    }

    [Fact]
    public async Task ReconciliationListsDuplicateIdsEvenWhenStoredFlagsAndCountsLookClean()
    {
        var first = await _service.AddOrUpdateInventoryAsync(MakeInventory(0));
        var second = MakeInventory(0);
        second.Id = Guid.NewGuid();
        second.ReportedQuantity = 0;
        _context.PharmacyInventories.Add(second);
        await _context.SaveChangesAsync();
        first.RequiresReconciliation.Should().BeFalse();
        second.RequiresReconciliation.Should().BeFalse();
        var controller = new PharmacyManagerController(Mock.Of<IPharmacyService>(), Mock.Of<IPharmacyStaffService>(),
            _service, Mock.Of<IPrescriptionService>(), Mock.Of<ILogger<PharmacyManagerController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test")) } }
        };

        var result = (await controller.Reconciliation(_pharmacyId)).Should().BeOfType<OkObjectResult>().Subject;
        var rows = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .EnumerateArray().ToArray();
        rows.Should().HaveCount(2);
        foreach (var row in rows)
        {
            row.GetProperty("requiresReconciliation").GetBoolean().Should().BeTrue();
            row.GetProperty("duplicateInventoryIds").EnumerateArray().Select(id => id.GetGuid())
                .Should().BeEquivalentTo(new[] { first.Id, second.Id });
        }
    }

    [Fact]
    public async Task SnapshotRetryIsIdempotentAndCannotUndoDispensing()
    {
        var item = await AddVerifiedStock(10);
        var actor = Guid.NewGuid();
        var metadata = new InventorySyncMetadata(Guid.NewGuid(), Guid.NewGuid(), 1, DateTime.UtcNow);
        var rows = new[] { new InventoryReportItem(_medicationId, 100) };
        var first = await _service.SyncReportedStockAsync(_pharmacyId, rows, actor, metadata);
        first.SyncProtocolVersion.Should().Be(1);
        await _service.DecreaseStockAsync(_pharmacyId, _medicationId, 3);
        var historyCount = _context.InventoryHistories.Count();
        var retry = await _service.SyncReportedStockAsync(_pharmacyId, rows, actor, metadata);
        retry.Should().BeEquivalentTo(first);
        _context.InventoryHistories.Count().Should().Be(historyCount);
        (await _service.GetAvailableStockAsync(_pharmacyId, _medicationId)).Should().Be(7);
        item.ReportedQuantity.Should().Be(100);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SyncReportedStockAsync(_pharmacyId,
            [new(_medicationId, 200)], actor, metadata));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SyncReportedStockAsync(_pharmacyId,
            rows, Guid.NewGuid(), metadata));
    }

    [Fact]
    public async Task SnapshotRejectsOutOfOrderAndPreManualUpdateCaptures()
    {
        var item = await AddVerifiedStock(10);
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        await _service.SyncReportedStockAsync(_pharmacyId, [new(_medicationId, 25)], actor,
            new(Guid.NewGuid(), source, 2, DateTime.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SyncReportedStockAsync(_pharmacyId,
            [new(_medicationId, 99)], actor, new(Guid.NewGuid(), source, 1, DateTime.UtcNow)));
        var oldCapture = DateTime.UtcNow.AddMinutes(-1);
        await _service.UpdateStockAsync(item.Id, 17);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SyncReportedStockAsync(_pharmacyId,
            [new(_medicationId, 99)], actor, new(Guid.NewGuid(), Guid.NewGuid(), 1, oldCapture)));
        item.ReportedQuantity.Should().Be(17);
    }

    private async Task<PharmacyInventory> AddVerifiedStock(int count)
    {
        var item = await _service.AddOrUpdateInventoryAsync(MakeInventory(count));
        await _service.AddBatchAsync(new InventoryBatch { PharmacyInventoryId = item.Id, BatchNumber = Guid.NewGuid().ToString(),
            Quantity = count, ExpirationDate = DateTime.UtcNow.AddDays(30) });
        return item;
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
