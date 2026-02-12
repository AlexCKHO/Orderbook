using System.Collections.Concurrent;
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Orderbook;
using System.Net.Http;

namespace MarketSimulator;

class Program
{
    private const int TotalOrders = 500_000;
    private const string Address = "http://127.0.0.1:50051";

    static async Task Main(string[] args)
    {
        // Performance tuning for Apple M4
        var httpHandler = new SocketsHttpHandler
        {
            // TCP No Delay is essential for HFT to send packets immediately
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };

        using var channel = GrpcChannel.ForAddress(Address, new GrpcChannelOptions 
        { 
            HttpHandler = httpHandler 
        });
        
        var client = new MatchingEngine.MatchingEngineClient(channel);

        // Pre-generate all orders to avoid CPU overhead during the test
        Console.WriteLine($"⚡ Pre-generating {TotalOrders} orders...");
        var requests = Enumerable.Range(0, TotalOrders).Select(i => GenerateRandomOrder(i)).ToArray();

        Console.WriteLine("\nChoose your destruction mode:");
        Console.WriteLine("1. HFT Streaming (Industry Standard - Low Latency)");
        Console.WriteLine("2. Batching (High Throughput - Higher Latency)");
        Console.Write("Selection [1 or 2]: ");
        var choice = Console.ReadLine();

        if (choice == "1")
        {
            await RunStreamingTest(client, requests);
        }
        else
        {
            await RunBatchingTest(client, requests);
        }
    }

    // --- MODE 1: HFT STREAMING (The "No Delay" Way) ---
    private static async Task RunStreamingTest(MatchingEngine.MatchingEngineClient client, OrderRequest[] orders)
    {
        Console.WriteLine("🚀 Launching HFT Streaming (Zero-waiting)...");
        var sw = Stopwatch.StartNew();
        int successCount = 0;

        using var call = client.PlaceOrderStream();

        // Background receiver task
        var reader = Task.Run(async () =>
        {
            await foreach (var resp in call.ResponseStream.ReadAllAsync())
            {
                Interlocked.Increment(ref successCount);
            }
        });

        // Firehose: push every order immediately into the stream
        foreach (var req in orders)
        {
            await call.RequestStream.WriteAsync(req);
        }

        await call.RequestStream.CompleteAsync();
        await reader;
        sw.Stop();

        PrintReport("HFT STREAMING", sw, successCount);
    }

    // --- MODE 2: BATCHING (The "Pack & Send" Way) ---
    private static async Task RunBatchingTest(MatchingEngine.MatchingEngineClient client, OrderRequest[] orders)
    {
        const int BatchSize = 100;
        Console.WriteLine($"🚀 Launching Batching (Size: {BatchSize})...");
        
        // Group orders into batches of 100
        var batches = orders.Chunk(BatchSize).Select(chunk => new OrderBatchRequest
        {
            Orders = { chunk }
        }).ToArray();

        var sw = Stopwatch.StartNew();
        int totalProcessed = 0;

        using var call = client.PlaceBatchStream();

        var reader = Task.Run(async () =>
        {
            await foreach (var resp in call.ResponseStream.ReadAllAsync())
            {
                Interlocked.Add(ref totalProcessed, (int)resp.ProcessedCount);
            }
        });

        foreach (var batch in batches)
        {
            await call.RequestStream.WriteAsync(batch);
        }

        await call.RequestStream.CompleteAsync();
        await reader;
        sw.Stop();

        PrintReport("BATCHING", sw, totalProcessed);
    }

    private static OrderRequest GenerateRandomOrder(int index) => new OrderRequest
    {
        Id = (ulong)(3000000 + index),
        Price = (ulong)Random.Shared.Next(100, 201),
        Qty = (ulong)Random.Shared.Next(1, 100),
        Side = Random.Shared.Next(0, 2) == 0 ? Side.Bid : Side.Ask,
        OrderType = OrderType.Limit,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };

    private static void PrintReport(string mode, Stopwatch sw, int count)
    {
        Console.WriteLine($"\n{new string('=', 40)}");
        Console.WriteLine($"🏁 {mode} RESULT");
        Console.WriteLine($"{new string('=', 40)}");
        Console.WriteLine($"⚡ TPS: {count / sw.Elapsed.TotalSeconds:N0} orders/sec");
        Console.WriteLine($"⏱️ Total Time: {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"✅ Processed: {count}");
        Console.WriteLine(new string('=', 40));
    }
}