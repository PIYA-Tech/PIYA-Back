using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PIYA_API.Hubs;

/// <summary>
/// SignalR hub for real-time notifications
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (!string.IsNullOrEmpty(userId))
        {
            // Add user to their personal group for targeted notifications
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            Console.WriteLine($"User {userId} connected to NotificationHub with connection {Context.ConnectionId}");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
            Console.WriteLine($"User {userId} disconnected from NotificationHub");
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join a specific notification group.
    /// Callers may only join their own user group or a role-group that matches their claim.
    /// This prevents arbitrary group membership escalation.
    /// </summary>
    public async Task JoinGroup(string groupName)
    {
        var userId = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var role    = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;

        // Allow joining the caller's own user group unconditionally
        var ownGroup = $"user_{userId}";

        // Role-named groups: only allow if the caller actually holds that role
        var allowedRoleGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { role, role.ToLowerInvariant() };

        bool isAllowed = string.Equals(groupName, ownGroup, StringComparison.Ordinal)
                         || allowedRoleGroups.Contains(groupName);

        if (!isAllowed)
        {
            await Clients.Caller.SendAsync("Error", $"Not authorised to join group '{groupName}'");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        await Clients.Caller.SendAsync("JoinedGroup", groupName);
    }

    /// <summary>
    /// Leave a notification group
    /// </summary>
    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        await Clients.Caller.SendAsync("LeftGroup", groupName);
    }

    /// <summary>
    /// Send a message to all connected clients (Admin only)
    /// </summary>
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Admin,SuperAdmin")]
    public async Task BroadcastMessage(string message)
    {
        await Clients.All.SendAsync("ReceiveBroadcast", message, DateTime.UtcNow);
    }
}
