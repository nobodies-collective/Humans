using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Humans.Web.Hubs;

[Authorize]
public class CampMapHub : Hub
{
    /// <summary>
    /// Called by clients to broadcast their cursor position.
    /// Relayed to all other connected clients.
    /// </summary>
    public async Task UpdateCursor(double lat, double lng)
    {
        var userName = Context.User?.Identity?.Name ?? "Unknown";
        await Clients.Others.SendAsync("CursorMoved", Context.ConnectionId, userName, lat, lng);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.Others.SendAsync("CursorLeft", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
