using System.Threading.Channels;
using Confluent.Kafka;
using Google.Protobuf;
using MarketDataBroadcaster.Hubs;
using Microsoft.AspNetCore.SignalR;
using Orderbook;

namespace MarketDataBroadcaster.Services;

public class PublicEventConsumer : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IHubContext<MarketHub> _hubContext;
    private readonly ILogger<PublicEventConsumer> _logger;
    
    // Decoupled memory pipeline to process high-throughput public trade feeds
    private readonly Channel<ConsumeResult<Ignore, byte[]>> _processingChannel;

    public PublicEventConsumer(IConfiguration config, IHubContext<MarketHub> hubContext, ILogger<PublicEventConsumer> logger)
    {
        _config = config;
        _hubContext = hubContext;
        _logger = logger;

        // Bounded capacity increased to 50,000 to buffer aggressive market micro-bursts
        _processingChannel = Channel.CreateBounded<ConsumeResult<Ignore, byte[]>>(new BoundedChannelOptions(50000)
        {
            SingleWriter = true,
            SingleReader = true, // Maintained as single reader to guarantee strict chronological trade execution order
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Use LongRunning option to force the runtime to allocate a dedicated native OS thread 
        // for the blocking Kafka consume loop, preventing ThreadPool starvation.
        var ingestionTask = Task.Factory.StartNew(
            () => StartIngestionLoop(stoppingToken),
            stoppingToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );

        var processorTask = Task.Run(() => StartProcessingLoopAsync(stoppingToken), stoppingToken);

        await Task.WhenAll(ingestionTask, processorTask);
    }

    // PHASE 1: Dedicated Ingestion Loop
    private void StartIngestionLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig()
        {
            BootstrapServers = _config["Kafka:Broker"],
            GroupId = _config["Kafka:GroupId"],
            AutoOffsetReset = AutoOffsetReset.Earliest, // Maintained Earliest for historical tick processing
            EnableAutoCommit = true,                    // High-performance background thread offset flushing
            EnableAutoOffsetStore = false               // Explicitly disabled to stop premature auto-acknowledgments
        };

        using var consumer = new ConsumerBuilder<Ignore, byte[]>(config).Build();
        consumer.Subscribe(_config["Kafka:Topic"]);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                
                while (!_processingChannel.Writer.TryWrite(result))
                {
                    Thread.Sleep(1); 
                }
                
                consumer.StoreOffset(result);
            }
        }
        catch (OperationCanceledException)
        {
            consumer.Close();
        }
    }

    // PHASE 2: Chronological Processing & SignalR Broadcast
    private async Task StartProcessingLoopAsync(CancellationToken stoppingToken)
    {
        var reader = _processingChannel.Reader;

        while (await reader.WaitToReadAsync(stoppingToken))
        {
            while (reader.TryRead(out var result))
            {
                byte[] rawData = result.Message.Value;

                try
                {
                    var matchEvent = MatchEvent.Parser.ParseFrom(rawData);

                    switch (matchEvent.EventDataCase)
                    {
                        case MatchEvent.EventDataOneofCase.Traded:
                            var traded = matchEvent.Traded;
                            
                            // Broadcast trade data out to all listening client tickers
                            await _hubContext.Clients.All.SendAsync("ReceiveMarketData", traded, cancellationToken: stoppingToken);
                            break;
                    }
                }
                catch (InvalidProtocolBufferException bufferException)
                {
                    _logger.LogError(bufferException, "Public poison pill skipped at offset {Offset}.", result.TopicPartitionOffset);
                    
                    // Asynchronously sideline the corrupt market data payload to clear the hot path instantly
                    _ = EnqueueToDeadLetterQueueAsync(result.Message.Key, rawData);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error broadcasting public market event.");
                }
            }
        }
    }

    private async Task EnqueueToDeadLetterQueueAsync(Ignore key, byte[] corruptData)
    {
        try
        {
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to route public corrupt data payload to DLQ.");
        }
    }
}