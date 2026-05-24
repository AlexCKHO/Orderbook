using System.Threading.Channels;
using Confluent.Kafka;
using Google.Protobuf;
using MarketDataBroadcaster.Hubs;
using Microsoft.AspNetCore.SignalR;
using Orderbook;

namespace MarketDataBroadcaster.Services;


/*
   NETWORK INTAKE                 IN-MEMORY PIPELINE (RAM DECOUPLING)                  NETWORK EGRESS
   ===================   ========================================================   ======================
   
   ┌──────────────┐      ┌────────────────┐      ┌───────────────────┐      ┌─────────────────┐      ┌────────────────┐      ┌──────────────────┐
   │ Kafka Broker │ ───► │ Ingestion Loop │ ───► │ _processingChannel│ ───► │ Processing Loop │ ───► │  SignalR Hub   │ ───► │ Connected Client │
   │ (Topic Log)  │      │ (Dedicated OS) │      │ (Bounded Buffer)  │      │ (Worker Thread) │      │ (Outbound I/O) │      │ (Browser/Mobile) │
   └──────────────┘      └────────────────┘      └───────────────────┘      └─────────────────┘      └────────────────┘      └──────────────────┘
      [Raw Bytes]            [Consume()]              [TryWrite()]               [TryRead()]             [SendAsync()]            [Live UI Render]
           │                      │                        │                          │                       │
           │ (TCP Wire)           │                        │                          │ (Valid Payload)       │ (WebSockets / WSS)
           └──────────────────────┘                        │                          └───────────────────────┘
                                                           │
                                                           │ (If Handoff Safe)                    (If Parse Fails)
                                                           ▼                                      ▼
                                                  ┌────────────────┐                     ┌────────────────┐
                                                  │  StoreOffset   │                     │   Async DLQ    │
                                                  │ (Memory Diary) │                     │ (Sidelined POI)│
                                                  └────────────────┘                     └────────────────┘
                                                           │
                                                           │ (Every 5s Clock)
                                                           ▼
                                                  [ AutoCommit Thread ]
                                                (Updates Kafka Broker State)

 */


public class IndividualEventConsumer : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IHubContext<MarketHub> _hubContext;
    private readonly ILogger<IndividualEventConsumer> _logger;
    private readonly Channel<ConsumeResult<Ignore, byte[]>> _processingChannel;

    public IndividualEventConsumer(IConfiguration config, IHubContext<MarketHub> hubContext, ILogger<IndividualEventConsumer> logger)
    {
        _config = config;
        _hubContext = hubContext;
        _logger = logger;
        
        // _processingChannel Set up
        // Bounded channel prevents memory explosion if SignalR gets backed up
        _processingChannel = Channel.CreateBounded<ConsumeResult<Ignore, byte[]>>(new BoundedChannelOptions(10000)
        {
            // Only the Kafka loop writes
            SingleWriter = true, 
            
            // Multiple workers can parse/broadcast
            SingleReader = false,
            
            // If channel is full, wait 
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start the background parsing processing loop
        var processorTask = Task.Run(() => StartProcessingLoopAsync(stoppingToken), stoppingToken);
        
        // Start the ingestion loop (blocks its own dedicated thread execution)
        var ingestionTask = Task.Run(() => StartIngestionLoop(stoppingToken), stoppingToken);

        await Task.WhenAll(processorTask, ingestionTask);
    }

    // PHASE 1: Ingestion Loop (Single Responsibility: Getting event from Kafka and passing it to _processingChannel) 
    private void StartIngestionLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _config["Kafka:Broker"],
            GroupId = _config["Kafka:GroupId"],
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true, 
            
            // Manually store offset ONLY after successfully handle raw byte to _processingChannel
            EnableAutoOffsetStore = false 
        };
        
        // Kafka consumer
        using var consumer = new ConsumerBuilder<Ignore, byte[]>(config).Build();
        consumer.Subscribe(_config["Kafka:IndTopic"]);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Fetch raw bytes off the Kafka
                var result = consumer.Consume(stoppingToken);
                
                // Pass the raw bytes to _processingChannel
                while (!_processingChannel.Writer.TryWrite(result))
                {
                    // Bounded channel is full. Apply a short back-off
                    // waiting for capacity to free up.  
                    Thread.Sleep(1); 
                }

                // Updating Last Committed Offset in RAM
                consumer.StoreOffset(result);
            }
        }
        catch (OperationCanceledException)
        {
            consumer.Close();
        }
    }

    // PHASE 2: Dedicated Processing & Distribution Workers
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
                    // Parsing raw bytes to MatchEvent object
                    var matchEvent = MatchEvent.Parser.ParseFrom(rawData);
                    await BroadcastEventAsync(matchEvent);
                }
                catch (InvalidProtocolBufferException bufferException)
                {
                    _logger.LogError(bufferException, "Poison pill at offset {Offset}.", result.TopicPartitionOffset);
                    
                    // Fire-and-forget 
                    // or TODO: hand off to an async DLQ pipeline so it doesn't block this worker
                    _ = EnqueueToDeadLetterQueueAsync(result.Message.Key, rawData);
                }
            }
        }
    }

    private async Task BroadcastEventAsync(MatchEvent matchEvent)
    {

                    switch (matchEvent.EventDataCase)
                    {
                        case MatchEvent.EventDataOneofCase.Placed:
                            var placed = matchEvent.Placed;
                            var accountId = _orderIdDecompose(placed.ClientOrderId).accountId;
                            await _hubContext.Clients.Group(accountId.ToString())
                                .SendAsync("ReceivePlacedData", placed);
                            break;

                        case MatchEvent.EventDataOneofCase.Filled:
                            var filled = matchEvent.Filled;
                            var filledClientAccountId = _orderIdDecompose(filled.MakerClientOrderId).accountId;
                            var filledEngineAccountId = _orderIdDecompose(filled.MakerEngineOrderId).accountId;
                            await _hubContext.Clients.Group(filledClientAccountId.ToString())
                                .SendAsync("ReceiveFilledData", filled);
                            await _hubContext.Clients.Group(filledEngineAccountId.ToString())
                                .SendAsync("ReceiveFilledData", filled);
                            break;

                        case MatchEvent.EventDataOneofCase.Cancelled:
                            var cancelled = matchEvent.Cancelled;
                            var cancelledAccountId = _orderIdDecompose(cancelled.ClientOrderId).accountId;
                            await _hubContext.Clients.Group(cancelledAccountId.ToString())
                                .SendAsync("ReceiveCancelledData", cancelled);
                            break;

                        case MatchEvent.EventDataOneofCase.Killed:
                            var killed = matchEvent.Killed;
                            var killedAccountId = _orderIdDecompose(killed.ClientOrderId).accountId;
                            await _hubContext.Clients.Group(killedAccountId.ToString())
                                .SendAsync("ReceiveKilledData", killed);
                            break;

                        case MatchEvent.EventDataOneofCase.Rejected:
                            var rejected = matchEvent.Rejected;
                            var rejectedAccountId = _orderIdDecompose(rejected.ClientOrderId).accountId;
                            await _hubContext.Clients.Group(rejectedAccountId.ToString())
                                .SendAsync("ReceiveRejectedData", rejected);
                            break;
                    }
    }

    private async Task EnqueueToDeadLetterQueueAsync(Ignore key, byte[] corruptData)
    {
        try
        {
            // TODO: Asynchronously publish to Kafka DLQ topic here
            // Ensure this producer client is distinct or optimized for non-blocking I/O
            await Task.CompletedTask; 
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to write to DLQ. Data risk active.");
        }
    }
    private static (uint accountId, uint sequence) _orderIdDecompose(ulong orderId)
    {
        uint sequence = (uint)(orderId & 0xFFFFFFFF);

        uint accountId = (uint)((orderId >> 32) & 0x7FFFFFFF);

        return (accountId, sequence);
    }
}