using Microsoft.AspNetCore.SignalR;

namespace PIYA_API.Hubs;

/// <summary>
/// SignalR hub for real-time pharmacy list updates.
/// All clients subscribe to the shared "pharmacies" group.
/// The server broadcasts PharmacyCreated / PharmacyUpdated / PharmacyDeleted events.
/// </summary>
public class PharmacyHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "pharmacies");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "pharmacies");
        await base.OnDisconnectedAsync(exception);
    }
}
