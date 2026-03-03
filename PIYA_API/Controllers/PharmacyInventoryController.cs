using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PharmacyInventoryController(
    IInventoryService inventoryService,
    IPharmacyStaffService pharmacyStaffService,
    ILogger<PharmacyInventoryController> logger) : ControllerBase
{
    private readonly IInventoryService _inventoryService = inventoryService;
    private readonly IPharmacyStaffService _pharmacyStaffService = pharmacyStaffService;
    private readonly ILogger<PharmacyInventoryController> _logger = logger;

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// Returns true when the caller is an Admin OR is active staff at the given pharmacy.
    /// </summary>
    private async Task<bool> CanAccessPharmacy(Guid pharmacyId) =>
        User.IsInRole("Admin") || await _pharmacyStaffService.IsStaffAtPharmacyAsync(pharmacyId, GetUserId());

    #region Inventory Management

    /// <summary>
    /// Get all inventory for a pharmacy
    /// </summary>
    [HttpGet("pharmacy/{pharmacyId}")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<List<PharmacyInventory>>> GetPharmacyInventory(Guid pharmacyId)
    {
        if (!await CanAccessPharmacy(pharmacyId))
            return Forbid();
        try
        {
            var inventory = await _inventoryService.GetPharmacyInventoryAsync(pharmacyId);
            return Ok(inventory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving inventory for pharmacy {PharmacyId}", pharmacyId);
            return StatusCode(500, new { error = "Failed to retrieve inventory" });
        }
    }

    /// <summary>
    /// Get inventory item by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<PharmacyInventory>> GetInventoryItem(Guid id)
    {
        try
        {
            var inventory = await _inventoryService.GetByIdAsync(id);
            if (inventory == null)
            {
                return NotFound(new { error = "Inventory item not found" });
            }
            return Ok(inventory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving inventory item {InventoryId}", id);
            return StatusCode(500, new { error = "Failed to retrieve inventory item" });
        }
    }

    /// <summary>
    /// Add or update inventory item
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<PharmacyInventory>> AddOrUpdateInventory(
        [FromBody] PharmacyInventoryRequest request)
    {
        if (!await CanAccessPharmacy(request.PharmacyId))
            return Forbid();
        try
        {
            var inventory = new PharmacyInventory
            {
                PharmacyId = request.PharmacyId,
                MedicationId = request.MedicationId,
                QuantityInStock = request.QuantityInStock,
                MinimumStockLevel = request.MinimumStockLevel,
                ReorderQuantity = request.ReorderQuantity,
                Price = request.Price,
                Currency = request.Currency,
                IsAvailable = request.IsAvailable
            };

            var result = await _inventoryService.AddOrUpdateInventoryAsync(inventory);
            return CreatedAtAction(nameof(GetInventoryItem), new { id = result.Id }, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding/updating inventory");
            return StatusCode(500, new { error = "Failed to add/update inventory" });
        }
    }

    /// <summary>
    /// Delete inventory item
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult> DeleteInventory(Guid id)
    {
        try
        {
            var deleted = await _inventoryService.DeleteAsync(id);
            if (!deleted)
            {
                return NotFound(new { error = "Inventory item not found" });
            }
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting inventory item {InventoryId}", id);
            return StatusCode(500, new { error = "Failed to delete inventory item" });
        }
    }

    #endregion

    #region Stock Updates

    /// <summary>
    /// Update stock quantity
    /// </summary>
    [HttpPut("{id}/stock")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<PharmacyInventory>> UpdateStock(
        Guid id,
        [FromBody] UpdateStockRequest request)
    {
        try
        {
            var userId = GetUserId();
            var result = await _inventoryService.UpdateStockAsync(id, request.Quantity, userId, request.Notes);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating stock for inventory {InventoryId}", id);
            return StatusCode(500, new { error = "Failed to update stock" });
        }
    }

    /// <summary>
    /// Increase stock (restock)
    /// </summary>
    [HttpPost("restock")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<PharmacyInventory>> Restock([FromBody] RestockRequest request)
    {
        if (!await CanAccessPharmacy(request.PharmacyId))
            return Forbid();
        try
        {
            var userId = GetUserId();
            var result = await _inventoryService.IncreaseStockAsync(
                request.PharmacyId,
                request.MedicationId,
                request.Quantity,
                userId,
                request.ReferenceNumber);
            
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restocking inventory");
            return StatusCode(500, new { error = "Failed to restock" });
        }
    }

    /// <summary>
    /// Decrease stock (sale/fulfillment)
    /// </summary>
    [HttpPost("decrease")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<PharmacyInventory>> DecreaseStock([FromBody] DecreaseStockRequest request)
    {
        if (!await CanAccessPharmacy(request.PharmacyId))
            return Forbid();
        try
        {
            var userId = GetUserId();
            var result = await _inventoryService.DecreaseStockAsync(
                request.PharmacyId,
                request.MedicationId,
                request.Quantity,
                userId,
                request.PrescriptionId,
                request.ReferenceNumber);
            
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decreasing stock");
            return StatusCode(500, new { error = "Failed to decrease stock" });
        }
    }

    #endregion

    #region Batch Management

    /// <summary>
    /// Add new inventory batch
    /// </summary>
    [HttpPost("batch")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<InventoryBatch>> AddBatch([FromBody] BatchRequest request)
    {
        try
        {
            var batch = new InventoryBatch
            {
                PharmacyInventoryId = request.InventoryId,
                BatchNumber = request.BatchNumber,
                Quantity = request.Quantity,
                ExpirationDate = request.ExpirationDate,
                ManufacturingDate = request.ManufacturingDate,
                Supplier = request.Supplier,
                CostPerUnit = request.CostPerUnit,
                StorageLocation = request.StorageLocation,
                Notes = request.Notes
            };

            var result = await _inventoryService.AddBatchAsync(batch);
            return CreatedAtAction(nameof(GetBatches), new { inventoryId = result.PharmacyInventoryId }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding batch");
            return StatusCode(500, new { error = "Failed to add batch" });
        }
    }

    /// <summary>
    /// Get batches for inventory item
    /// </summary>
    [HttpGet("{inventoryId}/batches")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<List<InventoryBatch>>> GetBatches(
        Guid inventoryId,
        [FromQuery] bool activeOnly = true)
    {
        try
        {
            var batches = await _inventoryService.GetBatchesAsync(inventoryId, activeOnly);
            return Ok(batches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving batches for inventory {InventoryId}", inventoryId);
            return StatusCode(500, new { error = "Failed to retrieve batches" });
        }
    }

    /// <summary>
    /// Get expiring batches (system-wide or pharmacy-specific)
    /// </summary>
    [HttpGet("batches/expiring")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<List<InventoryBatch>>> GetExpiringBatches([FromQuery] int days = 30)
    {
        try
        {
            var batches = await _inventoryService.GetExpiringBatchesAsync(days);
            return Ok(batches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving expiring batches");
            return StatusCode(500, new { error = "Failed to retrieve expiring batches" });
        }
    }

    /// <summary>
    /// Remove expired batches
    /// </summary>
    [HttpPost("batches/remove-expired")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult> RemoveExpiredBatches()
    {
        try
        {
            var userId = GetUserId();
            var removed = await _inventoryService.RemoveExpiredBatchesAsync(userId);
            
            return Ok(new { success = removed, message = removed ? "Expired batches removed" : "No expired batches found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing expired batches");
            return StatusCode(500, new { error = "Failed to remove expired batches" });
        }
    }

    #endregion

    #region Stock History

    /// <summary>
    /// Get stock history for inventory item
    /// </summary>
    [HttpGet("{inventoryId}/history")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<List<InventoryHistory>>> GetStockHistory(
        Guid inventoryId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] InventoryTransactionType? transactionType = null)
    {
        try
        {
            var history = await _inventoryService.GetStockHistoryAsync(inventoryId, startDate, endDate, transactionType);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving stock history for inventory {InventoryId}", inventoryId);
            return StatusCode(500, new { error = "Failed to retrieve stock history" });
        }
    }

    /// <summary>
    /// Get all stock history for pharmacy
    /// </summary>
    [HttpGet("pharmacy/{pharmacyId}/history")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<List<InventoryHistory>>> GetPharmacyStockHistory(
        Guid pharmacyId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        if (!await CanAccessPharmacy(pharmacyId))
            return Forbid();
        try
        {
            var history = await _inventoryService.GetPharmacyStockHistoryAsync(pharmacyId, startDate, endDate);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving stock history for pharmacy {PharmacyId}", pharmacyId);
            return StatusCode(500, new { error = "Failed to retrieve stock history" });
        }
    }

    #endregion

    #region Alerts & Reports

    /// <summary>
    /// Get low stock items for pharmacy
    /// </summary>
    [HttpGet("pharmacy/{pharmacyId}/low-stock")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<List<PharmacyInventory>>> GetLowStockItems(Guid pharmacyId)
    {
        if (!await CanAccessPharmacy(pharmacyId))
            return Forbid();
        try
        {
            var items = await _inventoryService.GetLowStockItemsAsync(pharmacyId);
            return Ok(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving low stock items for pharmacy {PharmacyId}", pharmacyId);
            return StatusCode(500, new { error = "Failed to retrieve low stock items" });
        }
    }

    /// <summary>
    /// Get expiring items for pharmacy
    /// </summary>
    [HttpGet("pharmacy/{pharmacyId}/expiring")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<List<PharmacyInventory>>> GetExpiringItems(
        Guid pharmacyId,
        [FromQuery] int days = 30)
    {
        if (!await CanAccessPharmacy(pharmacyId))
            return Forbid();
        try
        {
            var items = await _inventoryService.GetExpiringItemsAsync(pharmacyId, days);
            return Ok(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving expiring items for pharmacy {PharmacyId}", pharmacyId);
            return StatusCode(500, new { error = "Failed to retrieve expiring items" });
        }
    }

    /// <summary>
    /// Get reorder suggestions
    /// </summary>
    [HttpGet("pharmacy/{pharmacyId}/reorder-suggestions")]
    [Authorize(Roles = "Pharmacist,Admin")]
    public async Task<ActionResult<Dictionary<Guid, int>>> GetReorderSuggestions(Guid pharmacyId)
    {
        if (!await CanAccessPharmacy(pharmacyId))
            return Forbid();
        try
        {
            var suggestions = await _inventoryService.GetReorderSuggestionsAsync(pharmacyId);
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving reorder suggestions for pharmacy {PharmacyId}", pharmacyId);
            return StatusCode(500, new { error = "Failed to retrieve reorder suggestions" });
        }
    }

    #endregion

    #region Search & Availability

    /// <summary>
    /// Find pharmacies with medication in stock
    /// </summary>
    [HttpGet("medication/{medicationId}/pharmacies")]
    [AllowAnonymous]
    public async Task<ActionResult<List<PharmacyInventory>>> FindPharmaciesWithMedication(
        Guid medicationId,
        [FromQuery] int minimumQuantity = 1)
    {
        try
        {
            var pharmacies = await _inventoryService.GetPharmaciesWithMedicationAsync(medicationId, minimumQuantity);
            return Ok(pharmacies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding pharmacies with medication {MedicationId}", medicationId);
            return StatusCode(500, new { error = "Failed to find pharmacies" });
        }
    }

    /// <summary>
    /// Check if medication is in stock at pharmacy
    /// </summary>
    [HttpGet("check-stock")]
    public async Task<ActionResult<StockCheckResponse>> CheckStock(
        [FromQuery] Guid pharmacyId,
        [FromQuery] Guid medicationId,
        [FromQuery] int quantity = 1)
    {
        try
        {
            var inStock = await _inventoryService.IsInStockAsync(pharmacyId, medicationId, quantity);
            return Ok(new StockCheckResponse
            {
                InStock = inStock,
                PharmacyId = pharmacyId,
                MedicationId = medicationId,
                RequestedQuantity = quantity
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking stock");
            return StatusCode(500, new { error = "Failed to check stock" });
        }
    }

    #endregion
}

#region DTOs

public class PharmacyInventoryRequest
{
    public Guid PharmacyId { get; set; }
    public Guid MedicationId { get; set; }
    public int QuantityInStock { get; set; }
    public int MinimumStockLevel { get; set; } = 10;
    public int ReorderQuantity { get; set; } = 50;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "AZN";
    public bool IsAvailable { get; set; } = true;
}

public class UpdateStockRequest
{
    public int Quantity { get; set; }
    public string? Notes { get; set; }
}

public class RestockRequest
{
    public Guid PharmacyId { get; set; }
    public Guid MedicationId { get; set; }
    public int Quantity { get; set; }
    public string? ReferenceNumber { get; set; }
}

public class DecreaseStockRequest
{
    public Guid PharmacyId { get; set; }
    public Guid MedicationId { get; set; }
    public int Quantity { get; set; }
    public Guid? PrescriptionId { get; set; }
    public string? ReferenceNumber { get; set; }
}

public class BatchRequest
{
    public Guid InventoryId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public DateTime? ManufacturingDate { get; set; }
    public string? Supplier { get; set; }
    public decimal CostPerUnit { get; set; }
    public string? StorageLocation { get; set; }
    public string? Notes { get; set; }
}

public class StockCheckResponse
{
    public bool InStock { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid MedicationId { get; set; }
    public int RequestedQuantity { get; set; }
}

#endregion
