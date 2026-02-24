using System.Collections.Concurrent;
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Orderbook; // Ensure this matches your proto package name

namespace MarketSimulator;

class Program
{
    // Memory constraint warning: 100M objects consume ~10-15GB RAM
    private const int TotalOrders = 10_000_000;
    private const string Address = "http://127.0.0.1:50051";
    private const int Concurrency = 4;

    static async Task Main(string[] args)
    {
        // Network tuning for low-latency RPCs
        var httpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,

            ConnectTimeout = TimeSpan.FromSeconds(5)
        };

        using var channel = GrpcChannel.ForAddress(Address, new GrpcChannelOptions
        {
            HttpHandler = httpHandler,
            // Bump buffer limits to support massive throughput during batching
            MaxReceiveMessageSize = 16 * 1024 * 1024,
            MaxSendMessageSize = 16 * 1024 * 1024
        });

        var client = new MatchingEngine.MatchingEngineClient(channel);

        Console.WriteLine($"⚡ [SETUP] Pre-generating {TotalOrders:N0} orders in RAM...");

        // Pre-allocate objects to isolate GC overhead from our benchmark measurements
        var requests = Enumerable.Range(0, TotalOrders).Select(i => GenerateRandomOrder(i)).ToArray();

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
            await RunStreamingTest(client, requests);
        }
        else
        {
            int[] testBatchSizes = { 5000 };

            Console.WriteLine($"\n🕵️‍♂️ 啟動自動尋找最佳 Batch Size (Sweet Spot) 測試...");
            Console.WriteLine($"| Batch Size | TPS (Orders/sec) | Latency p50 (ms) | Latency p99 (ms) |");
            Console.WriteLine($"|------------|------------------|------------------|------------------|");
            
            
            foreach (var size in testBatchSizes)
            {
                // 1. 執行 GC 清理
                GC.Collect();
    
                // 2. 執行你寫好嘅 Batching 測試 (但今次傳入變數 size)
                await RunBatchingTest(client, requests, size);

            }
        }
    }

    // ==========================================
    // MODE 1: HFT STREAMING
    // Tracks latency by matching RequestId in response
    // ==========================================
    private static async Task RunStreamingTest(MatchingEngine.MatchingEngineClient client, OrderRequest[] orders)
    {
        // Scale concurrency based on logical cores (typically 4-8 yields the best throughput)

        Console.WriteLine($"🚀 Launching HFT Streaming ({Concurrency} Concurrent Streams)...");

        var sentTimestamps = new ConcurrentDictionary<ulong, long>();
        var latencies = new ConcurrentBag<double>();

        var sw = Stopwatch.StartNew();
        int successCount = 0;

        // Partition the 100M orders across worker threads
        int chunkSize = orders.Length / Concurrency;
        var tasks = new List<Task>();

        for (int i = 0; i < Concurrency; i++)
        {
            int taskIndex = i;
            var chunk = orders.Skip(taskIndex * chunkSize).Take(chunkSize).ToArray();

            // Spin up a dedicated gRPC stream per task to avoid head-of-line blocking
            tasks.Add(Task.Run(async () =>
            {
                using var call = client.PlaceOrderStream();

                var reader = Task.Run(async () =>
                {
                    await foreach (var resp in call.ResponseStream.ReadAllAsync())
                    {
                        if (sentTimestamps.TryRemove(resp.RequestId, out long startTicks))
                        {
                            latencies.Add(GetElapsedMs(startTicks));
                        }

                        if (resp.Success)
                        {
                            Interlocked.Increment(ref successCount);
                        }
                    }
                });

                // Hot path: Sender loop
                foreach (var req in chunk)
                {
                    if (req.Id % 1000 == 0)
                    {
                        sentTimestamps[req.Id] = Stopwatch.GetTimestamp();
                    }

                    await call.RequestStream.WriteAsync(req);
                }

                await call.RequestStream.CompleteAsync();
                await reader;
            }));
        }

        // Wait for all concurrent streams to flush and close
        await Task.WhenAll(tasks);
        sw.Stop();

        PrintReport("HFT MULTI-STREAMING", sw, successCount, latencies);
    }

    // ==========================================
    // MODE 2: BATCHING
    // Tracks latency using strict FIFO assumptions
    // ==========================================
    private static async Task RunBatchingTest(MatchingEngine.MatchingEngineClient client, OrderRequest[] orders,
        int batchSize = 5_000)
    {
        const int repeat = 5;

        Console.WriteLine($"\n🚀 [BENCHMARK] Starting Batching (Size: {batchSize}, Streams: {Concurrency})...");

        // Pre-compute all batches to keep allocation out of the benchmark hot loop
        var batches = orders.Chunk(batchSize).Select(chunk => new OrderBatchRequest
        {
            Orders = { chunk }
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

    private static OrderRequest GenerateRandomOrder(int index)
    {
        return new OrderRequest
        {
            Id = (ulong)(1000000 + index),
            Price = (ulong)Random.Shared.Next(100, 201),
            Qty = (ulong)Random.Shared.Next(1, 100),
            Side = Random.Shared.Next(0, 2) == 0 ? Side.Bid : Side.Ask,
            OrderType = OrderType.Limit,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
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