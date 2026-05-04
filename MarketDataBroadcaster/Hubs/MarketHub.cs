using Microsoft.AspNetCore.SignalR;

namespace MarketDataBroadcaster.Hubs;

public class MarketHub : Hub
{
    public async Task Subscribe(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, symbol);

        Console.WriteLine($"Client {Context.ConnectionId} subscribed to {symbol}");
    }
}