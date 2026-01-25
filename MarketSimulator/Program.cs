using System.Collections.Concurrent;
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Orderbook;

namespace MarketSimulator;

class Program
{
    // 🔥 加大到 30 萬張單，確保測試時間夠長
    private const int TotalOrders = 300_000; 
    private const string Address = "http://127.0.0.1:50051";

    static async Task Main(string[] args)
    {
        var httpHandler = new HttpClientHandler();
        using var channel = GrpcChannel.ForAddress(Address, new GrpcChannelOptions { HttpHandler = httpHandler });
        var client = new MatchingEngine.MatchingEngineClient(channel);

        // 1. 生成數據 (Zero Allocation)
        Console.WriteLine($"⚡ Pre-generating {TotalOrders} orders...");
        var requests = Enumerable.Range(0, TotalOrders).Select(i => GenerateRandomOrder(i)).ToArray();
        
        // 為了避免 Memory 爆，我們只抽樣記錄 Latency (每 100 張記一次)
        // Key: RequestId
        var latencies = new ConcurrentDictionary<ulong, long>();

        // ==========================================
        // 🔥 WARM UP PHASE (暖身階段)
        // ==========================================
        Console.WriteLine("🏃 Warming up engine (sending 1000 orders)...");
        await RunTest(client, requests.Take(1000).ToArray(), null); // Pass null to skip recording
        Console.WriteLine("✅ Warm up complete. Engine is HOT.");
        
        Console.WriteLine("\n========================================");
        Console.WriteLine($"🔥 [REAL TEST] Blasting {TotalOrders} orders...");
        Console.WriteLine("========================================");
        
        // ==========================================
        // 🚀 REAL TEST PHASE (真測試)
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
                // 只在真測試期間記錄 Latency
                if (latencies != null && latencies.TryGetValue(response.RequestId, out long startTime))
                {
                    long endTime = Stopwatch.GetTimestamp();
                    long elapsedTicks = endTime - startTime;
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
                // 我們只記錄一部分 Latency 以節省 Client 端 CPU (Sampling)
                // 否則 Dictionary 太大會影響測試準確度
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