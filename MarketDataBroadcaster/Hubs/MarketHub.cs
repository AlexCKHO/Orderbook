using Microsoft.AspNetCore.SignalR;

namespace MarketDataBroadcaster.Hubs;

public class MarketHub : Hub
{
    // User passes in their subscribe
    // {"protocol":"json","version":1}
    // {"type":1,"target":"Subscribe","arguments":["your_account_id_here"]}
    public async Task Subscribe(string accountId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, accountId);
    }
}