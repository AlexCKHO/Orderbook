using System.Collections.Concurrent;
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Orderbook;

namespace MarketSimulator;

class Program
{
    // Increased to 300k orders to ensure the test duration is long enough
    private const int TotalOrders = 300_000; 
    private const string Address = "http://127.0.0.1:50051";

    static async Task Main(string[] args)
    {
        var httpHandler = new HttpClientHandler();
        using var channel = GrpcChannel.ForAddress(Address, new GrpcChannelOptions { HttpHandler = httpHandler });
        var client = new MatchingEngine.MatchingEngineClient(channel);

        // 1. Generate Data (Zero Allocation)
        Console.WriteLine($"⚡ Pre-generating {TotalOrders} orders...");
        var requests = Enumerable.Range(0, TotalOrders).Select(i => GenerateRandomOrder(i)).ToArray();
        
        // To prevent memory bloat, we only record sampled Latency (e.g., every 100th order)
        // Key: RequestId
        var latencies = new ConcurrentDictionary<ulong, long>();

        // ==========================================
        //  WARM UP PHASE 
        // ==========================================
        Console.WriteLine("🏃 Warming up engine (sending 1000 orders)...");
        await RunTest(client, requests.Take(1000).ToArray(), null); // Pass null to skip recording
        Console.WriteLine("✅ Warm up complete. Engine is HOT.");
        
        Console.WriteLine("\n========================================");
        Console.WriteLine($"🔥 [REAL TEST] Blasting {TotalOrders} orders...");
        Console.WriteLine("========================================");
        
        // ==========================================
        //  REAL TEST PHASE 
        // ==========================================
        await RunTest(client, requests, latencies);
    }

    private static async Task RunTest(MatchingEngine.MatchingEngineClient client, OrderRequest[] orders, ConcurrentDictionary<ulong, long>? latencies)
    {
        var sw = Stopwatch.StartNew();
        int successCount = 0;
        using var call = client.PlaceOrderStream();

        // Receiver Task
        var reader = Task.Run(async () =>
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                // Only record Latency during the real test phase
                if (latencies != null && latencies.TryGetValue(response.RequestId, out long startTime))
                {
                    long endTime = Stopwatch.GetTimestamp();
                    long elapsedTicks = endTime - startTime;
                    // Convert ticks to milliseconds
                    latencies[response.RequestId] = (long)((double)elapsedTicks / Stopwatch.Frequency * 1000);
                }
                Interlocked.Increment(ref successCount);
            }
        });

        // Sender Loop
        foreach (var req in orders)
        {
            if (latencies != null)
            {
                // We only record a portion of Latency to save Client-side CPU (Sampling)
                // Otherwise, the Dictionary size would affect test accuracy
                if (req.Id % 100 == 0) 
                {
                    latencies[req.Id] = Stopwatch.GetTimestamp();
                }
            }
            await call.RequestStream.WriteAsync(req);
        }
        await call.RequestStream.CompleteAsync();
        await reader;
        sw.Stop();

        if (latencies != null)
        {
            PrintReport(sw, latencies.Values.ToArray(), successCount);
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

    private static void PrintReport(Stopwatch sw, long[] latencyValues, int success)
    {
        // Filter out anomalies and sort for percentile calculation
        var sorted = latencyValues.Where(x => x >= 0 && x < 10000).OrderBy(x => x).ToArray();
        double tps = success / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"⏱️ Total Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"⚡ TPS: {tps:N0} orders/sec");
        
        if (sorted.Length > 0)
        {
            Console.WriteLine($"📊 Latency (Sampled {sorted.Length} orders):");
            Console.WriteLine($"   p50: {sorted[sorted.Length / 2]} ms");
            Console.WriteLine($"   p99: {sorted[(int)(sorted.Length * 0.99)]} ms");
        }
    }
}