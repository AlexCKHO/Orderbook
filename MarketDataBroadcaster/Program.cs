using MarketDataBroadcaster.Hubs;
using MarketDataBroadcaster.Services;

namespace MarketDataBroadcaster;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        builder.Services.AddSignalR(options =>
        {

            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddHostedService<PublicEventConsumer>();
        builder.Services.AddHostedService<IndividualEventConsumer>();
        var app = builder.Build();
        app.MapHub<MarketHub>("/market-data");
        app.Run();
    }
}