using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class InventoryService(
    PharmacyApiDbContext context,
    IAuditService auditService,
    ILogger<InventoryService> logger) : IInventoryService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly ILogger<InventoryService> _logger = logger;

    #region Core Inventory Management

    public async Task<PharmacyInventory> AddOrUpdateInventoryAsync(PharmacyInventory inventory)
    {
        var existing = await _context.PharmacyInventories
            .FirstOrDefaultAsync(i => 
                i.PharmacyId == inventory.PharmacyId && 
                i.MedicationId == inventory.MedicationId);

        if (existing != null)
        {
            // Update existing inventory
            existing.QuantityInStock = inventory.QuantityInStock;
            existing.MinimumStockLevel = inventory.MinimumStockLevel;
            existing.ReorderQuantity = inventory.ReorderQuantity;
            existing.Price = inventory.Price;
            existing.Currency = inventory.Currency;
            existing.IsAvailable = inventory.IsAvailable;
            existing.LastRestockedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();
            await CheckAndTriggerLowStockAlertAsync(existing);
            return existing;
        }
        else
        {
            // Create new inventory record
            inventory.Id = Guid.NewGuid();
            inventory.LastRestockedAt = DateTime.UtcNow;
            inventory.UpdatedAt = DateTime.UtcNow;
            
            _context.PharmacyInventories.Add(inventory);
            await _context.SaveChangesAsync();

            await _auditService.LogEntityActionAsync(
                "INVENTORY_CREATED",
                "PharmacyInventory",
                inventory.Id.ToString(),
                null,
                $"Created inventory for medication {inventory.MedicationId} at pharmacy {inventory.PharmacyId}"
            );

            return inventory;
        }
    }

    public async Task<List<PharmacyInventory>> GetPharmacyInventoryAsync(Guid pharmacyId)
    {
        return await _context.PharmacyInventories
            .Include(i => i.Medication)
            .Include(i => i.Pharmacy)
            .Include(i => i.Batches.Where(b => b.IsActive))
            .Where(i => i.PharmacyId == pharmacyId)
            .OrderBy(i => i.Medication.BrandName)
            .ToListAsync();
    }

    public async Task<List<PharmacyInventory>> GetPharmaciesWithMedicationAsync(Guid medicationId, int minimumQuantity = 1)
    {
        return await _context.PharmacyInventories
            .Include(i => i.Medication)
            .Include(i => i.Pharmacy)
                .ThenInclude(p => p.Coordinates)
            .Where(i => i.MedicationId == medicationId)
            .Where(i => i.QuantityInStock >= minimumQuantity)
            .Where(i => i.IsAvailable)
            .ToListAsync();
    }

    public async Task<bool> IsInStockAsync(Guid pharmacyId, Guid medicationId, int requiredQuantity = 1)
    {
        var totalStock = await _context.PharmacyInventories
            .Where(i => i.PharmacyId == pharmacyId && i.MedicationId == medicationId)
            .Where(i => i.IsAvailable)
            .SumAsync(i => i.QuantityInStock);

        return totalStock >= requiredQuantity;
    }

    public async Task<PharmacyInventory?> GetByIdAsync(Guid id)
    {
        return await _context.PharmacyInventories
            .Include(i => i.Medication)
            .Include(i => i.Pharmacy)
            .Include(i => i.Batches.OrderBy(b => b.ExpirationDate))
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var inventory = await GetByIdAsync(id);
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

    public async Task<PharmacyInventory> UpdateStockAsync(
        Guid inventoryId, 
        int newQuantity, 
        Guid? userId = null,
        string? notes = null)
    {
        if (newQuantity < 0)
            throw new ArgumentException("Stock quantity cannot be negative", nameof(newQuantity));

        await using var tx = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);
        try
        {
            var inventory = await _context.PharmacyInventories.FindAsync(inventoryId)
                ?? throw new InvalidOperationException("Inventory item not found");

            int oldQuantity = inventory.QuantityInStock;
            int quantityChanged = newQuantity - oldQuantity;

            inventory.QuantityInStock = newQuantity;
            inventory.UpdatedAt = DateTime.UtcNow;

            if (newQuantity > oldQuantity)
            {
                inventory.LastRestockedAt = DateTime.UtcNow;
            }

            // Record history
            await RecordInventoryHistoryAsync(
                inventory.Id,
                null,
                InventoryTransactionType.Adjustment,
                quantityChanged,
                oldQuantity,
                newQuantity,
                userId,
                notes: notes ?? $"Manual stock adjustment from {oldQuantity} to {newQuantity}"
            );

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            await CheckAndTriggerLowStockAlertAsync(inventory);

            return inventory;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<PharmacyInventory> DecreaseStockAsync(
        Guid pharmacyId, 
        Guid medicationId, 
        int quantity,
        Guid? userId = null,
        Guid? prescriptionId = null,
        string? referenceNumber = null)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity must be positive", nameof(quantity));
        }

        if (_context.Database.CurrentTransaction != null)
        {
            return await DecreaseStockCoreAsync(
                pharmacyId,
                medicationId,
                quantity,
                userId,
                prescriptionId,
                referenceNumber);
        }

        await using var tx = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead);
        try
        {
            var inventory = await DecreaseStockCoreAsync(
                pharmacyId,
                medicationId,
                quantity,
                userId,
                prescriptionId,
                referenceNumber);

            await tx.CommitAsync();
            return inventory;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<PharmacyInventory> DecreaseStockCoreAsync(
        Guid pharmacyId,
        Guid medicationId,
        int quantity,
        Guid? userId,
        Guid? prescriptionId,
        string? referenceNumber)
    {
        var inventory = await _context.PharmacyInventories
            .Include(i => i.Batches.Where(b => b.IsActive))
            .FirstOrDefaultAsync(i => i.PharmacyId == pharmacyId && i.MedicationId == medicationId) ?? throw new InvalidOperationException("Inventory item not found");

        // FIFO - use oldest batches first
        var batches = inventory.Batches
            .Where(b => b.IsActive && b.Quantity > 0 && (!b.ExpirationDate.HasValue || b.ExpirationDate > DateTime.UtcNow))
            .OrderBy(b => b.ExpirationDate ?? DateTime.MaxValue)
            .ToList();

        int remainingToDecrease = quantity;
        int oldStock = inventory.QuantityInStock;

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

        inventory.QuantityInStock -= quantity;
        inventory.UpdatedAt = DateTime.UtcNow;

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

        await _context.SaveChangesAsync();
        await CheckAndTriggerLowStockAlertAsync(inventory);

        return inventory;
    }

    public async Task<PharmacyInventory> IncreaseStockAsync(
        Guid pharmacyId, 
        Guid medicationId, 
        int quantity,
        Guid? userId = null,
        string? referenceNumber = null)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Quantity must be positive", nameof(quantity));
        }

        var inventory = await _context.PharmacyInventories
            .FirstOrDefaultAsync(i => i.PharmacyId == pharmacyId && i.MedicationId == medicationId) ?? throw new InvalidOperationException("Inventory item not found. Use AddOrUpdateInventoryAsync to create new inventory.");
        int oldStock = inventory.QuantityInStock;
        inventory.QuantityInStock += quantity;
        inventory.LastRestockedAt = DateTime.UtcNow;
        inventory.UpdatedAt = DateTime.UtcNow;

        // Record history
        await RecordInventoryHistoryAsync(
            inventory.Id,
            null,
            InventoryTransactionType.Restock,
            quantity,
            oldStock,
            inventory.QuantityInStock,
            userId,
            referenceNumber: referenceNumber,
            notes: $"Stock increased by {quantity}"
        );

        await _context.SaveChangesAsync();
        await CheckAndTriggerLowStockAlertAsync(inventory);

        return inventory;
    }

    #endregion

    #region Batch Management

    public async Task<InventoryBatch> AddBatchAsync(InventoryBatch batch)
    {
        var inventory = await _context.PharmacyInventories.FindAsync(batch.PharmacyInventoryId) ?? throw new InvalidOperationException("Inventory item not found");
        batch.Id = Guid.NewGuid();
        batch.OriginalQuantity = batch.Quantity;
        batch.IsActive = true;
        batch.ReceivedAt = DateTime.UtcNow;
        batch.CreatedAt = DateTime.UtcNow;
        batch.UpdatedAt = DateTime.UtcNow;

        _context.InventoryBatches.Add(batch);

        // Update inventory stock
        int oldStock = inventory.QuantityInStock;
        inventory.QuantityInStock += batch.Quantity;
        inventory.LastRestockedAt = DateTime.UtcNow;
        inventory.UpdatedAt = DateTime.UtcNow;

        // Update nearest expiration date
        if (batch.ExpirationDate.HasValue)
        {
            if (!inventory.ExpirationDate.HasValue || batch.ExpirationDate < inventory.ExpirationDate)
            {
                inventory.ExpirationDate = batch.ExpirationDate;
            }
        }

        // Record history
        await RecordInventoryHistoryAsync(
            inventory.Id,
            batch.Id,
            InventoryTransactionType.Restock,
            batch.Quantity,
            oldStock,
            inventory.QuantityInStock,
            null,
            referenceNumber: batch.BatchNumber,
            notes: $"Added batch {batch.BatchNumber} with {batch.Quantity} units"
        );

        await _context.SaveChangesAsync();

        _logger.LogInformation("Added batch {BatchNumber} for inventory {InventoryId}", 
            batch.BatchNumber, inventory.Id);

        return batch;
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

    public async Task<bool> RemoveExpiredBatchesAsync(Guid? userId = null)
    {
        var expiredBatches = await _context.InventoryBatches
            .Include(b => b.PharmacyInventory)
            .Where(b => b.IsActive && b.ExpirationDate.HasValue && b.ExpirationDate < DateTime.UtcNow)
            .ToListAsync();

        if (!expiredBatches.Any())
        {
            return false;
        }

        foreach (var batch in expiredBatches)
        {
            int oldStock = batch.PharmacyInventory.QuantityInStock;
            
            // Remove quantity from inventory
            batch.PharmacyInventory.QuantityInStock -= batch.Quantity;
            batch.IsActive = false;
            batch.UpdatedAt = DateTime.UtcNow;

            // Record history
            await RecordInventoryHistoryAsync(
                batch.PharmacyInventoryId,
                batch.Id,
                InventoryTransactionType.Expired,
                -batch.Quantity,
                oldStock,
                batch.PharmacyInventory.QuantityInStock,
                userId,
                notes: $"Removed expired batch {batch.BatchNumber}"
            );

            _logger.LogWarning("Removed expired batch {BatchNumber} with {Quantity} units", 
                batch.BatchNumber, batch.Quantity);
        }

        await _context.SaveChangesAsync();
        return true;
    }

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

    public async Task<List<PharmacyInventory>> GetLowStockItemsAsync(Guid pharmacyId)
    {
        return await _context.PharmacyInventories
            .Include(i => i.Medication)
            .Where(i => i.PharmacyId == pharmacyId)
            .Where(i => i.QuantityInStock <= i.MinimumStockLevel)
            .OrderBy(i => i.QuantityInStock)
            .ToListAsync();
    }

    public async Task<List<PharmacyInventory>> GetExpiringItemsAsync(Guid pharmacyId, int daysThreshold = 30)
    {
        var thresholdDate = DateTime.UtcNow.AddDays(daysThreshold);

        return await _context.PharmacyInventories
            .Include(i => i.Medication)
            .Include(i => i.Batches.Where(b => b.IsActive))
            .Where(i => i.PharmacyId == pharmacyId)
            .Where(i => i.Batches.Any(b => b.IsActive && 
                                          b.ExpirationDate.HasValue && 
                                          b.ExpirationDate.Value <= thresholdDate &&
                                          b.ExpirationDate.Value > DateTime.UtcNow))
            .OrderBy(i => i.ExpirationDate)
            .ToListAsync();
    }

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
        var inventory = await _context.PharmacyInventories
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.PharmacyId == pharmacyId && i.MedicationId == medicationId);

        return inventory?.QuantityInStock ?? 0;
    }

    #endregion
}
