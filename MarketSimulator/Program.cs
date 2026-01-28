using System.Collections.Concurrent;
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Orderbook;

namespace MarketSimulator;

class Program
{
    private const int TotalOrders = 500_000; // Target: 500k orders
    private const int BatchSize = 100;       // 100 per batch 
    private const string Address = "http://127.0.0.1:50051";

    static async Task Main(string[] args)
    {
        var httpHandler = new HttpClientHandler();
        using var channel = GrpcChannel.ForAddress(Address, new GrpcChannelOptions { HttpHandler = httpHandler });
        var client = new MatchingEngine.MatchingEngineClient(channel);

        // 1. Pre-generate data
        Console.WriteLine($"⚡ Pre-generating {TotalOrders} orders...");
        var rawOrders = Enumerable.Range(0, TotalOrders).Select(i => GenerateRandomOrder(i)).ToArray();

        // 2. Chunk the data - LINQ Chunk is a .NET 6+ feature
        Console.WriteLine($"📦 Batching into chunks of {BatchSize}...");
        var batches = rawOrders.Chunk(BatchSize).Select(chunk => new OrderBatchRequest
        {
            Orders = { chunk } // Google Protobuf RepeatedField syntax
        }).ToArray();

        Console.WriteLine($"Ready to blast {batches.Length} batches ({TotalOrders} orders)...");
        Console.WriteLine("Press ENTER to destroy the engine...");
        Console.ReadLine();

        var sw = Stopwatch.StartNew();
        int processedCount = 0;

        // 3. Use Batch Stream
        using var call = client.PlaceBatchStream();

        // Receiver Task
        var reader = Task.Run(async () =>
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                // Since this is a Batch response, increment count by the amount the Server processed
                Interlocked.Add(ref processedCount, (int)response.ProcessedCount);
            }
        });

        // Sender Loop (Sending Batches)
        foreach (var batch in batches)
        {
            await call.RequestStream.WriteAsync(batch);
        }
        await call.RequestStream.CompleteAsync();
        
        // Wait for all responses
        await reader;
        sw.Stop();

        PrintReport(sw, processedCount);
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

    private static void PrintReport(Stopwatch sw, int count)
    {
        double tps = count / sw.Elapsed.TotalSeconds;
        Console.WriteLine("\n🚀 BATCHING RESULT:");
        Console.WriteLine($"⏱️ Total Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"⚡ TPS: {tps:N0} orders/sec");
        Console.WriteLine($"✅ Processed: {count}");
    }
}