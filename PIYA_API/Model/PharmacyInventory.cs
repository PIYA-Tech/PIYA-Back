namespace PIYA_API.Model;

/// <summary>
/// Inventory transaction types
/// </summary>
public enum InventoryTransactionType
{
    Restock = 1,           // Adding new stock
    Sale = 2,              // Medication sold
    Adjustment = 3,        // Manual adjustment (correction)
    Return = 4,            // Customer return
    Expired = 5,           // Removed due to expiration
    Damaged = 6,           // Removed due to damage
    Transfer = 7,          // Transferred to another pharmacy
    Report = 8            // External/manual reported quantity; no physical movement
}

/// <summary>
/// Real-time pharmacy inventory tracking with batch management
/// </summary>
public class PharmacyInventory
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Pharmacy that has this medication
    /// </summary>
    public Guid PharmacyId { get; set; }
    public Pharmacy Pharmacy { get; set; } = null!;
    
    /// <summary>
    /// Medication in stock
    /// </summary>
    public Guid MedicationId { get; set; }
    public Medication Medication { get; set; } = null!;
    
    /// <summary>
    /// Cached eligible batch quantity. Reads recompute this because lots expire with time.
    /// </summary>
    public int QuantityInStock { get; set; }
    public int? ReportedQuantity { get; set; }
    public DateTime? ReportedAt { get; set; }
    public Guid? ReportedSourceId { get; set; }
    /// <summary>Original pre-separation total, retained verbatim for reconciliation.</summary>
    public int? LegacyQuantityInStock { get; set; }
    public bool RequiresReconciliation { get; set; }
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public long Version { get; set; }
    
    /// <summary>
    /// Minimum stock level (for low stock alerts)
    /// </summary>
    public int MinimumStockLevel { get; set; } = 10;
    
    /// <summary>
    /// Reorder quantity (how much to order when stock is low)
    /// </summary>
    public int ReorderQuantity { get; set; } = 50;
    
    /// <summary>
    /// Price per unit
    /// </summary>
    public decimal Price { get; set; }
    
    /// <summary>
    /// Currency (AZN, USD, etc.)
    /// </summary>
    public string Currency { get; set; } = "AZN";
    
    /// <summary>
    /// Batch/lot number (primary batch - for backward compatibility)
    /// </summary>
    public string? BatchNumber { get; set; }
    
    /// <summary>
    /// Expiration date (nearest expiry from all batches)
    /// </summary>
    public DateTime? ExpirationDate { get; set; }
    
    /// <summary>
    /// Whether this medication is currently available for sale
    /// </summary>
    public bool IsAvailable { get; set; } = true;
    
    /// <summary>
    /// Whether low stock alert has been triggered
    /// </summary>
    public bool LowStockAlertTriggered { get; set; } = false;
    
    /// <summary>
    /// Last time stock was updated
    /// </summary>
    public DateTime LastRestockedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last time low stock alert was sent
    /// </summary>
    public DateTime? LastLowStockAlertAt { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<InventoryBatch> Batches { get; set; } = [];
    public ICollection<InventoryHistory> History { get; set; } = [];
    
    /// <summary>
    /// Check if stock is low (below minimum level)
    /// </summary>
    public bool IsLowStock() => QuantityInStock <= MinimumStockLevel;
    
    /// <summary>
    /// Check if any batch is expiring soon (within days)
    /// </summary>
    public bool HasExpiringSoon(int withinDays = 30)
    {
        return Batches.Any(b => b.IsActive && 
                               b.ExpirationDate.HasValue && 
                               b.ExpirationDate.Value.AddDays(-withinDays) <= DateTime.UtcNow);
    }
}

/// <summary>
/// Individual batch/lot tracking for medications
/// </summary>
public class InventoryBatch
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Parent inventory record
    /// </summary>
    public Guid PharmacyInventoryId { get; set; }
    public PharmacyInventory PharmacyInventory { get; set; } = null!;
    
    /// <summary>
    /// Batch/lot number from manufacturer
    /// </summary>
    public string BatchNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Quantity in this specific batch
    /// </summary>
    public int Quantity { get; set; }
    /// <summary>Preserves invalid legacy counts when migration quarantines a lot.</summary>
    public int? LegacyQuantity { get; set; }
    
    /// <summary>
    /// Original quantity when batch was created
    /// </summary>
    public int OriginalQuantity { get; set; }
    
    /// <summary>
    /// Expiration date for this batch
    /// </summary>
    public DateTime? ExpirationDate { get; set; }
    
    /// <summary>
    /// Manufacturing date
    /// </summary>
    public DateTime? ManufacturingDate { get; set; }
    
    /// <summary>
    /// Supplier/manufacturer name
    /// </summary>
    public string? Supplier { get; set; }
    
    /// <summary>
    /// Cost per unit for this batch (purchase price)
    /// </summary>
    public decimal CostPerUnit { get; set; }
    
    /// <summary>
    /// Whether this batch is still active (not expired/depleted)
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Storage location in pharmacy (shelf, room, etc.)
    /// </summary>
    public string? StorageLocation { get; set; }
    
    /// <summary>
    /// Notes about this batch
    /// </summary>
    public string? Notes { get; set; }
    
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Check if batch is expired
    /// </summary>
    public bool IsExpired() => ExpirationDate.HasValue && ExpirationDate.Value < DateTime.UtcNow;
    
    /// <summary>
    /// Check if batch is expiring soon
    /// </summary>
    public bool IsExpiringSoon(int withinDays = 30)
    {
        return ExpirationDate.HasValue && 
               ExpirationDate.Value.AddDays(-withinDays) <= DateTime.UtcNow &&
               !IsExpired();
    }
}

/// <summary>
/// Stock movement history for audit trail and analytics
/// </summary>
public class InventoryHistory
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Inventory record this history belongs to
    /// </summary>
    public Guid PharmacyInventoryId { get; set; }
    public PharmacyInventory PharmacyInventory { get; set; } = null!;
    
    /// <summary>
    /// Batch involved in this transaction (if applicable)
    /// </summary>
    public Guid? BatchId { get; set; }
    public InventoryBatch? Batch { get; set; }
    
    /// <summary>
    /// Type of transaction
    /// </summary>
    public InventoryTransactionType TransactionType { get; set; }
    
    /// <summary>
    /// Quantity changed (positive for additions, negative for removals)
    /// </summary>
    public int QuantityChanged { get; set; }
    
    /// <summary>
    /// Stock level before transaction
    /// </summary>
    public int StockBefore { get; set; }
    
    /// <summary>
    /// Stock level after transaction
    /// </summary>
    public int StockAfter { get; set; }
    
    /// <summary>
    /// User who performed the transaction
    /// </summary>
    public Guid? PerformedByUserId { get; set; }
    public User? PerformedByUser { get; set; }
    
    /// <summary>
    /// Related prescription (if transaction was a sale)
    /// </summary>
    public Guid? PrescriptionId { get; set; }
    public Prescription? Prescription { get; set; }
    
    /// <summary>
    /// Reference number (invoice, order number, etc.)
    /// </summary>
    public string? ReferenceNumber { get; set; }
    
    /// <summary>
    /// Notes/reason for transaction
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Cost/price per unit at time of transaction
    /// </summary>
    public decimal? UnitPrice { get; set; }
    
    /// <summary>
    /// Total transaction value
    /// </summary>
    public decimal? TotalValue { get; set; }
    
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

