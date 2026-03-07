using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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
[Authorize]
public class InventoryHub : Hub
{
    /// <summary>
    /// Client calls this after connecting to subscribe to a pharmacy's inventory feed.
    /// </summary>
    public async Task JoinPharmacy(string pharmacyId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"inventory:{pharmacyId}");
    }

    /// <summary>
    /// Client calls this to unsubscribe (e.g. navigating away).
    /// </summary>
    public async Task LeavePharmacy(string pharmacyId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"inventory:{pharmacyId}");
    }
}
