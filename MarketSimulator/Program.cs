using System.Collections.Concurrent;
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Orderbook; // Ensure this matches your proto package name

namespace MarketSimulator;

class Program
{
    // Adjust based on RAM (100M objects consume ~10-15GB depending on field sizes)
    private const int TotalOrders = 50_000;
    private const string Address = "http://127.0.0.1:50051";

    static async Task Main(string[] args)
    {
        // Network tuning for low-latency communication
        var httpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            // Disable Nagle's Algorithm to reduce latency for small packets
            ConnectTimeout = TimeSpan.FromSeconds(5) 
        };

        using var channel = GrpcChannel.ForAddress(Address, new GrpcChannelOptions 
        { 
            HttpHandler = httpHandler,
            // High buffer limits to support massive batch throughput
            MaxReceiveMessageSize = 16 * 1024 * 1024, 
            MaxSendMessageSize = 16 * 1024 * 1024
        });
        
        var client = new MatchingEngine.MatchingEngineClient(channel);

        Console.WriteLine($"⚡ [SETUP] Pre-generating {TotalOrders:N0} orders in RAM...");
        
        // Pre-allocate objects to isolate GC overhead from benchmark measurements
        var requests = Enumerable.Range(0, TotalOrders).Select(i => GenerateRandomOrder(i)).ToArray();

        Console.WriteLine("\n💀 Select benchmark mode:");
        Console.WriteLine("1. HFT Streaming (Precision tracking via Request ID)");
        Console.WriteLine("2. Batching (FIFO Latency estimation)");
        Console.Write("Selection [1 or 2]: ");
        var choice = Console.ReadLine();

        // Trigger manual GC to clear setup artifacts before starting
        GC.Collect();
        GC.WaitForPendingFinalizers();

        if (choice == "1")
        {
            await RunStreamingTest(client, requests);
        }
        else
        {
            await RunBatchingTest(client, requests);
        }
    }

    // ==========================================
    // MODE 1: HFT STREAMING
    // Tracks latency by matching RequestId in response
    // ==========================================
    private static async Task RunStreamingTest(MatchingEngine.MatchingEngineClient client, OrderRequest[] orders)
    {
        Console.WriteLine("🚀 Launching HFT Streaming (Profiling Latency)...");
        
        var sentTimestamps = new ConcurrentDictionary<ulong, long>();
        var latencies = new ConcurrentBag<double>();
        
        var sw = Stopwatch.StartNew();
        int successCount = 0;

        using var call = client.PlaceOrderStream();

        // Response receiver thread
        var reader = Task.Run(async () =>
        {
            await foreach (var resp in call.ResponseStream.ReadAllAsync())
            {
                // Match response to original timestamp using RequestId
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

        // Sender loop
        foreach (var req in orders)
        {
            // Sample 1 order every 1000 for latency tracking
            if (req.Id % 1000 == 0)
            {
                sentTimestamps[req.Id] = Stopwatch.GetTimestamp();
            }
            await call.RequestStream.WriteAsync(req);
        }

        await call.RequestStream.CompleteAsync();
        await reader;
        sw.Stop();

        PrintReport("HFT STREAMING", sw, successCount, latencies);
    }

    // ==========================================
    // MODE 2: BATCHING
    // Tracks latency using FIFO assumptions
    // ==========================================
    private static async Task RunBatchingTest(MatchingEngine.MatchingEngineClient client, OrderRequest[] orders)
    {
        const int BatchSize = 100;
        Console.WriteLine($"🚀 Launching Batching (Size: {BatchSize}) with FIFO Latency...");
        
        var batches = orders.Chunk(BatchSize).Select(chunk => new OrderBatchRequest
        {
            Orders = { chunk }
        }).ToArray();

        var batchTimestamps = new ConcurrentQueue<long>();
        var latencies = new ConcurrentBag<double>();

        var sw = Stopwatch.StartNew();
        int totalProcessed = 0;

        using var call = client.PlaceBatchStream();

        // Response receiver thread
        var reader = Task.Run(async () =>
        {
            await foreach (var resp in call.ResponseStream.ReadAllAsync())
            {
                // Dequeue earliest sent timestamp (FIFO)
                if (batchTimestamps.TryDequeue(out long startTicks))
                {
                    // Only record if the batch was selected for sampling
                    if (startTicks > 0)
                    {
                        latencies.Add(GetElapsedMs(startTicks));
                    }
                }
                
                Interlocked.Add(ref totalProcessed, (int)resp.ProcessedCount);
            }
        });

        // Sender loop
        int batchCounter = 0;
        foreach (var batch in batches)
        {
            long timestampToQueue = -1; // -1 indicates non-sampled batch

            // Sample every 100th batch
            if (batchCounter % 100 == 0)
            {
                timestampToQueue = Stopwatch.GetTimestamp();
            }

            // Enqueue before sending to prevent race conditions
            batchTimestamps.Enqueue(timestampToQueue);
            await call.RequestStream.WriteAsync(batch);
            batchCounter++;
        }

        await call.RequestStream.CompleteAsync();
        await reader;
        sw.Stop();

        PrintReport("BATCHING", sw, totalProcessed, latencies);
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