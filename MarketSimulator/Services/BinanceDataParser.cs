using System.Text.Json;
using MarketSimulator.Models;
using Orderbook;

namespace MarketSimulator.Services;

public class BinanceDataParser
{
    private readonly Dictionary<ulong, ulong> _activeBids = new();
    private readonly Dictionary<ulong, ulong> _activeAsks = new();
    private ulong _currentOrderId = 1_000_000;
    
    public int CorruptedLinesCount { get; private set; } = 0;

    public IEnumerable<EngineCommand> ParseLines(IEnumerable<string> jsonLines)
    {
        foreach (var line in jsonLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            int dataIndex = line.IndexOf("\"data\":{");
            if (dataIndex == -1) continue;
            
            string actualJson = line.Substring(dataIndex + 7);
            actualJson = actualJson.Substring(0, actualJson.Length - 1);

            BinanceMessage msg = null;

            // 🛡️ 加入 try-catch 攔截 64KB 截斷 / 格式錯誤嘅 JSON
            try
            {
                msg = JsonSerializer.Deserialize<BinanceMessage>(actualJson);
            }
            catch (JsonException)
            {
                // 遇到爛 JSON，記錄數量，然後直接 skip 呢一行，繼續處理下一行
                CorruptedLinesCount++;
                continue; 
            }

            if (msg == null) continue;

            if (msg.Stream.Contains("depth"))
            {
                var depth = msg.Data.Deserialize<DepthData>();
                
                foreach (var cmd in ProcessOrderBookLevels(depth.Bids, Side.Bid, _activeBids))
                    yield return cmd;

                foreach (var cmd in ProcessOrderBookLevels(depth.Asks, Side.Ask, _activeAsks))
                    yield return cmd;
            }
            else if (msg.Stream.Contains("aggTrade"))
            {
                var trade = msg.Data.Deserialize<AggTradeData>();
                ulong price = (ulong)(decimal.Parse(trade.Price) * 100m);
                ulong qty = (ulong)(decimal.Parse(trade.Qty) * 100_000_000m);
                var side = trade.IsBuyerMaker ? Side.Ask : Side.Bid;

                yield return new EngineCommand
                {
                    PlaceOrder = new OrderRequest
                    {
                        Id = _currentOrderId++,
                        Price = price,
                        Qty = qty,
                        Side = side,
                        OrderType = OrderType.Market,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                };
            }
        }
    }
    private IEnumerable<EngineCommand> ProcessOrderBookLevels(string[][] levels, Side side, Dictionary<ulong, ulong> activeDict)
    {
        // ... (Keep the previous yield return implementation here)
        if (levels == null) yield break;

        foreach (var level in levels)
        {
            ulong price = (ulong)(decimal.Parse(level[0]) * 100m);
            ulong qty = (ulong)(decimal.Parse(level[1]) * 100_000_000m);

            if (activeDict.Remove(price, out ulong oldOrderId))
            {
                yield return new EngineCommand { CancelOrder = new CancelRequest { Id = oldOrderId } };
            }

            if (qty > 0)
            {
                ulong newOrderId = _currentOrderId++;
                activeDict[price] = newOrderId;

                yield return new EngineCommand
                {
                    PlaceOrder = new OrderRequest
                    {
                        Id = newOrderId,
                        Price = price,
                        Qty = qty,
                        Side = side,
                        OrderType = OrderType.Limit,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                };
            }
        }
    }
}