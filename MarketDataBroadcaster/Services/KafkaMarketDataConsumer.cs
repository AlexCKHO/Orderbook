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

        return Task.Run(() => );
    }
}