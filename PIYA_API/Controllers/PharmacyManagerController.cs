using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

/// <summary>
/// PharmacyManager-specific operations:
///   - Dashboard stats (GET /api/pharmacymanager/{pharmacyId}/dashboard)
///   - Pharmacy profile self-update (PATCH /api/pharmacymanager/{pharmacyId}/profile)
///   - Staff management (GET/POST/PUT/DELETE under /{pharmacyId}/staff)
///   - Inventory bulk sync — used by the desktop stock-sync app
///     (POST /api/pharmacymanager/{pharmacyId}/inventory/sync)
///   - Reorder suggestions (GET /api/pharmacymanager/{pharmacyId}/reorder-suggestions)
/// </summary>
[ApiController]
[Route("api/pharmacymanager")]
[Authorize(Roles = "PharmacyManager,Admin,SuperAdmin")]
public class PharmacyManagerController(
    IPharmacyService pharmacyService,
    IPharmacyStaffService staffService,
    IInventoryService inventoryService,
    IPrescriptionService prescriptionService,
    ILogger<PharmacyManagerController> logger) : ControllerBase
{
    private readonly IPharmacyService _pharmacyService = pharmacyService;
    private readonly IPharmacyStaffService _staffService = staffService;
    private readonly IInventoryService _inventoryService = inventoryService;
    private readonly IPrescriptionService _prescriptionService = prescriptionService;
    private readonly ILogger<PharmacyManagerController> _logger = logger;

    private Guid GetUserId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private async Task<bool> CanManagePharmacy(Guid pharmacyId)
    {
        if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")) return true;
        return await _staffService.IsManagerAtPharmacyAsync(pharmacyId, GetUserId());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/pharmacymanager/{pharmacyId}/dashboard
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns aggregated dashboard stats for the manager's pharmacy.
    /// </summary>
    [HttpGet("{pharmacyId:guid}/dashboard")]
    public async Task<IActionResult> GetDashboard(Guid pharmacyId)
    {
        if (!await CanManagePharmacy(pharmacyId)) return Forbid();
        try
        {
            // These services share the request-scoped PharmacyApiDbContext. EF Core
            // does not permit concurrent operations on one context, so execute the
            // independent reads sequentially instead of using Task.WhenAll.
            var inventory = await _inventoryService.GetPharmacyInventoryAsync(pharmacyId);
            var lowStock = await _inventoryService.GetLowStockItemsAsync(pharmacyId);
            var expiring = await _inventoryService.GetExpiringItemsAsync(pharmacyId, 30);
            var staff = await _staffService.GetPharmacyStaffAsync(pharmacyId, true);
            var reorder = await _inventoryService.GetReorderSuggestionsAsync(pharmacyId);
            var history = await _inventoryService.GetPharmacyStockHistoryAsync(
                pharmacyId,
                DateTime.UtcNow.AddDays(-7),
                null);
            var prescriptions = await _prescriptionService.GetByPharmacyAsync(pharmacyId);

            var pendingRx   = prescriptions.Count(p => p.Status == PrescriptionStatus.Active || p.Status == PrescriptionStatus.PartiallyFulfilled);
            var fulfilledRx = prescriptions.Count(p => p.Status == PrescriptionStatus.Fulfilled);

            return Ok(new ManagerDashboardDto
            {
                TotalInventoryItems  = inventory.Count,
                AvailableItems       = inventory.Count(i => i.IsAvailable),
                LowStockCount        = lowStock.Count,
                ExpiringCount        = expiring.Count,
                ActiveStaffCount     = staff.Count,
                PendingPrescriptions = pendingRx,
                FulfilledToday       = fulfilledRx,
                ReorderSuggestions   = reorder.Select(r => new ReorderSuggestionDto
                {
                    InventoryId    = r.Key,
                    MedicationId   = inventory.FirstOrDefault(i => i.Id == r.Key)?.MedicationId ?? Guid.Empty,
                    MedicationName = inventory.FirstOrDefault(i => i.Id == r.Key)?.Medication?.BrandName ?? "Unknown",
                    CurrentStock   = inventory.FirstOrDefault(i => i.Id == r.Key)?.QuantityInStock ?? 0,
                    MinimumLevel   = inventory.FirstOrDefault(i => i.Id == r.Key)?.MinimumStockLevel ?? 0,
                    SuggestedQty   = r.Value,
                }).ToList(),
                RecentActivity = history.Select(h => new StockActivityDto
                {
                    MedicationName = h.PharmacyInventory?.Medication?.BrandName ?? "Unknown",
                    ChangeType     = h.TransactionType.ToString(),
                    Quantity       = h.QuantityChanged,
                    OccurredAt     = h.CreatedAt,
                    Notes          = h.Notes,
                }).ToList(),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building manager dashboard for pharmacy {Id}", pharmacyId);
            return StatusCode(500, new { error = "Failed to load dashboard" });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PATCH /api/pharmacymanager/{pharmacyId}/profile
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Allows the manager to update the pharmacy's public profile fields.
    /// Admins can also call this.
    /// </summary>
    [HttpPatch("{pharmacyId:guid}/profile")]
    public async Task<IActionResult> UpdateProfile(Guid pharmacyId, [FromBody] ManagerPharmacyProfileDto dto)
    {
        if (!await CanManagePharmacy(pharmacyId)) return Forbid();
        try
        {
            var pharmacy = await _pharmacyService.GetById(pharmacyId);
            if (pharmacy == null) return NotFound(new { error = "Pharmacy not found" });

            if (dto.PhoneNumber    is not null) pharmacy.PhoneNumber    = dto.PhoneNumber;
            if (dto.Email          is not null) pharmacy.Email          = dto.Email;
            if (dto.Website        is not null) pharmacy.Website        = dto.Website;
            if (dto.OperatingHours is not null) pharmacy.OperatingHours = dto.OperatingHours;
            if (dto.EmergencyContact is not null) pharmacy.EmergencyContact = dto.EmergencyContact;
            if (dto.Is24Hours      is not null) pharmacy.Is24Hours      = dto.Is24Hours.Value;
            if (dto.Services       is not null) pharmacy.Services       = dto.Services;

            var updated = await _pharmacyService.Update(pharmacy);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating pharmacy profile {Id}", pharmacyId);
            return StatusCode(500, new { error = "Failed to update profile" });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Staff sub-resource
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// List all staff at this pharmacy (manager's own view).
    /// </summary>
    [HttpGet("{pharmacyId:guid}/staff")]
    public async Task<IActionResult> GetStaff(Guid pharmacyId, [FromQuery] bool activeOnly = false)
    {
        if (!await CanManagePharmacy(pharmacyId)) return Forbid();
        try
        {
            var staff = await _staffService.GetPharmacyStaffAsync(pharmacyId, activeOnly);
            return Ok(staff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing staff for pharmacy {Id}", pharmacyId);
            return StatusCode(500, new { error = "Failed to list staff" });
        }
    }

    /// <summary>
    /// Assign a new staff member to this pharmacy.
    /// </summary>
    [HttpPost("{pharmacyId:guid}/staff")]
    public async Task<IActionResult> AddStaff(Guid pharmacyId, [FromBody] ManagerAddStaffDto dto)
    {
        if (!await CanManagePharmacy(pharmacyId)) return Forbid();
        try
        {
            var callerId = GetUserId();
            var staff = await _staffService.AssignStaffAsync(pharmacyId, dto.UserId, dto.Role, callerId);
            return CreatedAtAction(null, new { pharmacyId, id = staff.Id }, staff);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding staff to pharmacy {Id}", pharmacyId);
            return StatusCode(500, new { error = "Failed to add staff" });
        }
    }

    /// <summary>
    /// Remove a staff member from this pharmacy.
    /// </summary>
    [HttpDelete("{pharmacyId:guid}/staff/{userId:guid}")]
    public async Task<IActionResult> RemoveStaff(Guid pharmacyId, Guid userId)
    {
        if (!await CanManagePharmacy(pharmacyId)) return Forbid();
        try
        {
            var removed = await _staffService.RemoveStaffAsync(pharmacyId, userId);
            return removed ? NoContent() : NotFound(new { error = "Staff member not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing staff from pharmacy {Id}", pharmacyId);
            return StatusCode(500, new { error = "Failed to remove staff" });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reorder suggestions
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns items that are below reorder threshold and suggested reorder quantities.
    /// </summary>
    [HttpGet("{pharmacyId:guid}/reorder-suggestions")]
    public async Task<IActionResult> GetReorderSuggestions(Guid pharmacyId)
    {
        if (!await CanManagePharmacy(pharmacyId)) return Forbid();
        try
        {
            var suggestions = await _inventoryService.GetReorderSuggestionsAsync(pharmacyId);
            var inventory   = await _inventoryService.GetPharmacyInventoryAsync(pharmacyId);
            var inventoryMap = inventory.ToDictionary(i => i.Id);

            var result = suggestions.Select(s =>
            {
                inventoryMap.TryGetValue(s.Key, out var item);
                return new ReorderSuggestionDto
                {
                    InventoryId    = s.Key,
                    MedicationId   = item?.MedicationId ?? Guid.Empty,
                    MedicationName = item?.Medication?.BrandName ?? "Unknown",
                    CurrentStock   = item?.QuantityInStock ?? 0,
                    MinimumLevel   = item?.MinimumStockLevel ?? 0,
                    SuggestedQty   = s.Value,
                };
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reorder suggestions for pharmacy {Id}", pharmacyId);
            return StatusCode(500, new { error = "Failed to get reorder suggestions" });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bulk inventory sync  (used by the desktop app)
    // POST /api/pharmacymanager/{pharmacyId}/inventory/sync
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Accepts a batch of stock-level updates from the desktop sync app.
    /// Each entry: { medicationId, quantityInStock, price?, minimumStockLevel? }
    /// Items that don't exist in the pharmacy's inventory are created.
    /// Items with quantity = -1 are skipped (use as sentinel for "no data").
    /// Returns a summary: { updated, created, skipped, errors }.
    /// </summary>
    [HttpPost("{pharmacyId:guid}/inventory/sync")]
    public async Task<IActionResult> SyncInventory(Guid pharmacyId, [FromBody] List<InventorySyncItem> items)
    {
        if (!await CanManagePharmacy(pharmacyId)) return Forbid();
        if (items == null || items.Count == 0)
            return BadRequest(new { error = "No items provided" });

        var callerId = GetUserId();
        int updated = 0, created = 0, skipped = 0;
        var errors = new List<string>();

        // Load the full inventory once — avoids an O(n) DB round-trip per item
        var existingInventory = (await _inventoryService.GetPharmacyInventoryAsync(pharmacyId))
            .ToDictionary(i => i.MedicationId);

        foreach (var item in items)
        {
            if (item.QuantityInStock < 0) { skipped++; continue; }

            try
            {
                existingInventory.TryGetValue(item.MedicationId, out var existing);

                if (existing is null)
                {
                    // Create new inventory entry
                    var newEntry = new PharmacyInventory
                    {
                        PharmacyId        = pharmacyId,
                        MedicationId      = item.MedicationId,
                        QuantityInStock   = item.QuantityInStock,
                        MinimumStockLevel = item.MinimumStockLevel ?? 10,
                        ReorderQuantity   = item.ReorderQuantity   ?? 50,
                        Price             = item.Price             ?? 0,
                        Currency          = item.Currency          ?? "AZN",
                        IsAvailable       = item.QuantityInStock   > 0,
                    };
                    await _inventoryService.AddOrUpdateInventoryAsync(newEntry);
                    created++;
                }
                else
                {
                    // Update stock level
                    await _inventoryService.UpdateStockAsync(existing.Id, item.QuantityInStock, callerId,
                        $"Desktop sync @ {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
                    if (item.Price.HasValue)
                    {
                        existing.Price = item.Price.Value;
                        await _inventoryService.AddOrUpdateInventoryAsync(existing);
                    }
                    updated++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{item.MedicationId}: {ex.Message}");
                _logger.LogWarning(ex, "Sync error for medication {MedId} in pharmacy {PharmId}",
                    item.MedicationId, pharmacyId);
            }
        }

        return Ok(new { updated, created, skipped, errors });
    }
}

// ─── DTOs ─────────────────────────────────────────────────────────────────────

public class ManagerDashboardDto
{
    public int TotalInventoryItems  { get; set; }
    public int AvailableItems       { get; set; }
    public int LowStockCount        { get; set; }
    public int ExpiringCount        { get; set; }
    public int ActiveStaffCount     { get; set; }
    public int PendingPrescriptions { get; set; }
    public int FulfilledToday       { get; set; }
    public List<ReorderSuggestionDto> ReorderSuggestions { get; set; } = [];
    public List<StockActivityDto>     RecentActivity     { get; set; } = [];
}

public class ReorderSuggestionDto
{
    public Guid   InventoryId    { get; set; }
    public Guid   MedicationId   { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public int    CurrentStock   { get; set; }
    public int    MinimumLevel   { get; set; }
    public int    SuggestedQty   { get; set; }
}

public class StockActivityDto
{
    public string   MedicationName { get; set; } = string.Empty;
    public string   ChangeType     { get; set; } = string.Empty;
    public int      Quantity       { get; set; }
    public DateTime OccurredAt     { get; set; }
    public string?  Notes          { get; set; }
}

public class ManagerPharmacyProfileDto
{
    public string?       PhoneNumber      { get; set; }
    public string?       Email            { get; set; }
    public string?       Website          { get; set; }
    public string?       OperatingHours   { get; set; }
    public string?       EmergencyContact { get; set; }
    public bool?         Is24Hours        { get; set; }
    public List<string>? Services         { get; set; }
}

public class ManagerAddStaffDto
{
    public Guid              UserId { get; set; }
    public PharmacyStaffRole Role   { get; set; } = PharmacyStaffRole.Staff;
}

public class InventorySyncItem
{
    public Guid    MedicationId      { get; set; }
    public int     QuantityInStock   { get; set; }
    public decimal? Price            { get; set; }
    public int?    MinimumStockLevel { get; set; }
    public int?    ReorderQuantity   { get; set; }
    public string? Currency          { get; set; }
}
