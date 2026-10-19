using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PIYA_API.Data;
using PIYA_API.Migrations;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Integration;

public sealed class InventoryConsistencyTests(PiyaWebApplicationFactory factory) : IClassFixture<PiyaWebApplicationFactory>
{
    private async Task<(Guid Pharmacy, Guid Medication, Guid Inventory, Guid Actor)> Setup(int stock = 5)
    {
        await factory.EnsureMigratedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        var pharmacy = new Pharmacy { Id = Guid.NewGuid(), Name = "Synthetic inventory test", Address = "Test", Country = "Test",
            Coordinates = new Coordinates { Latitude = 1, Longitude = 1 }, Company = new PharmacyCompany { Name = "Synthetic" } };
        var medication = new Medication { Id = Guid.NewGuid(), BrandName = "Synthetic", GenericName = "Synthetic", Form = "Test", Strength = "Test" };
        db.AddRange(pharmacy, medication);
        await db.SaveChangesAsync();
        var actor = await db.Users.Where(u => u.Username == PiyaWebApplicationFactory.IntegrationAdminUsername).Select(u => u.Id).SingleAsync();
        var service = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var inventory = await service.AddOrUpdateInventoryAsync(new PharmacyInventory { PharmacyId = pharmacy.Id, MedicationId = medication.Id, QuantityInStock = stock });
        if (stock > 0) await service.AddBatchAsync(new InventoryBatch { PharmacyInventoryId = inventory.Id,
            BatchNumber = "synthetic-lot", Quantity = stock, ExpirationDate = DateTime.UtcNow.AddDays(60) }, actor);
        return (pharmacy.Id, medication.Id, inventory.Id, actor);
    }

    private async Task<T> WithService<T>(Func<IInventoryService, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<IInventoryService>());
    }

    [Fact]
    public async Task ConcurrentDispensesSerializeAndCannotOversell()
    {
        var data = await Setup();
        async Task<bool> Dispense()
        {
            try { await WithService(s => s.DecreaseStockAsync(data.Pharmacy, data.Medication, 4, data.Actor)); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(Dispense(), Dispense());
        results.Count(r => r).Should().Be(1);
        (await WithService(s => s.GetAvailableStockAsync(data.Pharmacy, data.Medication))).Should().Be(1);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        (await db.InventoryBatches.SingleAsync(b => b.PharmacyInventoryId == data.Inventory)).Quantity.Should().Be(1);
        (await db.PharmacyInventories.FindAsync(data.Inventory))!.ReportedQuantity.Should().Be(5);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"InventoryBatches\" SET \"Quantity\" = -1 WHERE \"PharmacyInventoryId\" = {data.Inventory}"));
        failure.SqlState.Should().Be("23514");
    }

    [Fact]
    public async Task ConcurrentSnapshotRetriesApplyOnceAndNeverRestoreDispensedLots()
    {
        var data = await Setup();
        var metadata = new InventorySyncMetadata(Guid.NewGuid(), Guid.NewGuid(), 1, DateTime.UtcNow);
        InventoryReportItem[] rows = [new(data.Medication, 50)];
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => WithService(s =>
            s.SyncReportedStockAsync(data.Pharmacy, rows, data.Actor, metadata))));
        results.Should().OnlyContain(r => r.SyncProtocolVersion == 1 && r.Updated == 1);
        await WithService(s => s.DecreaseStockAsync(data.Pharmacy, data.Medication, 2, data.Actor));
        await WithService(s => s.SyncReportedStockAsync(data.Pharmacy, rows, data.Actor, metadata));
        (await WithService(s => s.GetAvailableStockAsync(data.Pharmacy, data.Medication))).Should().Be(3);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        (await db.Set<InventorySyncReceipt>().CountAsync(r => r.PharmacyId == data.Pharmacy)).Should().Be(1);
        (await db.InventoryHistories.CountAsync(h => h.PharmacyInventoryId == data.Inventory && h.TransactionType == InventoryTransactionType.Report))
            .Should().Be(2, "one initial report and one applied snapshot, despite three concurrent retries");
    }

    [Fact]
    public async Task InvalidSnapshotCannotPartiallyUpdateOrConsumeItsRequestIdentity()
    {
        var data = await Setup();
        var metadata = new InventorySyncMetadata(Guid.NewGuid(), Guid.NewGuid(), 1, DateTime.UtcNow);
        await Assert.ThrowsAsync<ArgumentException>(() => WithService(s => s.SyncReportedStockAsync(data.Pharmacy,
            [new(data.Medication, 99), new(Guid.NewGuid(), 20)], data.Actor, metadata)));
        var inventory = await WithService(s => s.GetByIdAsync(data.Inventory));
        inventory!.ReportedQuantity.Should().Be(5);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        (await db.Set<InventorySyncReceipt>().AnyAsync(r => r.RequestId == metadata.RequestId)).Should().BeFalse();
    }

    [Fact]
    public async Task HttpReportBatchAndDispenseJourneyPreservesStockOnRetry()
    {
        var data = await Setup(0);
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var token = await scope.ServiceProvider.GetRequiredService<IJwtService>()
            .GenerateSecurityToken(PiyaWebApplicationFactory.IntegrationAdminUsername);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        var requestId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var capturedAt = DateTime.UtcNow.ToString("O");
        async Task<HttpResponseMessage> Report(Guid id, long revision, int quantity)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/pharmacymanager/{data.Pharmacy}/inventory/sync");
            request.Headers.Add("X-PIYA-Sync-Request-Id", id.ToString());
            request.Headers.Add("X-PIYA-Sync-Source-Id", sourceId.ToString());
            request.Headers.Add("X-PIYA-Sync-Revision", revision.ToString());
            request.Headers.Add("X-PIYA-Sync-Captured-At", capturedAt);
            request.Content = JsonContent.Create(new[] { new { medicationId = data.Medication, quantityInStock = quantity } });
            return await client.SendAsync(request);
        }
        var response = await Report(requestId, 1, 20);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("syncProtocolVersion").GetInt32().Should().Be(1);
        var countOnly = await client.GetFromJsonAsync<JsonElement>($"/api/pharmacyinventory/{data.Inventory}");
        countOnly.GetProperty("reportedQuantity").GetInt32().Should().Be(20);
        countOnly.GetProperty("quantityInStock").GetInt32().Should().Be(0);
        var deduction = new { pharmacyId = data.Pharmacy, medicationId = data.Medication, quantity = 2 };
        (await client.PostAsJsonAsync("/api/pharmacyinventory/decrease", deduction)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/pharmacyinventory/batch", new {
            inventoryId = data.Inventory, batchNumber = "verified-http-lot", quantity = 20,
            expirationDate = DateTime.UtcNow.AddDays(30)
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/pharmacyinventory/decrease", deduction)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Report(requestId, 1, 20)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Report(Guid.NewGuid(), 1, 99)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var final = await client.GetFromJsonAsync<JsonElement>($"/api/pharmacyinventory/{data.Inventory}");
        final.GetProperty("quantityInStock").GetInt32().Should().Be(18);
        final.GetProperty("reportedQuantity").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task CapabilitiesRequiresAuthorizationBeforeAnySnapshotWrite()
    {
        var data = await Setup();
        using var client = factory.CreateClient();
        var path = $"/api/pharmacymanager/{data.Pharmacy}/inventory/sync/capabilities";
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var scope = factory.Services.CreateScope();
        var token = await scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateSecurityToken(PiyaWebApplicationFactory.IntegrationAdminUsername);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("syncProtocolVersion").GetInt32().Should().Be(1);
        body.GetProperty("reportedStockOnly").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SeparationMigrationPreservesLegacyEvidenceAndOnlyCountsEligibleLots()
    {
        // Exercise the actual migration operations in a transaction-local synthetic
        // schema of the explicitly disposable database. Rollback removes this schema.
        var connectionString = PiyaWebApplicationFactory.ValidateTestConnection(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var db = new PharmacyApiDbContext(new DbContextOptionsBuilder<PharmacyApiDbContext>().UseNpgsql(connection).Options);
        await db.Database.UseTransactionAsync(transaction);
        var schema = "inventory_migration_test_" + Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA {schema}; SET LOCAL search_path TO {schema};");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "PharmacyInventories" ("Id" uuid PRIMARY KEY, "PharmacyId" uuid, "MedicationId" uuid, "QuantityInStock" integer, "IsAvailable" boolean);
            CREATE TABLE "InventoryBatches" ("Id" uuid PRIMARY KEY, "PharmacyInventoryId" uuid, "BatchNumber" text, "Quantity" integer, "IsActive" boolean, "ExpirationDate" timestamptz);
            INSERT INTO "PharmacyInventories" VALUES ('00000000-0000-0000-0000-000000000001',gen_random_uuid(),gen_random_uuid(),20,true);
            INSERT INTO "InventoryBatches" VALUES
              (gen_random_uuid(),'00000000-0000-0000-0000-000000000001','known',4,true,now()+interval '1 day'),
              (gen_random_uuid(),'00000000-0000-0000-0000-000000000001','unknown',7,true,NULL),
              (gen_random_uuid(),'00000000-0000-0000-0000-000000000001','expired',9,true,now()-interval '1 day'),
              (gen_random_uuid(),'00000000-0000-0000-0000-000000000001','negative',-2,true,NULL);
            """);
        var migration = new SeparateReportedAndBatchStock();
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        await using var read = new NpgsqlCommand("SELECT \"LegacyQuantityInStock\", \"ReportedQuantity\", \"QuantityInStock\", \"RequiresReconciliation\" FROM \"PharmacyInventories\"", connection, transaction);
        await using (var reader = await read.ExecuteReaderAsync())
        {
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetInt32(0).Should().Be(20);
            reader.GetInt32(1).Should().Be(20);
            reader.GetInt32(2).Should().Be(4);
            reader.GetBoolean(3).Should().BeTrue();
        }
        await using var legacy = new NpgsqlCommand("SELECT \"LegacyQuantity\" FROM \"InventoryBatches\" WHERE \"BatchNumber\" = 'negative'", connection, transaction);
        (await legacy.ExecuteScalarAsync()).Should().Be(-2);
        await transaction.RollbackAsync();
    }
}
