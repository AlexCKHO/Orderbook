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

        Console.WriteLine($"⚡ [SETUP] Setting up Binance trade Data");

        var binPath = config["DataSettings:BinanceBinaryDataPath"];
        var jsonPath = config["DataSettings:BinanceJsonDataPath"];
        var engineCommands = new List<EngineCommand>();

        try
        {
            if (File.Exists(binPath))
            {
                using var input = File.OpenRead(binPath);
                while (input.Position < input.Length)
                {
                    var command = EngineCommand.Parser.ParseDelimitedFrom(input);
                    if (command != null) engineCommands.Add(command);
                }

                Console.WriteLine($"Binary file exists, engineCommands's count: {engineCommands.Count}");
            }
        }
        catch (InvalidProtocolBufferException ex)
        {
            // The file exists but the data inside is corrupted
            Console.WriteLine($"Binary data is corrupted: {ex.Message}");
        }
        catch (IOException ex)
        {
            // Log that the binary was locked or unreadable, then fall through to JSON
            Console.WriteLine($"Binary read failed, falling back to JSON: {ex.Message}");
        }


        // Fallback logic
        if (engineCommands.Count == 0 && File.Exists(jsonPath))
        {
            Console.WriteLine($"Binary file does not exist, parsing json file");
            try
            {
                var parser = new BinanceDataParser();
                engineCommands = parser.ParseLines(File.ReadLines(jsonPath)).ToList();

                using var output = File.Create(binPath);
                foreach (var command in engineCommands)
                {
                    command.WriteDelimitedTo(output);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical error processing JSON/Binary: {ex.Message}");
            }
        }

        Console.WriteLine($"✅ Loaded {engineCommands.Count:N0} commands into RAM ready for benchmark.");
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
            RunSequentialMarketReplay(producer, engineCommands.ToArray());
        }
        else
        {
            GC.Collect();
            await RunBatchMarketReplay(producer, engineCommands.ToArray(), BatchSize);
        }
    }

    // ==========================================
    // MODE 1: HFT STREAMING
    // Tracks latency by matching RequestId in response
    // ==========================================

    private static void RunSequentialMarketReplay(IProducer<byte[], byte[]> producer, EngineCommand[] commands)
    {
        Console.WriteLine($"🚀 Launching Sequential Market Replay (Strict FIFO)...");

        var sw = Stopwatch.StartNew();
        int successCount = 0;
        byte[] routingKey = System.Text.Encoding.UTF8.GetBytes("BTC_USDT");

        var listOfCommands = commands.Select(c => c.ToByteArray()).ToList();

        foreach (var command in listOfCommands)
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
        // Get 
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
    private static async Task RunBatchMarketReplay(IProducer<byte[], byte[]> producer,
        EngineCommand[] commands,
        int batchSize = 5_000)
    {
        Console.WriteLine($"\n🚀 [BENCHMARK] Starting Batching (Size: {batchSize}, Streams: {Concurrency})...");

        byte[][] byteBatches = commands
            .Chunk(batchSize)
            .Select(chunk =>
            {
                var batch = new EngineBatchCommand();
                batch.Commands.AddRange(chunk);
                return batch.ToByteArray();
            })
            .ToArray();

        var tasks = new List<Task>();


        var sw = Stopwatch.StartNew();
        int successCount = 0;

        byte[] routingKey = System.Text.Encoding.UTF8.GetBytes("BTC_USDT");

        int start = 0;

        var batchesPerTask = byteBatches.Length / Concurrency;

        for (int i = 0; i < Concurrency; i++)
        {
            int localStart = start;

            //int localEnd = i == Concurrency - 1 ? localStart + (batchesPerTask + byteBatches.Length % Concurrency) : localStart + batchesPerTask;

            int localEnd = localStart + batchesPerTask;

            if (i == Concurrency - 1)
            {
                localEnd = byteBatches.Length;
            }

            tasks.Add(Task.Run(() =>
                {
                    for (int j = localStart; j < localEnd; j++)
                    {
                        var message = new Message<byte[], byte[]> { Key = routingKey, Value = byteBatches[j] };

                        producer.Produce("engine-commands-topic", message, deliveryReport =>
                        {
                            if (deliveryReport.Error.IsFatal)
                            {
                                Console.WriteLine($"Delivery failed: {deliveryReport.Error.Reason}");
                            }
                            else
                            {
                                Interlocked.Increment(ref successCount);
                            }
                        });
                    }
                }
            ));

            start += batchesPerTask;
        }

        await Task.WhenAll(tasks);
        producer.Flush(TimeSpan.FromSeconds(10));

        sw.Stop();
        Console.WriteLine($"\n========================================");
        Console.WriteLine($"🏁 SEQUENTIAL REPLAY RESULT");
        Console.WriteLine($"========================================");
        Console.WriteLine($"⚡ TPS: {successCount / sw.Elapsed.TotalSeconds:N0} messages/sec");
        Console.WriteLine($"✅ Delivered to Kafka: {successCount:N0}");
        Console.WriteLine($"========================================");
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