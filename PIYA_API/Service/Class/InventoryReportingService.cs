using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Model;

namespace PIYA_API.Service.Class;

public partial class InventoryService
{
    public Task<InventoryBatch?> GetBatchByIdAsync(Guid batchId) =>
        _context.InventoryBatches.AsNoTracking().Include(b => b.PharmacyInventory).SingleOrDefaultAsync(b => b.Id == batchId);
    public Task<InventoryBatch> CorrectBatchAsync(Guid batchId, InventoryBatchCorrection correction, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(correction.Reason) || correction.Reason.Length > 1000 ||
            correction.BatchNumber?.Length > 100 || correction.Quantity < 0 ||
            correction.IsActive && (string.IsNullOrWhiteSpace(correction.BatchNumber) || !correction.ExpirationDate.HasValue) ||
            correction.ExpirationDate.HasValue && (correction.ExpirationDate.Value == default || correction.ExpirationDate.Value.Kind != DateTimeKind.Utc))
            throw new ArgumentException("Provide the actual batch number, known UTC expiry, non-negative count and correction reason.");
        return AtomicAsync(async () =>
        {
            var id = await _context.InventoryBatches.Where(b => b.Id == batchId).Select(b => (Guid?)b.PharmacyInventoryId)
                .SingleOrDefaultAsync() ?? throw new InvalidOperationException("Batch not found");
            var inventory = await LockInventoryAsync(id);
            if (inventory.Version != correction.ExpectedVersion) throw new InvalidOperationException("Inventory changed. Refresh before correcting the lot.");
            var batch = inventory.Batches.Single(b => b.Id == batchId);
            var number = correction.BatchNumber?.Trim() ?? "";
            if (number.Length > 0 && inventory.Batches.Any(b => b.Id != batchId && b.BatchNumber == number))
                throw new InvalidOperationException("Another lot already uses that batch number.");
            if (batch.ManufacturingDate >= correction.ExpirationDate)
                throw new ArgumentException("Expiry must follow the recorded manufacturing date.");
            InventoryStock.Refresh(inventory, DateTime.UtcNow);
            var beforeStock = inventory.QuantityInStock;
            var before = JsonSerializer.Serialize(new { batch.BatchNumber, batch.Quantity, batch.ExpirationDate, batch.IsActive });
            batch.BatchNumber = number;
            batch.Quantity = correction.Quantity;
            batch.ExpirationDate = correction.ExpirationDate;
            batch.IsActive = correction.IsActive && correction.Quantity > 0;
            batch.UpdatedAt = DateTime.UtcNow;
            InventoryStock.Refresh(inventory, DateTime.UtcNow);
            inventory.Version++;
            inventory.UpdatedAt = DateTime.UtcNow;
            await RecordInventoryHistoryAsync(id, batchId, InventoryTransactionType.Adjustment,
                inventory.QuantityInStock - beforeStock, beforeStock, inventory.QuantityInStock, userId,
                notes: $"Verified lot correction: {correction.Reason.Trim()}. Previous: {before}. Current: {JsonSerializer.Serialize(correction)}");
            await CheckAndTriggerLowStockAlertAsync(inventory);
            await _context.SaveChangesAsync();
            return batch;
        });
    }

    public Task<PharmacyInventory> AcknowledgeReconciliationAsync(Guid inventoryId, long expectedVersion, string reason, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            throw new ArgumentException("A reconciliation reason is required (maximum 1000 characters).");
        return AtomicAsync(async () =>
        {
            var inventory = await LockInventoryAsync(inventoryId);
            if (inventory.Version != expectedVersion) throw new InvalidOperationException("Inventory changed. Refresh before acknowledging.");
            if (await _context.PharmacyInventories.AnyAsync(i => i.Id != inventoryId &&
                i.PharmacyId == inventory.PharmacyId && i.MedicationId == inventory.MedicationId))
                throw new InvalidOperationException("Duplicate inventory records exist for this medication at this pharmacy. Resolve the duplicate records before acknowledging reconciliation.");
            InventoryStock.Refresh(inventory, DateTime.UtcNow);
            if (inventory.ReportedQuantity != inventory.QuantityInStock || inventory.Batches.Any(b => b.IsActive &&
                (string.IsNullOrWhiteSpace(b.BatchNumber) || !b.ExpirationDate.HasValue)))
                throw new InvalidOperationException("Correct or retire unverified lots and reconcile the reported count with eligible batches first.");
            inventory.RequiresReconciliation = false;
            inventory.Version++;
            inventory.UpdatedAt = DateTime.UtcNow;
            await RecordInventoryHistoryAsync(inventoryId, null, InventoryTransactionType.Adjustment, 0,
                inventory.QuantityInStock, inventory.QuantityInStock, userId, notes: "Reconciliation acknowledged: " + reason.Trim());
            await _context.SaveChangesAsync();
            return inventory;
        });
    }

    private async Task<T> AtomicAsync<T>(Func<Task<T>> action)
    {
        if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction != null)
            return await action();
        return await _context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            try
            {
                var result = await action();
                await tx.CommitAsync();
                return result;
            }
            catch
            {
                await tx.RollbackAsync();
                // Retrying a transaction must not reuse partially mutated tracked lots.
                _context.ChangeTracker.Clear();
                throw;
            }
        });
    }

    private async Task LockPharmacyAsync(Guid id)
    {
        if (_context.Database.IsRelational())
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Pharmacies\" WHERE \"Id\" = {id} FOR UPDATE");
    }

    private async Task<PharmacyInventory> LockInventoryAsync(Guid id)
    {
        // Every stock/report writer takes the same parent-row lock. This protects
        // the batch sum even when two independent contexts dispense simultaneously.
        if (_context.Database.IsRelational())
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PharmacyInventories\" WHERE \"Id\" = {id} FOR UPDATE");
        var inventory = await _context.PharmacyInventories.FindAsync(id)
            ?? throw new InvalidOperationException("Inventory item not found");
        if (_context.Database.IsRelational()) await _context.Entry(inventory).ReloadAsync();
        await _context.Entry(inventory).Collection(i => i.Batches).LoadAsync();
        if (_context.Database.IsRelational())
            foreach (var batch in inventory.Batches) await _context.Entry(batch).ReloadAsync();
        return inventory;
    }

    private async Task SetReportAsync(PharmacyInventory inventory, int quantity, Guid? source,
        DateTime capturedAt, Guid? userId, string notes)
    {
        var before = inventory.ReportedQuantity ?? 0;
        inventory.ReportedQuantity = quantity;
        inventory.ReportedAt = capturedAt;
        inventory.ReportedSourceId = source;
        inventory.UpdatedAt = DateTime.UtcNow;
        InventoryStock.Refresh(inventory, DateTime.UtcNow);
        inventory.RequiresReconciliation = inventory.RequiresReconciliation || quantity != inventory.QuantityInStock;
        inventory.Version++;
        // Report history is explicitly a different transaction type; it must not
        // be interpreted as a physical stock movement or a dispensing receipt.
        await RecordInventoryHistoryAsync(inventory.Id, null, InventoryTransactionType.Report,
            quantity - before, before, quantity, userId, notes: notes);
        await _context.SaveChangesAsync();
    }

    public Task<InventorySyncResult> SyncReportedStockAsync(Guid pharmacyId, IReadOnlyList<InventoryReportItem> items,
        Guid userId, InventorySyncMetadata? metadata = null)
    {
        if (items.Count is < 1 or > 1000 || items.Select(i => i.MedicationId).Distinct().Count() != items.Count ||
            items.Any(i => i.MedicationId == Guid.Empty || i.QuantityInStock < 0 || i.Price < 0 ||
                i.MinimumStockLevel < 0 || i.ReorderQuantity < 0 || i.Currency?.Length > 8))
            throw new ArgumentException("Provide 1–1000 distinct medications with non-negative quantities/prices and valid currency.");
        if (metadata != null && (metadata.RequestId == Guid.Empty || metadata.SourceId == Guid.Empty ||
            metadata.Revision < 1 || metadata.Revision > 9007199254740991 || metadata.CapturedAt.Kind != DateTimeKind.Utc))
            throw new ArgumentException("Sync identity, positive safe-integer revision and UTC capture time are required.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            UserId = userId, Metadata = metadata, Items = items.OrderBy(i => i.MedicationId)
        }))));
        return AtomicAsync(async () =>
        {
            await LockPharmacyAsync(pharmacyId);
            if (metadata != null)
            {
                var prior = await _context.Set<InventorySyncReceipt>().AsNoTracking()
                    .SingleOrDefaultAsync(r => r.PharmacyId == pharmacyId && r.RequestId == metadata.RequestId);
                if (prior != null)
                {
                    if (prior.UserId != userId || prior.PayloadHash != hash) throw new InvalidOperationException("Request ID was already used for a different caller or content.");
                    return JsonSerializer.Deserialize<InventorySyncResult>(prior.ResultJson)!;
                }
                if (metadata.CapturedAt > DateTime.UtcNow.AddMinutes(5))
                    throw new ArgumentException("Snapshot capture time is too far in the future. Correct the device clock.");
                var last = await _context.Set<InventorySyncSource>().AsNoTracking()
                    .SingleOrDefaultAsync(s => s.PharmacyId == pharmacyId && s.SourceId == metadata.SourceId);
                if (last != null && (last.UserId != userId || last.LastRevision >= metadata.Revision || last.CapturedAt > metadata.CapturedAt))
                    throw new InvalidOperationException("An equal or newer source snapshot has already been accepted.");
            }
            var medIds = items.Select(i => i.MedicationId).ToArray();
            if (await _context.Medications.CountAsync(m => medIds.Contains(m.Id)) != items.Count)
                throw new ArgumentException("One or more medications do not exist. No reports were applied.");
            var existing = await _context.PharmacyInventories.Where(i => i.PharmacyId == pharmacyId && medIds.Contains(i.MedicationId))
                .ToDictionaryAsync(i => i.MedicationId);
            var locked = new Dictionary<Guid, PharmacyInventory>();
            foreach (var inventory in existing.Values.OrderBy(i => i.Id))
            {
                var row = await LockInventoryAsync(inventory.Id);
                if (metadata != null && row.ReportedAt > metadata.CapturedAt)
                    throw new InvalidOperationException("A newer report or manual adjustment exists. Reconcile this snapshot before retrying.");
                locked.Add(row.MedicationId, row);
            }
            var created = 0;
            foreach (var item in items)
            {
                if (!locked.TryGetValue(item.MedicationId, out var row))
                {
                    row = new PharmacyInventory { Id = Guid.NewGuid(), PharmacyId = pharmacyId, MedicationId = item.MedicationId };
                    _context.PharmacyInventories.Add(row);
                    created++;
                }
                if (item.Price.HasValue) row.Price = item.Price.Value;
                if (item.MinimumStockLevel.HasValue) row.MinimumStockLevel = item.MinimumStockLevel.Value;
                if (item.ReorderQuantity.HasValue) row.ReorderQuantity = item.ReorderQuantity.Value;
                if (item.Currency != null) row.Currency = item.Currency;
                await SetReportAsync(row, item.QuantityInStock, metadata?.SourceId, metadata?.CapturedAt ?? DateTime.UtcNow,
                    userId, "Desktop reported snapshot; no dispensable batch movement");
            }
            var result = new InventorySyncResult(items.Count - created, created, 0, [], metadata == null ? 0 : 1);
            if (metadata != null)
            {
                var state = await _context.Set<InventorySyncSource>().FindAsync(pharmacyId, metadata.SourceId);
                if (state == null) _context.Set<InventorySyncSource>().Add(state = new() { PharmacyId = pharmacyId, SourceId = metadata.SourceId, UserId = userId });
                state.LastRevision = metadata.Revision;
                state.CapturedAt = metadata.CapturedAt;
                _context.Set<InventorySyncReceipt>().Add(new() {
                    PharmacyId = pharmacyId, RequestId = metadata.RequestId, UserId = userId, PayloadHash = hash,
                    ResultJson = JsonSerializer.Serialize(result), CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
            return result;
        });
    }
}
