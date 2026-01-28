using System.Diagnostics;
using Grpc.Net.Client;
using Orderbook;

namespace MarketSimulator;

class Program
{
    private const int TotalOrders = 50000;
    private const string Address = "http://127.0.0.1:50051";

    static async Task Main(string[] args)
    {
        // 1. Establish Connection
        using var channel = GrpcChannel.ForAddress(Address);
        var client = new MatchingEngine.MatchingEngineClient(channel);

        Console.WriteLine("💣 HFT Stress Test Client Ready.");
        Console.WriteLine($"🎯 Target: {TotalOrders} orders.");
        Console.WriteLine("Press ENTER to launch the attack...");
        Console.ReadLine();

        // 2. Start the timer
        Console.WriteLine("🚀 Launching sequence started...");
        var stopwatch = Stopwatch.StartNew();

        int successCount = 0;
        int errorCount = 0;

        // 3. Machine Gun mode (Parallel.ForEachAsync)
        // Start multiple threads to send order request to rust service

        await Parallel.ForEachAsync(Enumerable.Range(0, TotalOrders),
            new ParallelOptions { MaxDegreeOfParallelism = 20 },
            async (i, ct) =>
            {
                try
                {
                    // Randomly generate orders 
                    var request = GenerateRandomOrder(i);

                    var reply = await client.PlaceOrderAsync(request);

                    if (reply.Success)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref errorCount);
                    }
                }
                catch (Exception e)
                {
                    Interlocked.Increment(ref errorCount);
                }

                // Progress bar for each 1000 orders
                if (i % 100 == 0 && i > 0)
                {
                    Console.Write(".");
                }
            });

        stopwatch.Stop();

        // 4. Reporting Performance
        Console.WriteLine("\n\n=========================================");
        Console.WriteLine($"⏱️  Time Taken:   {stopwatch.Elapsed.TotalMilliseconds} ms");
        Console.WriteLine($"✅ Successful:   {successCount}");
        Console.WriteLine($"❌ Failed:       {errorCount}");
        Console.WriteLine("=========================================");

        // TPS (Transactions Per Second)
        double tps = TotalOrders / stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine($"⚡ TPS (Speed):  {tps:N0} orders/second");
        Console.WriteLine("=========================================");
    }

    private static OrderRequest GenerateRandomOrder(int index)
    {   
        
        var side = Random.Shared.Next(0, 2) == 0 ? Side.Bid : Side.Ask;

        var price = Random.Shared.Next(100, 201);
        
        var now = DateTimeOffset.UtcNow;
        long unixNanoseconds = (now.Ticks - DateTime.UnixEpoch.Ticks) * 100;

        return new OrderRequest
        {
            Id = (ulong)(10000 + index),
            Price = (ulong)price,
            Qty = (ulong)Random.Shared.Next(1, 100),
            Side = side,
            OrderType = OrderType.Limit,
            Timestamp = unixNanoseconds
        };
    }
}