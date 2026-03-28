using System.Collections.Concurrent;
using System.Diagnostics;
using Confluent.Kafka;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using MarketSimulator.Services;
using Orderbook;
using Microsoft.Extensions.Configuration;

namespace MarketSimulator;

class Program
{
    private const int NoOfOrderToTest = 13_000_000;
    private const int Repeat = 4;
    private const string Address = "http://127.0.0.1:50051";
    private const int Concurrency = 4;
    private const int BatchSize = 5000;
    private const bool UseHistorical = true;

    static async Task Main(string[] args)
    {
        Console.WriteLine($"⚡ [SETUP] Configuring gRPC Client...");

        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            EnableMultipleHttp2Connections = true
        };

        using var channel = GrpcChannel.ForAddress(Address, new GrpcChannelOptions
        {
            HttpHandler = handler,
            // Bumped to 32MB to be absolutely safe with real-world Binance string allocations
            MaxReceiveMessageSize = 32 * 1024 * 1024,
            MaxSendMessageSize = 32 * 1024 * 1024
        });

        var client = new MatchingEngine.MatchingEngineClient(channel);

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.local.json")
            .Build();

        Console.WriteLine($"⚡ [SETUP] Setting up Binance trade Data");

        var binPath = config["DataSettings:BinanceBinaryDataPath"];
        var jsonPath = config["DataSettings:BinanceJsonDataPath"];
        var engineCommands = new List<EngineCommand>();


        if (UseHistorical)
        {
            try
            {
                if (File.Exists(binPath))
                {
                    using var input = File.OpenRead(binPath);
                    while (input.Position < input.Length
                           && engineCommands.Count < NoOfOrderToTest
                          )
                    {
                        var command = EngineCommand.Parser.ParseDelimitedFrom(input);
                        if (command != null) engineCommands.Add(command);
                    }

                    Console.WriteLine($"Binary file exists, engineCommands count: {engineCommands.Count}");
                }
            }
            catch (InvalidProtocolBufferException ex)
            {
                Console.WriteLine($"Binary data is corrupted: {ex.Message}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Binary read failed, falling back to JSON: {ex.Message}");
            }
        }
        else
        {
            for (int i = 0; i < NoOfOrderToTest; i++)
            {
                // Use Random.Shared for both to ensure thread-safety and proper seeding
                if (Random.Shared.Next(0, 2) == 0)
                {
                    engineCommands.Add(GenerateRandomOrderRequest(i));
                }
                else
                {
                    // Use Random.Shared here as well
                    int cancelId = Random.Shared.Next(1000000, 1000000 + i);
                    engineCommands.Add(GenerateRandomCancelRequest(cancelId));
                }
            }
        }

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
        Console.WriteLine("1. HFT Streaming (Sequential unbatched over single gRPC stream)");
        Console.WriteLine("2. Batching (Multiplexed concurrent gRPC streams)");
        Console.Write("Selection [1 or 2]: ");
        var choice = Console.ReadLine();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        for (int i = 0; i < Repeat; i++)
        {
            if (choice == "1")
            {
                GC.Collect();
                await SendOrderSequentially(client, engineCommands.ToArray());
            }
            else
            {
                GC.Collect();
                await SendOrdersByBatch(client, engineCommands.ToArray(), BatchSize);
            }
        }
    }

    private static async Task SendOrderSequentially(MatchingEngine.MatchingEngineClient client,
        EngineCommand[] commands)
    {
        Console.WriteLine($"🚀 Launching Sequential Market Replay over gRPC...");

        var sw = Stopwatch.StartNew();
        long successCount = 0;

        using var call = client.PlaceBatchStream();

        // Queue to track timestamps (FIFO - First-In-First-Out)
        var timestamps = new ConcurrentQueue<long>();

        // Pre-allocate the list to avoid ANY memory resizing during the benchmark
        var latencies = new List<double>(commands.Length);

        var readTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (response.Success)
                    {
                        // Match the response to the oldest unacknowledged request
                        if (timestamps.TryDequeue(out long startTicks))
                        {
                            double elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
                            latencies.Add(
                                elapsedMs); // Safe to use standard List here because only ONE thread is reading
                        }

                        Interlocked.Add(ref successCount, (long)response.QueuedCount);
                    }
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"\n❌ Reader broke: {ex.Status.Detail}");
            }
        });

        try
        {
            foreach (var command in commands)
            {
                var batch = new EngineBatchCommand();
                batch.Commands.Add(command);

                // Record the exact tick before pushing to the network
                timestamps.Enqueue(Stopwatch.GetTimestamp());
                await call.RequestStream.WriteAsync(batch);
            }

            await call.RequestStream.CompleteAsync();
        }
        catch (RpcException ex)
        {
            Console.WriteLine($"\n❌ Writer broke: {ex.Status.Detail}");
        }

        await readTask;
        sw.Stop();

        // Calculate Percentiles
        var sorted = latencies.ToArray();
        Array.Sort(sorted);

        double p0 = sorted.Length > 0 ? sorted[0] : 0; // Min
        double p50 = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.50)] : 0; // Median
        double p99 = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.99)] : 0;
        double p999 = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.999)] : 0;
        double p100 = sorted.Length > 0 ? sorted[^1] : 0; // Max

        Console.WriteLine($"\n========================================");
        Console.WriteLine($"🏁 SEQUENTIAL REPLAY RESULT");
        Console.WriteLine($"========================================");
        Console.WriteLine($"⚡ TPS: {successCount / sw.Elapsed.TotalSeconds:N0} messages/sec");
        Console.WriteLine($"✅ Delivered via gRPC: {successCount:N0}");
        Console.WriteLine($"========================================");
        Console.WriteLine($"⏱️ Latency Min (p0):   {p0:F3} ms");
        Console.WriteLine($"⏱️ Latency Median(p50):{p50:F3} ms");
        Console.WriteLine($"⏱️ Latency p99:        {p99:F3} ms");
        Console.WriteLine($"⏱️ Latency p99.9:      {p999:F3} ms");
        Console.WriteLine($"⏱️ Latency Max (p100): {p100:F3} ms");
        Console.WriteLine($"========================================");
    }

    private static async Task SendOrdersByBatch(MatchingEngine.MatchingEngineClient client, EngineCommand[] commands,
        int batchSize = 5_000)
    {
        Console.WriteLine($"\n🚀 [BENCHMARK] Starting Batching (Size: {batchSize}, Streams: {Concurrency})...");

        var sw = Stopwatch.StartNew();
        long successCount = 0;
        var tasks = new List<Task>();

        // Thread-safe bag to collect all batch round-trip latencies
        var latencies = new ConcurrentBag<double>();

        int totalCommands = commands.Length;
        int commandsPerTask = totalCommands / Concurrency;

        for (int i = 0; i < Concurrency; i++)
        {
            int taskIndex = i;
            int localStart = taskIndex * commandsPerTask;
            int localEnd = (taskIndex == Concurrency - 1) ? totalCommands : localStart + commandsPerTask;

            tasks.Add(Task.Run(async () =>
            {
                using var call = client.PlaceBatchStream();

                // Queue to track timestamps for this specific stream (FIFO)
                var batchTimestamps = new ConcurrentQueue<long>();

                var readTask = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var response in call.ResponseStream.ReadAllAsync())
                        {
                            if (response.Success)
                            {
                                // Dequeue the timestamp of the oldest unacknowledged batch
                                if (batchTimestamps.TryDequeue(out long startTicks))
                                {
                                    double elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 /
                                                       Stopwatch.Frequency;
                                    latencies.Add(elapsedMs);
                                }

                                Interlocked.Add(ref successCount, (long)response.QueuedCount);
                            }
                        }
                    }
                    catch (RpcException ex)
                    {
                        Console.WriteLine($"\n❌ [STREAM {taskIndex}] Reader broke: {ex.Status.Detail}");
                    }
                });

                var batch = new EngineBatchCommand();
                batch.Commands.Capacity = batchSize;

                try
                {
                    for (int j = localStart; j < localEnd; j++)
                    {
                        batch.Commands.Add(commands[j]);

                        if (batch.Commands.Count >= batchSize)
                        {
                            // Record timestamp exactly before sending
                            batchTimestamps.Enqueue(Stopwatch.GetTimestamp());
                            await call.RequestStream.WriteAsync(batch);

                            batch = new EngineBatchCommand();
                            batch.Commands.Capacity = batchSize;
                        }
                    }

                    if (batch.Commands.Count > 0)
                    {
                        batchTimestamps.Enqueue(Stopwatch.GetTimestamp());
                        await call.RequestStream.WriteAsync(batch);
                    }

                    await call.RequestStream.CompleteAsync();
                }
                catch (RpcException ex)
                {
                    Console.WriteLine($"\n❌ [STREAM {taskIndex}] Writer broke: {ex.Status.Detail}");
                }

                await readTask;
            }));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        // Calculate Percentiles
        var sorted = latencies.ToArray();
        Array.Sort(sorted);

        double p0 = sorted.Length > 0 ? sorted[0] : 0; // Min
        double p50 = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.50)] : 0; // Median
        double p99 = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.99)] : 0;
        double p999 = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.999)] : 0;
        double p100 = sorted.Length > 0 ? sorted[^1] : 0; // Max

        Console.WriteLine($"\n========================================");
        Console.WriteLine($"🏁 BATCH REPLAY RESULT (50M READY)");
        Console.WriteLine($"========================================");
        Console.WriteLine($"⚡ TPS: {successCount / sw.Elapsed.TotalSeconds:N0} messages/sec");
        Console.WriteLine($"✅ Delivered: {successCount:N0}");
        Console.WriteLine($"========================================");
        Console.WriteLine($"⏱️ Latency Min (p0):   {p0:F3} ms");
        Console.WriteLine($"⏱️ Latency Median(p50):{p50:F3} ms");
        Console.WriteLine($"⏱️ Latency p99:        {p99:F3} ms");
        Console.WriteLine($"⏱️ Latency p99.9:      {p999:F3} ms");
        Console.WriteLine($"⏱️ Latency Max (p100): {p100:F3} ms");
        Console.WriteLine($"========================================");
    }

    private static EngineCommand GenerateRandomOrderRequest(int index)
    {
        return new EngineCommand
        {
            PlaceOrder = new OrderRequest
            {
                ClientId = (ulong)(1000000 + index),
                Price = (ulong)Random.Shared.Next(100, 201),
                Qty = (ulong)Random.Shared.Next(1, 100),
                Side = Random.Shared.Next(0, 2) == 0 ? Side.Bid : Side.Ask,
                OrderType = Random.Shared.Next(0, 2) == 0 ? OrderType.Limit : OrderType.Market,
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
                ClientId = (ulong)(1000000 + index)
            }
        };
    }
}