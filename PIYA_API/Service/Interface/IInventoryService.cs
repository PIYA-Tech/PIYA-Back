using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Enhanced service for managing pharmacy inventory with batch tracking, stock history, and alerts
/// </summary>
public interface IInventoryService
{
    #region Core Inventory Management
    
    /// <summary>
    /// Add or update inventory for a pharmacy
    /// </summary>
    Task<PharmacyInventory> AddOrUpdateInventoryAsync(PharmacyInventory inventory);
    
    /// <summary>
    /// Get inventory item by ID
    /// </summary>
    Task<PharmacyInventory?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Get all inventory for a pharmacy
    /// </summary>
    Task<List<PharmacyInventory>> GetPharmacyInventoryAsync(Guid pharmacyId);
    
    /// <summary>
    /// Get pharmacies that have a specific medication in stock
    /// </summary>
    Task<List<PharmacyInventory>> GetPharmaciesWithMedicationAsync(Guid medicationId, int minimumQuantity = 1);
    
    /// <summary>
    /// Check if pharmacy has medication in stock
    /// </summary>
    Task<bool> IsInStockAsync(Guid pharmacyId, Guid medicationId, int requiredQuantity = 1);
    
    /// <summary>
    /// Delete inventory item
    /// </summary>
    Task<bool> DeleteAsync(Guid id);
    
    #endregion
    
    #region Real-time Stock Tracking
    
    /// <summary>
    /// Update stock quantity with optional user tracking and notes
    /// </summary>
    Task<PharmacyInventory> UpdateStockAsync(Guid inventoryId, int newQuantity, Guid? userId = null, string? notes = null);
    
    /// <summary>
    /// Decrease stock (after sale/fulfillment) with FIFO batch management
    /// </summary>
    Task<PharmacyInventory> DecreaseStockAsync(Guid pharmacyId, Guid medicationId, int quantity, Guid? userId = null, Guid? prescriptionId = null, string? referenceNumber = null);
    
    /// <summary>
    /// Increase stock (restock) with tracking
    /// </summary>
    Task<PharmacyInventory> IncreaseStockAsync(Guid pharmacyId, Guid medicationId, int quantity, Guid? userId = null, string? referenceNumber = null);
    
    #endregion
    
    #region Batch Management
    
    /// <summary>
    /// Add a new inventory batch with batch number and expiration tracking
    /// </summary>
    Task<InventoryBatch> AddBatchAsync(InventoryBatch batch);
    
    /// <summary>
    /// Get all batches for an inventory item
    /// </summary>
    Task<List<InventoryBatch>> GetBatchesAsync(Guid inventoryId, bool activeOnly = true);
    
    /// <summary>
    /// Get batch by batch number
    /// </summary>
    Task<InventoryBatch?> GetBatchByNumberAsync(Guid inventoryId, string batchNumber);
    
    /// <summary>
    /// Get all batches expiring within specified days
    /// </summary>
    Task<List<InventoryBatch>> GetExpiringBatchesAsync(int daysThreshold = 30);
    
    /// <summary>
    /// Remove expired batches from inventory
    /// </summary>
    Task<bool> RemoveExpiredBatchesAsync(Guid? userId = null);
    
    #endregion
    
    #region Stock History
    
    /// <summary>
    /// Get stock movement history for an inventory item
    /// </summary>
    Task<List<InventoryHistory>> GetStockHistoryAsync(
        Guid inventoryId, 
        DateTime? startDate = null,
        DateTime? endDate = null,
        InventoryTransactionType? transactionType = null);
    
    /// <summary>
    /// Get all stock history for a pharmacy
    /// </summary>
    Task<List<InventoryHistory>> GetPharmacyStockHistoryAsync(
        Guid pharmacyId,
        DateTime? startDate = null,
        DateTime? endDate = null);
    
    #endregion
    
    #region Low Stock Alerts
    
    /// <summary>
    /// Get inventory items below minimum stock level
    /// </summary>
    Task<List<PharmacyInventory>> GetLowStockItemsAsync(Guid pharmacyId);
    
    /// <summary>
    /// Get inventory items with batches expiring soon
    /// </summary>
    Task<List<PharmacyInventory>> GetExpiringItemsAsync(Guid pharmacyId, int daysThreshold = 30);
    
    /// <summary>
    /// Check and trigger low stock alert if needed
    /// </summary>
    Task<bool> CheckAndTriggerLowStockAlertAsync(PharmacyInventory inventory);
    
    /// <summary>
    /// Get reorder suggestions based on low stock items
    /// </summary>
    Task<Dictionary<Guid, int>> GetReorderSuggestionsAsync(Guid pharmacyId);
    
    #endregion
    
    #region Prescription Fulfillment
    
    /// <summary>
    /// Check if pharmacy can fulfill prescription (has all medications)
    /// </summary>
    Task<(bool CanFulfill, List<Guid> MissingMedicationIds)> CanFulfillPrescriptionAsync(
        Guid pharmacyId, 
        List<Guid> medicationIds);

    /// <summary>
    /// Get the current available quantity for a specific medication at a pharmacy.
    /// Returns 0 if no inventory record exists.
    /// </summary>
    Task<int> GetAvailableStockAsync(Guid pharmacyId, Guid medicationId);
    
    #endregion
}
