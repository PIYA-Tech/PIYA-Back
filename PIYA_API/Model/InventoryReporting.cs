namespace PIYA_API.Model;

public sealed class InventorySyncSource
{
    public Guid PharmacyId { get; set; }
    public Guid SourceId { get; set; }
    public Guid UserId { get; set; }
    public long LastRevision { get; set; }
    public DateTime CapturedAt { get; set; }
}

public sealed class InventorySyncReceipt
{
    public Guid PharmacyId { get; set; }
    public Guid RequestId { get; set; }
    public Guid UserId { get; set; }
    public required string PayloadHash { get; set; }
    public required string ResultJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed record InventoryReportItem(Guid MedicationId, int QuantityInStock, decimal? Price = null,
    int? MinimumStockLevel = null, int? ReorderQuantity = null, string? Currency = null);
public sealed record InventorySyncMetadata(Guid RequestId, Guid SourceId, long Revision, DateTime CapturedAt);
public sealed record InventorySyncResult(int Updated, int Created, int Skipped, string[] Errors, int SyncProtocolVersion);
public sealed record InventoryBatchCorrection(long ExpectedVersion, string? BatchNumber, int Quantity, DateTime? ExpirationDate,
    bool IsActive, string Reason);
public sealed record InventoryReconciliationRequest(long ExpectedVersion, string Reason);

/// <summary>Shared stock rules. Unknown expiry and unnamed lots never prove dispensability.</summary>
public static class InventoryStock
{
    public static bool IsEligible(InventoryBatch batch, DateTime now) =>
        batch.IsActive && batch.Quantity > 0 && !string.IsNullOrWhiteSpace(batch.BatchNumber) &&
        batch.ExpirationDate.HasValue && batch.ExpirationDate.Value > now;

    public static void Refresh(PharmacyInventory inventory, DateTime now)
    {
        var batches = inventory.Batches.Where(b => IsEligible(b, now)).ToList();
        inventory.QuantityInStock = inventory.IsAvailable ? batches.Sum(b => b.Quantity) : 0;
        inventory.ExpirationDate = batches.Select(b => b.ExpirationDate).Min();
    }
}
