using Microsoft.AspNetCore.SignalR;

namespace MarketDataBroadcaster.Hubs;

public class MarketHub : Hub
{
    // User passes in their subscribe
    public async Task Subscribe(string accountId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, accountId);
    }
}