using System.Collections.Concurrent;
using System.Diagnostics;
using Confluent.Kafka;
using Google.Protobuf;
using Grpc.Core;
using MarketSimulator.Services;
using Orderbook;
using Microsoft.Extensions.Configuration;

namespace MarketSimulator;

class Program
{
    // Memory constraint warning: 100M objects consume ~10-15GB RAM
    private const int TotalOrders = 10_000_000;
    private const string Address = "http://127.0.0.1:50051";
    private const int Concurrency = 4;
    private const int BatchSize = 5000;

    static async Task Main(string[] args)
    {
        // 🏆 Interview Essential: High-Frequency Trading (HFT) Kafka Producer Tuning
        var producerConfig = new ProducerConfig
        {
            // Points to the Redpanda/Kafka instance 
            BootstrapServers = "localhost:19092",

            // 1. Acks.Leader (The balance between Throughput and Durability)
            // Don't wait for all replicas to acknowledge. Once the Leader receives it, return "Success".
            // This significantly boosts TPS (Transactions Per Second).
            Acks = Acks.Leader,

            // 2. LingerMs (The "Micro-batching" Magic)
            // Instead of sending every single message immediately, wait for 2ms.
            // This allows the producer to group thousands of messages into a single batch,
            // drastically reducing the number of requests and overhead.
            LingerMs = 2,

            // 3. Compression (Optimizing Network Bandwidth)
            // Compresses data before sending. LZ4 is often preferred for its high speed and low CPU overhead.
            CompressionType = CompressionType.Lz4
        };

        using var producer = new ProducerBuilder<byte[], byte[]>(producerConfig).Build();

        Console.WriteLine($"⚡ [SETUP] Configuring Kafka Producer...");

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.local.json")
            .Build();

        string filePath = config["DataSettings:BinanceDataPath"];

        var jsonLinesStream = File.ReadLines(filePath);

        var parser = new BinanceDataParser();
        var engineCommands = parser.ParseLines(jsonLinesStream).ToArray();
        var listOfCommands = new List<byte[]>();
        foreach (var command in engineCommands)
        {
            listOfCommands.Add(command.ToByteArray());
        }


        Console.WriteLine($"✅ Loaded {engineCommands.Length:N0} commands into RAM ready for benchmark.");
        Console.WriteLine("\n💀 Select benchmark mode:");
        Console.WriteLine("1. HFT Streaming (Precision tracking via Request ID)");
        Console.WriteLine("2. Batching (FIFO Latency estimation)");
        Console.Write("Selection [1 or 2]: ");
        var choice = Console.ReadLine();


        // Trigger manual GC to sweep setup artifacts before starting the timer
        GC.Collect();
        GC.WaitForPendingFinalizers();

        if (choice == "1")
        {
            GC.Collect();
            await RunSequentialMarketReplay(producer, listOfCommands);
        }
        else
        {
            GC.Collect();
            // await RunBatchingTest(client, engineCommands, BatchSize);
        }
    }

    // ==========================================
    // MODE 1: HFT STREAMING
    // Tracks latency by matching RequestId in response
    // ==========================================

    private static async Task RunSequentialMarketReplay(IProducer<byte[], byte[]> producer, List<byte[]> commands)
    {
        Console.WriteLine($"🚀 Launching Sequential Market Replay (Strict FIFO)...");

        var sw = Stopwatch.StartNew();
        int successCount = 0;
        byte[] routingKey = System.Text.Encoding.UTF8.GetBytes("BTC_USDT");

        foreach (var command in commands)
        {
            var message = new Message<byte[], byte[]> { Key = routingKey, Value = command };

            producer.Produce("engine-commands-topic", message, deliveryReport =>
                {
                    if (deliveryReport.Error.IsFatal)
                    {
                        Console.WriteLine($"Delivery failed: {deliveryReport.Error.Reason}");
                    }
                    else
                    {
                        // ToAsk what is Interlocked?
                        Interlocked.Increment(ref successCount);
                    }
                }
            );
        }
        // ToAsk what is buffer? 
        producer.Flush(TimeSpan.FromSeconds(10));
        sw.Stop();

        Console.WriteLine($"\n========================================");
        Console.WriteLine($"🏁 SEQUENTIAL REPLAY RESULT");
        Console.WriteLine($"========================================");
        Console.WriteLine($"⚡ TPS: {successCount / sw.Elapsed.TotalSeconds:N0} messages/sec");
        Console.WriteLine($"✅ Delivered to Kafka: {successCount:N0}");
        Console.WriteLine($"========================================");
    }

    // ==========================================
    // MODE 2: BATCHING
    // Tracks latency using strict FIFO assumptions
    // ==========================================
    private static async Task RunSequentialMarketReplay(MatchingEngine.MatchingEngineClient client,
        EngineCommand[] orders,
        int batchSize = 5_000)
    {
        const int repeat = 5;

        Console.WriteLine($"\n🚀 [BENCHMARK] Starting Batching (Size: {batchSize}, Streams: {Concurrency})...");

        // Pre-compute all batches to keep allocation out of the benchmark hot loop
        var batches = orders.Chunk(batchSize).Select(chunk => new EngineBatchCommand()
        {
            Commands = { chunk }
        }).ToArray();


        int chunkSize = batches.Length / Concurrency;

        for (int k = 0; k < repeat; k++)
        {
            var latencies = new ConcurrentBag<double>();
            int totalProcessed = 0;

            // Partition batches across concurrent tasks

            var tasks = new List<Task>();

            var sw = Stopwatch.StartNew();


            for (int i = 0; i < Concurrency; i++)
            {
                int taskIndex = i;
                var batchChunk = batches.Skip(taskIndex * chunkSize).Take(chunkSize).ToArray();

                tasks.Add(Task.Run(async () =>
                {
                    using var call = client.PlaceBatchStream();

                    // Each stream maintains its own FIFO queue for precise latency tracking 
                    // avoiding global lock contention
                    var batchTimestamps = new ConcurrentQueue<long>();

                    // Background reader to process ACKs asynchronously
                    var reader = Task.Run(async () =>
                    {
                        await foreach (var resp in call.ResponseStream.ReadAllAsync())
                        {
                            // Map responses back to requests assuming strict FIFO ordering per stream
                            if (batchTimestamps.TryDequeue(out long startTicks) && startTicks > 0)
                            {
                                latencies.Add(GetElapsedMs(startTicks));
                            }

                            Interlocked.Add(ref totalProcessed, (int)resp.ProcessedCount);
                        }
                    });

                    // Hot path for sending batches
                    for (int j = 0; j < batchChunk.Length; j++)
                    {
                        // Sample every 100th batch per stream. (-1 implies ignored sample)
                        long timestampToQueue = (j % 100 == 0) ? Stopwatch.GetTimestamp() : -1;

                        // Enqueue timestamp *before* sending to prevent race conditions with the reader task
                        batchTimestamps.Enqueue(timestampToQueue);
                        await call.RequestStream.WriteAsync(batchChunk[j]);
                    }

                    await call.RequestStream.CompleteAsync();
                    await reader;
                }));
            }

            await Task.WhenAll(tasks);
            sw.Stop();

            PrintReport($"MULTI-BATCHING (x{Concurrency})", sw, totalProcessed, latencies);
        }
    }

    private static EngineCommand GenerateRandomOrderRequest(int index)
    {
        return new EngineCommand
        {
            PlaceOrder = new OrderRequest
            {
                Id = (ulong)(1000000 + index),
                Price = (ulong)Random.Shared.Next(100, 201),
                Qty = (ulong)Random.Shared.Next(1, 100),
                Side = Random.Shared.Next(0, 2) == 0 ? Side.Bid : Side.Ask,
                OrderType = OrderType.Limit,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }
        };
    }

    private static EngineCommand GenerateRandomCancelRequest(int index)
    {
        return new EngineCommand
        {
            CancelOrder = new CancelRequest()
            {
                Id = (ulong)(1000000 + index)
            }
        };
    }

    private static double GetElapsedMs(long startTicks)
    {
        return (double)(Stopwatch.GetTimestamp() - startTicks) * 1000 / Stopwatch.Frequency;
    }

    private static void PrintReport(string mode, Stopwatch sw, int count, ConcurrentBag<double> latencies)
    {
        // Filter out ignored samples (-1) and sort for percentile calculation
        var sorted = latencies.Where(x => x >= 0).OrderBy(x => x).ToArray();
        double p50 = 0, p99 = 0, max = 0;

        if (sorted.Length > 0)
        {
            p50 = sorted[sorted.Length / 2];
            p99 = sorted[(int)(sorted.Length * 0.99)];
            max = sorted.Last();
        }

        Console.WriteLine($"\n{new string('=', 40)}");
        Console.WriteLine($"🏁 {mode} RESULT");
        Console.WriteLine($"{new string('=', 40)}");
        Console.WriteLine($"⚡ TPS: {count / sw.Elapsed.TotalSeconds:N0} orders/sec");
        Console.WriteLine($"⏱️ Total Time: {sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"✅ Processed: {count:N0}");
        Console.WriteLine($"{new string('-', 40)}");
        Console.WriteLine($"📊 Latency (p50): {p50:F3} ms");
        Console.WriteLine($"📊 Latency (p99): {p99:F3} ms");
        Console.WriteLine($"📊 Latency (Max): {max:F3} ms");
        Console.WriteLine($"📦 Samples Taken: {sorted.Length:N0}");
        Console.WriteLine(new string('=', 40));
    }
}