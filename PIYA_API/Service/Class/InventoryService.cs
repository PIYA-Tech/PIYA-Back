using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public partial class InventoryService(
    PharmacyApiDbContext context,
    IAuditService auditService,
    ILogger<InventoryService> logger) : IInventoryService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly ILogger<InventoryService> _logger = logger;

    #region Core Inventory Management

    public Task<PharmacyInventory> AddOrUpdateInventoryAsync(PharmacyInventory inventory)
    {
        var reported = inventory.ReportedQuantity ?? inventory.QuantityInStock;
        if (reported < 0 || inventory.Price < 0 || inventory.MinimumStockLevel < 0 || inventory.ReorderQuantity < 0)
            throw new ArgumentException("Inventory quantities and prices must be non-negative.");
        return AtomicAsync(async () =>
        {
            await LockPharmacyAsync(inventory.PharmacyId);
            var existing = await _context.PharmacyInventories.SingleOrDefaultAsync(i =>
                i.PharmacyId == inventory.PharmacyId && i.MedicationId == inventory.MedicationId);
            if (existing is null)
            {
                existing = new PharmacyInventory { Id = Guid.NewGuid(), PharmacyId = inventory.PharmacyId,
                    MedicationId = inventory.MedicationId };
                _context.PharmacyInventories.Add(existing);
            }
            else existing = await LockInventoryAsync(existing.Id);
            existing.MinimumStockLevel = inventory.MinimumStockLevel;
            existing.ReorderQuantity = inventory.ReorderQuantity;
            existing.Price = inventory.Price;
            existing.Currency = inventory.Currency;
            existing.IsAvailable = inventory.IsAvailable;
            await SetReportAsync(existing, reported, null, DateTime.UtcNow, null, "Reported stock updated; no batch movement");
            return existing;
        });
    }

    public async Task<List<PharmacyInventory>> GetPharmacyInventoryAsync(Guid pharmacyId)
    {
        var rows = await _context.PharmacyInventories.AsNoTracking().Include(i => i.Medication)
            .Include(i => i.Pharmacy).Include(i => i.Batches).Where(i => i.PharmacyId == pharmacyId)
            .OrderBy(i => i.Medication.BrandName).ToListAsync();
        foreach (var row in rows) InventoryStock.Refresh(row, DateTime.UtcNow);
        return rows;
    }

    public async Task<List<PharmacyInventory>> GetPharmaciesWithMedicationAsync(Guid medicationId, int minimumQuantity = 1)
    {
        if (minimumQuantity < 1) throw new ArgumentException("Minimum quantity must be positive.");
        var rows = await _context.PharmacyInventories.AsNoTracking().Include(i => i.Medication)
            .Include(i => i.Pharmacy).ThenInclude(p => p.Coordinates).Include(i => i.Batches)
            .Where(i => i.MedicationId == medicationId && i.IsAvailable && i.Pharmacy.IsActive && i.Medication.IsAvailable)
            .ToListAsync();
        foreach (var row in rows) InventoryStock.Refresh(row, DateTime.UtcNow);
        return rows.Where(i => i.QuantityInStock >= minimumQuantity).ToList();
    }

    public async Task<bool> IsInStockAsync(Guid pharmacyId, Guid medicationId, int requiredQuantity = 1)
    {
        if (requiredQuantity < 1) throw new ArgumentException("Required quantity must be positive.");
        return await GetAvailableStockAsync(pharmacyId, medicationId) >= requiredQuantity;
    }

    public async Task<PharmacyInventory?> GetByIdAsync(Guid id)
    {
        var row = await _context.PharmacyInventories.AsNoTracking().Include(i => i.Medication)
            .Include(i => i.Pharmacy).Include(i => i.Batches).FirstOrDefaultAsync(i => i.Id == id);
        if (row != null) InventoryStock.Refresh(row, DateTime.UtcNow);
        return row;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var inventory = await _context.PharmacyInventories.FindAsync(id);
        if (inventory == null)
        {
            return false;
        }

        _context.PharmacyInventories.Remove(inventory);
        await _context.SaveChangesAsync();

        await _auditService.LogEntityActionAsync(
            "INVENTORY_DELETED",
            "PharmacyInventory",
            id.ToString(),
            null,
            $"Deleted inventory item for medication {inventory.MedicationId}"
        );

        return true;
    }

    #endregion

    #region Real-time Stock Tracking

    public Task<PharmacyInventory> UpdateStockAsync(Guid inventoryId, int newQuantity,
        Guid? userId = null, string? notes = null)
    {
        if (newQuantity < 0) throw new ArgumentException("Reported quantity cannot be negative.");
        return AtomicAsync(async () =>
        {
            var inventory = await LockInventoryAsync(inventoryId);
            await SetReportAsync(inventory, newQuantity, null, DateTime.UtcNow, userId,
                notes ?? "Reported stock adjustment; no batch movement");
            return inventory;
        });
    }

    public Task<PharmacyInventory> DecreaseStockAsync(Guid pharmacyId, Guid medicationId, int quantity,
        Guid? userId = null, Guid? prescriptionId = null, string? referenceNumber = null)
    {
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive.");
        return AtomicAsync(() => DecreaseStockCoreAsync(pharmacyId, medicationId, quantity,
            userId, prescriptionId, referenceNumber));
    }

    private async Task<PharmacyInventory> DecreaseStockCoreAsync(
        Guid pharmacyId,
        Guid medicationId,
        int quantity,
        Guid? userId,
        Guid? prescriptionId,
        string? referenceNumber)
    {
        var inventoryId = await _context.PharmacyInventories
            .Where(i => i.PharmacyId == pharmacyId && i.MedicationId == medicationId)
            .Select(i => (Guid?)i.Id).SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("Inventory item not found");
        var inventory = await LockInventoryAsync(inventoryId);
        if (!inventory.IsAvailable) throw new InvalidOperationException("Inventory is unavailable for dispensing.");
        if (!await _context.Pharmacies.AnyAsync(p => p.Id == pharmacyId && p.IsActive) ||
            !await _context.Medications.AnyAsync(m => m.Id == medicationId && m.IsAvailable))
            throw new InvalidOperationException("The pharmacy or medication is unavailable for dispensing.");
        var now = DateTime.UtcNow;
        InventoryStock.Refresh(inventory, now);
        var batches = inventory.Batches.Where(b => InventoryStock.IsEligible(b, now))
            .OrderBy(b => b.ExpirationDate).ThenBy(b => b.Id).ToList();
        var oldStock = inventory.QuantityInStock;
        if (oldStock < quantity)
            throw new InvalidOperationException($"Insufficient eligible batch stock. Required: {quantity}, Available: {oldStock}");
        var remainingToDecrease = quantity;

        foreach (var batch in batches)
        {
            if (remainingToDecrease <= 0)
                break;

            int decreaseAmount = Math.Min(batch.Quantity, remainingToDecrease);
            batch.Quantity -= decreaseAmount;
            batch.UpdatedAt = DateTime.UtcNow;
            
            if (batch.Quantity == 0)
            {
                batch.IsActive = false;
            }

            remainingToDecrease -= decreaseAmount;
        }

        if (remainingToDecrease > 0)
        {
            throw new InvalidOperationException(
                $"Insufficient stock. Required: {quantity}, Available: {oldStock}");
        }

        InventoryStock.Refresh(inventory, now);
        inventory.UpdatedAt = now;
        inventory.Version++;

        // Record history
        await RecordInventoryHistoryAsync(
            inventory.Id,
            batches.FirstOrDefault()?.Id,
            prescriptionId.HasValue ? InventoryTransactionType.Sale : InventoryTransactionType.Adjustment,
            -quantity,
            oldStock,
            inventory.QuantityInStock,
            userId,
            prescriptionId,
            referenceNumber,
            $"Stock decreased by {quantity}"
        );

        await CheckAndTriggerLowStockAlertAsync(inventory);
        await _context.SaveChangesAsync();

        return inventory;
    }

    public Task<PharmacyInventory> IncreaseStockAsync(Guid pharmacyId, Guid medicationId, int quantity,
        Guid? userId = null, string? referenceNumber = null)
    {
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive.");
        return AtomicAsync(async () =>
        {
            var id = await _context.PharmacyInventories.Where(i => i.PharmacyId == pharmacyId && i.MedicationId == medicationId)
                .Select(i => (Guid?)i.Id).SingleOrDefaultAsync() ?? throw new InvalidOperationException("Inventory item not found");
            var inventory = await LockInventoryAsync(id);
            await SetReportAsync(inventory, checked((inventory.ReportedQuantity ?? 0) + quantity), null,
                DateTime.UtcNow, userId, "Reported restock; batch details are required for dispensing");
            return inventory;
        });
    }

    #endregion

    #region Batch Management

    public Task<InventoryBatch> AddBatchAsync(InventoryBatch batch) => AddBatchCoreAsync(batch, null);
    public Task<InventoryBatch> AddBatchAsync(InventoryBatch batch, Guid userId) => AddBatchCoreAsync(batch, userId);
    private Task<InventoryBatch> AddBatchCoreAsync(InventoryBatch batch, Guid? userId)
    {
        if (batch.Quantity <= 0 || string.IsNullOrWhiteSpace(batch.BatchNumber) || batch.BatchNumber.Length > 100 ||
            !batch.ExpirationDate.HasValue || batch.ExpirationDate <= DateTime.UtcNow || batch.CostPerUnit < 0 ||
            batch.ManufacturingDate > DateTime.UtcNow || batch.ManufacturingDate >= batch.ExpirationDate)
            throw new ArgumentException("A positive quantity, named batch, known future expiry and valid dates/cost are required.");
        return AtomicAsync(async () =>
        {
            var inventory = await LockInventoryAsync(batch.PharmacyInventoryId);
            batch.BatchNumber = batch.BatchNumber.Trim();
            if (inventory.Batches.Any(b => b.BatchNumber == batch.BatchNumber))
                throw new InvalidOperationException("This batch number already exists. Review the recorded lot before adding stock.");
            InventoryStock.Refresh(inventory, DateTime.UtcNow);
            var oldStock = inventory.QuantityInStock;
            batch.Id = Guid.NewGuid();
            batch.OriginalQuantity = batch.Quantity;
            batch.IsActive = true;
            batch.ReceivedAt = batch.CreatedAt = batch.UpdatedAt = DateTime.UtcNow;
            inventory.Batches.Add(batch);
            _context.InventoryBatches.Add(batch);
            InventoryStock.Refresh(inventory, DateTime.UtcNow);
            inventory.LastRestockedAt = inventory.UpdatedAt = DateTime.UtcNow;
            inventory.Version++;
            await RecordInventoryHistoryAsync(inventory.Id, batch.Id, InventoryTransactionType.Restock,
                batch.Quantity, oldStock, inventory.QuantityInStock, userId,
                referenceNumber: batch.BatchNumber, notes: "Recorded verified batch stock. " + batch.Notes);
            await CheckAndTriggerLowStockAlertAsync(inventory);
            await _context.SaveChangesAsync();
            return batch;
        });
    }

    public async Task<List<InventoryBatch>> GetBatchesAsync(Guid inventoryId, bool activeOnly = true)
    {
        var query = _context.InventoryBatches
            .Where(b => b.PharmacyInventoryId == inventoryId);

        if (activeOnly)
        {
            query = query.Where(b => b.IsActive);
        }

        return await query.OrderBy(b => b.ExpirationDate).ToListAsync();
    }

    public async Task<InventoryBatch?> GetBatchByNumberAsync(Guid inventoryId, string batchNumber)
    {
        return await _context.InventoryBatches
            .FirstOrDefaultAsync(b => b.PharmacyInventoryId == inventoryId && b.BatchNumber == batchNumber);
    }

    public async Task<List<InventoryBatch>> GetExpiringBatchesAsync(int daysThreshold = 30)
    {
        var thresholdDate = DateTime.UtcNow.AddDays(daysThreshold);

        return await _context.InventoryBatches
            .Include(b => b.PharmacyInventory)
                .ThenInclude(i => i.Medication)
            .Include(b => b.PharmacyInventory)
                .ThenInclude(i => i.Pharmacy)
            .Where(b => b.IsActive && b.Quantity > 0)
            .Where(b => b.ExpirationDate.HasValue && 
                       b.ExpirationDate.Value <= thresholdDate && 
                       b.ExpirationDate.Value > DateTime.UtcNow)
            .OrderBy(b => b.ExpirationDate)
            .ToListAsync();
    }

    public Task<bool> RemoveExpiredBatchesAsync(Guid? userId = null) => AtomicAsync(async () =>
    {
        var now = DateTime.UtcNow;
        var ids = await _context.InventoryBatches.Where(b => b.IsActive && b.ExpirationDate <= now)
            .Select(b => b.PharmacyInventoryId).Distinct().OrderBy(id => id).ToListAsync();
        foreach (var id in ids)
        {
            var inventory = await LockInventoryAsync(id);
            InventoryStock.Refresh(inventory, now);
            foreach (var batch in inventory.Batches.Where(b => b.IsActive && b.ExpirationDate <= now))
            {
                batch.IsActive = false;
                batch.UpdatedAt = now;
                await RecordInventoryHistoryAsync(id, batch.Id, InventoryTransactionType.Expired, -batch.Quantity,
                    inventory.QuantityInStock, inventory.QuantityInStock, userId,
                    notes: "Expired lot retired; already excluded from dispensable quantity");
            }
            inventory.Version++;
            inventory.UpdatedAt = now;
            await CheckAndTriggerLowStockAlertAsync(inventory);
        }
        await _context.SaveChangesAsync();
        return ids.Count > 0;
    });

    #endregion

    #region Stock History

    public async Task<List<InventoryHistory>> GetStockHistoryAsync(
        Guid inventoryId, 
        DateTime? startDate = null,
        DateTime? endDate = null,
        InventoryTransactionType? transactionType = null)
    {
        var query = _context.InventoryHistories
            .Include(h => h.PerformedByUser)
            .Include(h => h.Batch)
            .Where(h => h.PharmacyInventoryId == inventoryId);

        if (startDate.HasValue)
        {
            query = query.Where(h => h.TransactionDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(h => h.TransactionDate <= endDate.Value);
        }

        if (transactionType.HasValue)
        {
            query = query.Where(h => h.TransactionType == transactionType.Value);
        }

        return await query.OrderByDescending(h => h.TransactionDate).ToListAsync();
    }

    public async Task<List<InventoryHistory>> GetPharmacyStockHistoryAsync(
        Guid pharmacyId,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var query = _context.InventoryHistories
            .Include(h => h.PharmacyInventory)
                .ThenInclude(i => i.Medication)
            .Include(h => h.PerformedByUser)
            .Where(h => h.PharmacyInventory.PharmacyId == pharmacyId);

        if (startDate.HasValue)
        {
            query = query.Where(h => h.TransactionDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(h => h.TransactionDate <= endDate.Value);
        }

        return await query.OrderByDescending(h => h.TransactionDate).ToListAsync();
    }

    private Task RecordInventoryHistoryAsync(
        Guid inventoryId,
        Guid? batchId,
        InventoryTransactionType transactionType,
        int quantityChanged,
        int stockBefore,
        int stockAfter,
        Guid? userId,
        Guid? prescriptionId = null,
        string? referenceNumber = null,
        string? notes = null)
    {
        var history = new InventoryHistory
        {
            Id = Guid.NewGuid(),
            PharmacyInventoryId = inventoryId,
            BatchId = batchId,
            TransactionType = transactionType,
            QuantityChanged = quantityChanged,
            StockBefore = stockBefore,
            StockAfter = stockAfter,
            PerformedByUserId = userId,
            PrescriptionId = prescriptionId,
            ReferenceNumber = referenceNumber,
            Notes = notes,
            TransactionDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _context.InventoryHistories.Add(history);
        return Task.CompletedTask;
    }

    #endregion

    #region Low Stock Alerts

    public async Task<List<PharmacyInventory>> GetLowStockItemsAsync(Guid pharmacyId) =>
        (await GetPharmacyInventoryAsync(pharmacyId)).Where(i => i.QuantityInStock <= i.MinimumStockLevel)
            .OrderBy(i => i.QuantityInStock).ToList();

    public async Task<List<PharmacyInventory>> GetExpiringItemsAsync(Guid pharmacyId, int daysThreshold = 30) =>
        (await GetPharmacyInventoryAsync(pharmacyId)).Where(i => i.ExpirationDate <= DateTime.UtcNow.AddDays(daysThreshold))
            .OrderBy(i => i.ExpirationDate).ToList();

    public async Task<bool> CheckAndTriggerLowStockAlertAsync(PharmacyInventory inventory)
    {
        bool isLowStock = inventory.IsLowStock();
        bool shouldTriggerAlert = isLowStock && !inventory.LowStockAlertTriggered;

        if (shouldTriggerAlert)
        {
            inventory.LowStockAlertTriggered = true;
            inventory.LastLowStockAlertAt = DateTime.UtcNow;
            
            await _auditService.LogEntityActionAsync(
                "LOW_STOCK_ALERT",
                "PharmacyInventory",
                inventory.Id.ToString(),
                null,
                $"Low stock alert: {inventory.QuantityInStock} units remaining (min: {inventory.MinimumStockLevel})"
            );

            _logger.LogWarning(
                "Low stock alert for medication {MedicationId} at pharmacy {PharmacyId}. Stock: {Stock}, Min: {Min}",
                inventory.MedicationId, inventory.PharmacyId, inventory.QuantityInStock, inventory.MinimumStockLevel);

            return true;
        }
        else if (!isLowStock && inventory.LowStockAlertTriggered)
        {
            // Reset alert when stock is replenished
            inventory.LowStockAlertTriggered = false;
        }

        return false;
    }

    public async Task<Dictionary<Guid, int>> GetReorderSuggestionsAsync(Guid pharmacyId)
    {
        var lowStockItems = await GetLowStockItemsAsync(pharmacyId);
        
        return lowStockItems.ToDictionary(
            i => i.MedicationId,
            i => i.ReorderQuantity
        );
    }

    #endregion

    #region Prescription Fulfillment

    public async Task<(bool CanFulfill, List<Guid> MissingMedicationIds)> CanFulfillPrescriptionAsync(
        Guid pharmacyId, 
        List<Guid> medicationIds)
    {
        var missingMedicationIds = new List<Guid>();

        foreach (var medicationId in medicationIds)
        {
            bool inStock = await IsInStockAsync(pharmacyId, medicationId, 1);
            if (!inStock)
            {
                missingMedicationIds.Add(medicationId);
            }
        }

        return (missingMedicationIds.Count == 0, missingMedicationIds);
    }

    public async Task<int> GetAvailableStockAsync(Guid pharmacyId, Guid medicationId)
    {
        var now = DateTime.UtcNow;
        return await _context.InventoryBatches.Where(b => b.PharmacyInventory.PharmacyId == pharmacyId &&
            b.PharmacyInventory.MedicationId == medicationId && b.PharmacyInventory.IsAvailable &&
            b.PharmacyInventory.Pharmacy.IsActive && b.PharmacyInventory.Medication.IsAvailable &&
            b.IsActive && b.Quantity > 0 && b.BatchNumber.Trim() != "" && b.ExpirationDate > now)
            .SumAsync(b => b.Quantity);
    }

    #endregion
}
