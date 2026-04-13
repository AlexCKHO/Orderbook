using System.Collections.Concurrent;
using System.Diagnostics;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using MarketSimulator.Models;
using MarketSimulator.Pacing;
using MarketSimulator.Services;
using Microsoft.Extensions.Configuration;
using Orderbook;

namespace MarketSimulator;

/*
============================================================
SEQUENTIAL MODE PACER SELECTION
============================================================

UseHistorical -----------\
                          AND -----> historicalPacer -----> TimestampOrderPacer
EnableHistoricalPacing --/

!UseHistorical ----------\
                          AND ---\
EnableOrderPacing -------/       AND -----> randomPacer -----> OrderRatePacer
OrderRatePerSecond > 0 ----------/

Runtime choice:
historicalPacer != null  -----> use TimestampOrderPacer
else randomPacer != null -----> use OrderRatePacer
else ---------------------> no pacing

============================================================
BATCH MODE PACER SELECTION (CURRENT CODE)
============================================================

EnableHistoricalPacing ---------------------> TimestampOrderPacer

if not EnableHistoricalPacing:

EnableOrderPacing -------\
                          AND -----> OrderRatePacer
OrderRatePerSecond > 0 --/

otherwise -------------------------------> no pacing

*/

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("⚡ [SETUP] Configuring gRPC Client...");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var settings = configuration
                           .GetSection("MarketSimulatorSettings")
                           .Get<MarketSimulatorSettings>()
                       ?? throw new InvalidOperationException("MarketSimulatorSettings section is missing.");

        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            EnableMultipleHttp2Connections = true
        };

        using var channel = GrpcChannel.ForAddress(settings.gRPCAddress, new GrpcChannelOptions
        {
            HttpHandler = handler,
            MaxReceiveMessageSize = 32 * 1024 * 1024,
            MaxSendMessageSize = 32 * 1024 * 1024
        });

        var client = new MatchingEngine.MatchingEngineClient(channel);

        Console.WriteLine("⚡ [SETUP] Setting up Binance trade data...");

        var binPath = configuration["DataSettings:BinanceBinaryDataPath"];
        var jsonPath = configuration["DataSettings:BinanceJsonDataPath"];
        var engineCommands = new List<EngineCommand>();


        if (settings.UseHistorical)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(binPath) && File.Exists(binPath))
                {
                    using var input = File.OpenRead(binPath);

                    while (input.Position < input.Length && engineCommands.Count < settings.NoOfOrderToTest)
                    {
                        var command = EngineCommand.Parser.ParseDelimitedFrom(input);
                        if (command != null)
                            engineCommands.Add(command);
                    }

                    Console.WriteLine($"✅ Binary file exists, engineCommands count: {engineCommands.Count:N0}");
                }
            }
            catch (InvalidProtocolBufferException ex)
            {
                Console.WriteLine($"❌ Binary data is corrupted: {ex.Message}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"❌ Binary read failed, falling back to JSON: {ex.Message}");
            }
        }
        else
        {
            for (int i = 0; i < settings.NoOfOrderToTest; i++)
            {
                if (Random.Shared.Next(0, 2) == 0)
                {
                    engineCommands.Add(GenerateRandomOrderRequest(i));
                }
                else
                {
                    int cancelId = Random.Shared.Next(1_000_000, 1_000_000 + i);
                    engineCommands.Add(GenerateRandomCancelRequest(cancelId));
                }
            }
        }

        if (engineCommands.Count == 0 && !string.IsNullOrWhiteSpace(jsonPath) && File.Exists(jsonPath))
        {
            Console.WriteLine("⚡ Binary file does not exist, parsing JSON file...");
            try
            {
                var parser = new BinanceDataParser();
                engineCommands = parser.ParseLines(File.ReadLines(jsonPath))
                    .Take(settings.NoOfOrderToTest)
                    .ToList();

                if (!string.IsNullOrWhiteSpace(binPath))
                {
                    using var output = File.Create(binPath);
                    foreach (var command in engineCommands)
                    {
                        command.WriteDelimitedTo(output);
                    }
                }

                Console.WriteLine($"✅ Parsed JSON successfully, engineCommands count: {engineCommands.Count:N0}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Critical error processing JSON/Binary: {ex.Message}");
                return;
            }
        }

        if (engineCommands.Count == 0)
        {
            Console.WriteLine("❌ No commands loaded. Exiting.");
            return;
        }

        var commands = engineCommands.ToArray();

        // Print command details
        // try
        // {
        //     engineCommands.ForEach(e =>
        //     {
        //         // Just access the property directly
        //         if (e.PlaceOrder != null)
        //         {
        //             Console.WriteLine(e.PlaceOrder.ToString());
        //         }
        //     });
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine(ex.Message);
        // }

        Console.WriteLine($"✅ Loaded {commands.Length:N0} commands into RAM ready for benchmark.");
        Console.WriteLine();
        Console.WriteLine("💀 Select benchmark mode:");
        Console.WriteLine("1. HFT Streaming (Sequential unbatched over single gRPC stream)");
        Console.WriteLine("2. Batching");
        Console.Write("Selection [1 or 2]: ");

        var choice = Console.ReadLine();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        for (int i = 0; i < settings.TestRepeat; i++)
        {
            Console.WriteLine($"\n================ RUN {i + 1}/{settings.TestRepeat} ================\n");

            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (choice == "1")
            {
                await SendOrderIndividually(client, commands, settings);
            }
            else
            {
                await SendOrdersByBatch(client, commands, settings);
            }
        }
    }

    private static async Task SendOrderIndividually(
        MatchingEngine.MatchingEngineClient client,
        EngineCommand[] commands,
        MarketSimulatorSettings settings)
    {
        Console.WriteLine("🚀 Launching Sequential Market Replay over gRPC...");

        bool useHistoricalPacing = settings.UseHistorical && settings.EnableHistoricalPacing;
        TimestampRatePacer? historicalPacer = useHistoricalPacing
            ? new TimestampRatePacer(settings.ReplaySpeed)
            : null;

        OrderRatePacer? randomPacer =
            !settings.UseHistorical && settings.EnableOrderPacing && settings.OrderRatePerSecond > 0
                ? new OrderRatePacer(settings.OrderRatePerSecond)
                : null;

        if (useHistoricalPacing)
            Console.WriteLine($"⏱ Historical pacing enabled at x{settings.ReplaySpeed:0.###}");

        if (!settings.UseHistorical && randomPacer != null)
            Console.WriteLine($"⏱ Random pacing enabled at {settings.OrderRatePerSecond:N0} orders/sec");

        var sw = Stopwatch.StartNew();
        long successCount = 0;

        using var call = client.PlaceBatchStream();

        var timestamps = new ConcurrentQueue<long>();
        var latencies = new List<double>(commands.Length);

        var readTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Success)
                        continue;

                    if (timestamps.TryDequeue(out long startTicks))
                    {
                        double elapsedMs =
                            (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
                        latencies.Add(elapsedMs);
                    }

                    Interlocked.Add(ref successCount, (long)response.Acks.Count);
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
                if (historicalPacer != null)
                {
                    long? historicalTs = TryGetHistoricalTimestampMs(command);
                    await historicalPacer.WaitAsync(historicalTs);
                }
                else if (randomPacer != null)
                {
                    await randomPacer.WaitAsync(1);
                }

                var batch = new EngineBatchCommand();
                batch.Commands.Add(command);

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

        PrintResults("SEQUENTIAL REPLAY RESULT", successCount, sw, latencies);
    }

    private static async Task SendOrdersByBatch(
        MatchingEngine.MatchingEngineClient client,
        EngineCommand[] commands,
        MarketSimulatorSettings settings)
    {
        bool useHistoricalBatchPacing = settings.EnableHistoricalPacing;


        if (useHistoricalBatchPacing)
        {
            await SendOrdersByBatchHistorical(client, commands, settings);
            return;
        }

        Console.WriteLine(
            $"\n🚀 [BENCHMARK] Starting Batching (Size: {settings.BatchSize}, Streams: {settings.Concurrency})...");

        if (settings.EnableOrderPacing)
        {
            Console.WriteLine(
                $"⏱ Random pacing enabled at {settings.OrderRatePerSecond:N0} orders/sec total (~{settings.PerStreamRate:N0}/stream)");
        }

        var sw = Stopwatch.StartNew();
        long successCount = 0;
        var tasks = new List<Task>();
        var latencies = new ConcurrentBag<double>();

        int totalCommands = commands.Length;


        int commandsPerTask = totalCommands / settings.Concurrency;

        for (int i = 0; i < settings.Concurrency; i++)
        {
            int taskIndex = i;
            int localStart = taskIndex * commandsPerTask;
            int localEnd = (taskIndex == settings.Concurrency - 1)
                ? totalCommands
                : localStart + commandsPerTask;

            tasks.Add(Task.Run(async () =>
            {
                using var call = client.PlaceBatchStream();
                var batchTimestamps = new ConcurrentQueue<long>();
                var pacer = settings.EnableOrderPacing && settings.OrderRatePerSecond > 0
                    ? new OrderRatePacer(settings.PerStreamRate)
                    : null;

                var readTask = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var response in call.ResponseStream.ReadAllAsync())
                        {
                            if (!response.Success)
                                continue;

                            if (batchTimestamps.TryDequeue(out long startTicks))
                            {
                                double elapsedMs =
                                    (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
                                latencies.Add(elapsedMs);
                            }

                            Interlocked.Add(ref successCount, (long)response.Acks.Count);
                        }
                    }
                    catch (RpcException ex)
                    {
                        Console.WriteLine($"\n❌ [STREAM {taskIndex}] Reader broke: {ex.Status.Detail}");
                    }
                });

                var batch = new EngineBatchCommand();
                batch.Commands.Capacity = settings.BatchSize;

                try
                {
                    for (int j = localStart; j < localEnd; j++)
                    {
                        batch.Commands.Add(commands[j]);

                        if (batch.Commands.Count >= settings.BatchSize)
                        {
                            batchTimestamps.Enqueue(Stopwatch.GetTimestamp());
                            await call.RequestStream.WriteAsync(batch);

                            if (pacer != null)
                                await pacer.WaitAsync(batch.Commands.Count);

                            batch = new EngineBatchCommand();
                            batch.Commands.Capacity = settings.BatchSize;
                        }
                    }

                    if (batch.Commands.Count > 0)
                    {
                        batchTimestamps.Enqueue(Stopwatch.GetTimestamp());
                        await call.RequestStream.WriteAsync(batch);

                        if (pacer != null)
                            await pacer.WaitAsync(batch.Commands.Count);
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

        PrintResults("BATCH REPLAY RESULT", successCount, sw, latencies.ToArray());
    }

    private static async Task SendOrdersByBatchHistorical(
        MatchingEngine.MatchingEngineClient client,
        EngineCommand[] commands,
        MarketSimulatorSettings settings)
    {
        Console.WriteLine(
            $"\n🚀 [BENCHMARK] Starting Historical Batching (Size: {settings.BatchSize}, Speed: x{settings.ReplaySpeed:0.###})...");
        Console.WriteLine("⏱ Historical batching groups consecutive commands with the same timestamp.");

        var sw = Stopwatch.StartNew();
        long successCount = 0;

        using var call = client.PlaceBatchStream();
        var pacer = new TimestampRatePacer(settings.ReplaySpeed);

        var batchSendTicks = new ConcurrentQueue<long>();
        var latencies = new List<double>();

        var readTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (!response.Success)
                        continue;

                    if (batchSendTicks.TryDequeue(out long startTicks))
                    {
                        double elapsedMs =
                            (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
                        latencies.Add(elapsedMs);
                    }

                    Interlocked.Add(ref successCount, (long)response.Acks.Count);
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"\n❌ Historical batch reader broke: {ex.Status.Detail}");
            }
        });

        try
        {
            var batch = new EngineBatchCommand();
            batch.Commands.Capacity = settings.BatchSize;

            long? batchTimestamp = null;


            long sentCount = 0;
            int total = commands.Length;
            int updateInterval = Math.Max(1, total / 100);

            foreach (var command in commands)
            {
                long? commandTimestamp = TryGetHistoricalTimestampMs(command);

                bool shouldFlush =
                    batch.Commands.Count > 0 &&
                    (
                        batch.Commands.Count >= settings.BatchSize ||
                        !SameTimestamp(batchTimestamp, commandTimestamp)
                    );

                if (shouldFlush)
                {
                    await pacer.WaitAsync(batchTimestamp);

                    batchSendTicks.Enqueue(Stopwatch.GetTimestamp());
                    await call.RequestStream.WriteAsync(batch);


                    sentCount += batch.Commands.Count;
                    if (sentCount >= updateInterval || sentCount == total)
                    {
                        DrawProgressBar(sentCount, total);
                    }

                    batch = new EngineBatchCommand();
                    batch.Commands.Capacity = settings.BatchSize;
                    batchTimestamp = null;
                }

                if (batch.Commands.Count == 0)
                    batchTimestamp = commandTimestamp;

                batch.Commands.Add(command);
            }

            if (batch.Commands.Count > 0)
            {
                sentCount += batch.Commands.Count;
                DrawProgressBar(sentCount, total);
            }

            Console.WriteLine();

            if (batch.Commands.Count > 0)
            {
                await pacer.WaitAsync(batchTimestamp);

                batchSendTicks.Enqueue(Stopwatch.GetTimestamp());
                await call.RequestStream.WriteAsync(batch);
            }

            await call.RequestStream.CompleteAsync();
        }
        catch (RpcException ex)
        {
            Console.WriteLine($"\n❌ Historical batch writer broke: {ex.Status.Detail}");
        }

        await readTask;
        sw.Stop();

        PrintResults("HISTORICAL BATCH REPLAY RESULT", successCount, sw, latencies);
    }

    private static long? TryGetHistoricalTimestampMs(EngineCommand command)
    {
        if (command?.PlaceOrder != null && command.PlaceOrder.Timestamp > 0)
            return command.PlaceOrder.Timestamp;

        if (command?.CancelOrder != null && command.CancelOrder.Timestamp > 0)
            return command.CancelOrder.Timestamp;

        return null;
    }

    private static bool SameTimestamp(long? left, long? right)
    {
        if (!left.HasValue && !right.HasValue)
            return true;

        if (left.HasValue && right.HasValue)
            return left.Value == right.Value;

        return false;
    }

    private static void PrintResults(
        string title,
        long successCount,
        Stopwatch sw,
        IReadOnlyList<double> latencies)
    {
        var sorted = latencies.ToArray();
        Array.Sort(sorted);

        double p0 = sorted.Length > 0 ? sorted[0] : 0;
        double p50 = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.50)] : 0;
        double p99 = sorted.Length > 0 ? sorted[Math.Min((int)(sorted.Length * 0.99), sorted.Length - 1)] : 0;
        double p999 = sorted.Length > 0 ? sorted[Math.Min((int)(sorted.Length * 0.999), sorted.Length - 1)] : 0;
        double p100 = sorted.Length > 0 ? sorted[^1] : 0;

        Console.WriteLine("\n========================================");
        Console.WriteLine($"🏁 {title}");
        Console.WriteLine("========================================");
        Console.WriteLine($"⚡ TPS: {successCount / sw.Elapsed.TotalSeconds:N0} messages/sec");
        Console.WriteLine($"✅ Delivered: {successCount:N0}");
        Console.WriteLine("========================================");
        Console.WriteLine($"⏱️ Latency Min (p0):    {p0:F3} ms");
        Console.WriteLine($"⏱️ Latency Median(p50): {p50:F3} ms");
        Console.WriteLine($"⏱️ Latency p99:         {p99:F3} ms");
        Console.WriteLine($"⏱️ Latency p99.9:       {p999:F3} ms");
        Console.WriteLine($"⏱️ Latency Max (p100):  {p100:F3} ms");
        Console.WriteLine("========================================");
    }

    private static EngineCommand GenerateRandomOrderRequest(int index)
    {
        return new EngineCommand
        {
            PlaceOrder = new OrderRequest
            {
                ClientOrderId = (ulong)(1_000_000 + index),
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
            CancelOrder = new CancelRequest
            {
                EngineOrderId = (ulong)(1_000_000 + index),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }
        };
    }

    private static void DrawProgressBar(long current, long total, int barSize = 40)
    {
        if (total == 0) return;

        decimal progress = (decimal)current / total;
        int progressChars = (int)(progress * barSize);

        // [====>----------] 45%
        string bar = new string('=', progressChars) + (progressChars < barSize ? ">" : "");
        string spaces = new string('-', barSize - bar.Length);

        Console.Write($"\rProgress: [{bar}{spaces}] {(progress * 100):F1}% ({current:N0}/{total:N0})");
    }
}