using Confluent.Kafka;
using MarketDataBroadcaster.Hubs;
using Microsoft.AspNetCore.SignalR;
using Google.Protobuf;
using Orderbook;

namespace MarketDataBroadcaster.Services;

public class KafkaMarketDataConsumer : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IHubContext<MarketHub> _hubContext;
    private readonly ILogger<KafkaMarketDataConsumer> _logger;

    public KafkaMarketDataConsumer(IConfiguration config, IHubContext<MarketHub> hubContext,
        ILogger<KafkaMarketDataConsumer> logger)
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
        var topic = _config["Kafka:Topic"];
        var groupId = _config["Kafka:GroupId"];

        var config = new ConsumerConfig()
        {
            BootstrapServers = broker,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest
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
                    case MatchEvent.EventDataOneofCase.Filled:
                        var filled = matchEvent.Filled;
                        _logger.LogInformation($" Price: {filled.Price}, Qty: {filled.Qty}");
                        await _hubContext.Clients.All.SendAsync("ReceiveMarketData", filled);
                        break;

                    case MatchEvent.EventDataOneofCase.Traded:
                        var traded = matchEvent.Traded;
                        await _hubContext.Clients.All.SendAsync("ReceiveMarketData", traded);
                        break;


                }
            }
        }
        catch (OperationCanceledException)
        {
            consumer.Close();
        }
    }
}