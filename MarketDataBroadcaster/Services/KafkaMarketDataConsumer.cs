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

                try
                {
                    var tradeData = PublicTrade.Parser.ParseFrom(rawData);

                    _logger.LogInformation(
                        $"[Trade] ID:{tradeData.TradeId}, Price:{tradeData.Price}, Qty:{tradeData.Qty}, Side:{tradeData.TakerSide}");

                    await _hubContext.Clients.All.SendAsync("ReceiveMarketData", tradeData, stoppingToken);
                }
                catch (InvalidProtocolBufferException ex)
                {
                    _logger.LogError($"無法解碼 Protobuf 數據: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            consumer.Close();
        }
    }
}