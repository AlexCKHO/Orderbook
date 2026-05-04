using Confluent.Kafka;

namespace MarketDataBroadcaster.Services;

public class KafkaMarketDataConsumer : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<KafkaMarketDataConsumer> _logger;

    public KafkaMarketDataConsumer(IConfiguration config, ILogger<KafkaMarketDataConsumer> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => StartConsumer(stoppingToken), stoppingToken);
    }

    private void StartConsumer(CancellationToken stoppingToken)
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
        
        using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        consumer.Subscribe(topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                _logger.LogInformation($"Received: {result.Message.Value}");

                
            }
        }
        catch (OperationCanceledException)
        {
            consumer.Close();
        }
    }
}