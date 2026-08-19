using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Hubs;

/// <summary>
/// SignalR hub for real-time pharmacy inventory (medication stock) updates.
///
/// Clients join a per-pharmacy group named "inventory:{pharmacyId}" on connect
/// by calling the <c>JoinPharmacy</c> client method.
///
/// Server broadcasts:
///   - InventoryAdded    (PharmacyInventory)  — new item added / existing item upserted
///   - InventoryUpdated  (PharmacyInventory)  — stock level / availability changed
///   - InventoryDeleted  (string inventoryId) — item removed
/// </summary>
[Authorize(Roles = "Pharmacist,PharmacyManager,Admin,SuperAdmin")]
public class InventoryHub(IPharmacyStaffService pharmacyStaffService) : Hub
{
    private readonly IPharmacyStaffService _pharmacyStaffService = pharmacyStaffService;

    /// <summary>
    /// Client calls this after connecting to subscribe to a pharmacy's inventory feed.
    /// </summary>
    public async Task JoinPharmacy(string pharmacyId)
    {
        if (!Guid.TryParse(pharmacyId, out var parsedPharmacyId))
            throw new HubException("Invalid pharmacy identifier.");

        if (!Context.User!.IsInRole("Admin") && !Context.User.IsInRole("SuperAdmin"))
        {
            var userIdClaim = Context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId) ||
                !await _pharmacyStaffService.IsStaffAtPharmacyAsync(parsedPharmacyId, userId))
            {
                throw new HubException("You are not authorized to subscribe to this pharmacy.");
            }
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"inventory:{parsedPharmacyId:D}");
    }

    /// <summary>
    /// Client calls this to unsubscribe (e.g. navigating away).
    /// </summary>
    public async Task LeavePharmacy(string pharmacyId)
    {
        if (!Guid.TryParse(pharmacyId, out var parsedPharmacyId))
            throw new HubException("Invalid pharmacy identifier.");

        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            $"inventory:{parsedPharmacyId:D}");
    }
}
