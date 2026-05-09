using System.Data;
using Confluent.Kafka;
using MarketDataBroadcaster.Hubs;
using Microsoft.AspNetCore.SignalR;
using Orderbook;

namespace MarketDataBroadcaster.Services;

public class IndividualEventConsumer : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IHubContext<MarketHub> _hubContext;
    private readonly ILogger<IndividualEventConsumer> _logger;

    public IndividualEventConsumer(IConfiguration config, IHubContext<MarketHub> hubContext,
        ILogger<IndividualEventConsumer> logger)
    {
        _hubContext = hubContext;
        _config = config;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => StartConsumer(stoppingToken), stoppingToken);
    }

    private async Task StartConsumer(CancellationToken stoppingToken)
    {
        // Configuring kafka channel 

        var broker = _config["Kafka:Broker"];
        var topic = _config["Kafka:IndTopic"];
        var groupId = _config["Kafka:GroupId"];

        var config = new ConsumerConfig()
        {
            BootstrapServers = broker,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Latest
        };

        using var consumer = new ConsumerBuilder<Ignore, byte[]>(config).Build();
        consumer.Subscribe(topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                byte[] rawData = result.Message.Value;

                var matchEvent = MatchEvent.Parser.ParseFrom(rawData);

                switch (matchEvent.EventDataCase)
                {
                    case MatchEvent.EventDataOneofCase.Placed:
                        var placed = matchEvent.Placed;
                        var accountId = _decompose(placed.ClientOrderId);
                        await _hubContext.Clients.Group(accountId.ToString()).SendAsync("ReceivePlacedData", placed);
                        break;

                    case MatchEvent.EventDataOneofCase.Filled:
                        var filled = matchEvent.Filled;
                        var filledClientAccountId = _decompose(filled.MakerClientOrderId);
                        var filledEngineAccountId = _decompose(filled.MakerEngineOrderId);
                        await _hubContext.Clients.Group(filledClientAccountId.ToString())
                            .SendAsync("ReceiveFilledData", filled);
                        await _hubContext.Clients.Group(filledEngineAccountId.ToString())
                            .SendAsync("ReceiveFilledData", filled);
                        break;

                    case MatchEvent.EventDataOneofCase.Cancelled:
                        var cancelled = matchEvent.Cancelled;
                        var cancelledAccountId = _decompose(cancelled.ClientOrderId);
                        await _hubContext.Clients.Group(cancelledAccountId.ToString())
                            .SendAsync("ReceiveCancelledData", cancelled);
                        break;

                    case MatchEvent.EventDataOneofCase.Killed:
                        var killed = matchEvent.Killed;
                        var killedAccountId = _decompose(killed.ClientOrderId);
                        await _hubContext.Clients.Group(killedAccountId.ToString())
                            .SendAsync("ReceiveKilledData", killed);
                        break;

                    case MatchEvent.EventDataOneofCase.Rejected:
                        var rejected = matchEvent.Rejected;
                        var rejectedAccountId = _decompose(rejected.ClientOrderId);
                        await _hubContext.Clients.Group(rejectedAccountId.ToString())
                            .SendAsync("ReceiveRejectedData", rejected);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            consumer.Close();
        }
    }

    private static (uint accountId, uint sequence) _decompose(ulong orderId)
    {

        uint sequence = (uint)(orderId & 0xFFFFFFFF);

        uint accountId = (uint)((orderId >> 32) & 0x7FFFFFFF);

        return (accountId, sequence);
    }
}