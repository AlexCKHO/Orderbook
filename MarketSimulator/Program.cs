using System.Collections.Concurrent; // 👈 必須引用這個！
using System.Diagnostics;
using Grpc.Core;
using Grpc.Net.Client;
using Orderbook; // 你的 Namespace

namespace MarketSimulator;

class Program
{
    private const int TotalOrders = 50000; 
    private const string Address = "http://127.0.0.1:50051";

    static async Task Main(string[] args)
    {
        // 1. 設定連線
        // Mac/Linux 需要 HttpClientHandler 來支援 http2 without SSL
        var httpHandler = new HttpClientHandler();
        // httpHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        
        using var channel = GrpcChannel.ForAddress(Address, new GrpcChannelOptions { HttpHandler = httpHandler });
        var client = new MatchingEngine.MatchingEngineClient(channel);

        // 2. 預先準備數據 (Zero Allocation during test)
        Console.WriteLine($"⚡ Pre-generating {TotalOrders} orders...");
        var requests = Enumerable.Range(0, TotalOrders).Select(i => GenerateRandomOrder(i)).ToArray();
        
        // 🔥 3. 修正：在此宣告 latencies (ConcurrentDictionary)
        // Key: RequestId (ulong), Value: StartTimestamp (long)
        var latencies = new ConcurrentDictionary<ulong, long>();

        Console.WriteLine($"🔥 [STREAMING MODE] Ready to blast {TotalOrders} orders...");
        Console.WriteLine("Press ENTER to start...");
        Console.ReadLine();

        var sw = Stopwatch.StartNew();
        int successCount = 0;

        // 4. 開啟雙向流 (Stream)
        using var call = client.PlaceOrderStream();

        // 5. 接收端任務 (Receiver Task)
        // 使用 (Func<Task>) 強制轉型解決 Task.Run 歧義
        var responseReaderTask = Task.Run((Func<Task>)(async () =>
        {
            // ReadAllAsync 需要 .NET Core 3.0+
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                // 注意：這裡假設你 Proto 已經加了 request_id，C# 生成出來就是 RequestId
                if (latencies.TryGetValue(response.RequestId, out long startTime))
                {
                    long endTime = Stopwatch.GetTimestamp();
                    // 計算 Latency 並更新 Value (存成時間差 ms)
                    // 使用 double 運算保持精度
                    long elapsedTicks = endTime - startTime;
                    double ms = (double)elapsedTicks / Stopwatch.Frequency * 1000;
                    
                    latencies[response.RequestId] = (long)ms; 
                }
                Interlocked.Increment(ref successCount);
            }
        }));

        // 6. 發送端任務 (Sender Loop)
        foreach (var req in requests)
        {
            // 記錄開始時間 (Raw Ticks)
            latencies[req.Id] = Stopwatch.GetTimestamp();
            
            // 寫入 Stream
            await call.RequestStream.WriteAsync(req);
        }

        // 7. 告訴 Server：我射完喇 (Complete)
        await call.RequestStream.CompleteAsync();

        // 8. 等待接收端收完所有回覆
        await responseReaderTask;
        
        sw.Stop();

        // 9. 打印報告
        // 只取 Values (已經變成 Latency ms 了)
        PrintReport(sw, latencies.Values.ToArray(), successCount);
    }

    private static OrderRequest GenerateRandomOrder(int index)
    {
        return new OrderRequest
        {
            Id = (ulong)(200000 + index), // 確保 ID 不重複
            Price = (ulong)Random.Shared.Next(100, 201),
            Qty = (ulong)Random.Shared.Next(1, 100),
            Side = Random.Shared.Next(0, 2) == 0 ? Side.Bid : Side.Ask,
            OrderType = OrderType.Limit,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static void PrintReport(Stopwatch sw, long[] latencyValues, int success)
    {
        // 過濾掉還沒計算完的 (值還是極大的 Timestamp) 或者異常值
        // 這裡我們簡單過濾 > 0 且 < 10000ms (假設)
        var sorted = latencyValues.Where(x => x >= 0 && x < 10000).OrderBy(x => x).ToArray();
        
        double tps = success / sw.Elapsed.TotalSeconds;

        Console.WriteLine("\n🚀 STREAMING RESULT:");
        Console.WriteLine($"⏱️ Total Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"⚡ TPS: {tps:N0} orders/sec");
        Console.WriteLine($"✅ Success: {success}");
        
        if (sorted.Length > 0)
        {
            Console.WriteLine("📊 Latency Distribution:");
            Console.WriteLine($"   p50 (Median): {sorted[sorted.Length / 2]} ms");
            Console.WriteLine($"   p95:          {sorted[(int)(sorted.Length * 0.95)]} ms");
            Console.WriteLine($"   p99:          {sorted[(int)(sorted.Length * 0.99)]} ms");
            Console.WriteLine($"   Max:          {sorted.Last()} ms");
        }
        else 
        {
             Console.WriteLine("⚠️ No latency data collected. Check if RequestId is matching.");
        }
    }
}