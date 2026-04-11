using System.Globalization;
using System.Text.Json;
using MarketSimulator.Models;
using Orderbook;

namespace MarketSimulator.Services;

public class BinanceDataParser
{
    private readonly Dictionary<ulong, ulong> _activeBids = new();
    private readonly Dictionary<ulong, ulong> _activeAsks = new();
    private ulong _currentOrderId = 100_000_000;

    public int CorruptedLinesCount { get; private set; }

    public IEnumerable<EngineCommand> ParseLines(IEnumerable<string> jsonLines)
    {
        foreach (var line in jsonLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            BinanceEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<BinanceEnvelope>(line);
            }
            catch (JsonException)
            {
                CorruptedLinesCount++;
                continue;
            }

            if (envelope?.Data == null || string.IsNullOrWhiteSpace(envelope.Data.Stream))
                continue;

            var stream = envelope.Data.Stream;
            var payload = envelope.Data.Payload;

            if (stream.Contains("depth", StringComparison.OrdinalIgnoreCase))
            {
                DepthData? depth;
                try
                {
                    depth = payload.Deserialize<DepthData>();
                }
                catch (JsonException)
                {
                    CorruptedLinesCount++;
                    continue;
                }

                if (depth == null)
                    continue;

                // choose which timestamp you want:
                // depth.EventTime = Binance exchange event time
                // envelope.LocalTimestamp = your captured local time
                long timestamp = envelope.LocalTimestamp;

                foreach (var cmd in ProcessOrderBookLevels(depth.Bids, Side.Bid, _activeBids, timestamp))
                    yield return cmd;

                foreach (var cmd in ProcessOrderBookLevels(depth.Asks, Side.Ask, _activeAsks, timestamp))
                    yield return cmd;
            }
            else if (stream.Contains("aggTrade", StringComparison.OrdinalIgnoreCase))
            {
                AggTradeData? trade;
                try
                {
                    trade = payload.Deserialize<AggTradeData>();
                }
                catch (JsonException)
                {
                    CorruptedLinesCount++;
                    continue;
                }

                if (trade == null)
                    continue;

                ulong price = ParsePriceToTicks(trade.Price);
                ulong qty = ParseQtyToLots(trade.Qty);
                var side = trade.IsBuyerMaker ? Side.Ask : Side.Bid;

                yield return new EngineCommand
                {
                    PlaceOrder = new OrderRequest
                    {
                        ClientId = _currentOrderId++,
                        Price = price,
                        Qty = qty,
                        Side = side,
                        OrderType = OrderType.Market,
                        Timestamp = envelope.LocalTimestamp // or trade.EventTime
                    }
                };
            }
        }
    }

    private IEnumerable<EngineCommand> ProcessOrderBookLevels(
        string[][]? levels,
        Side side,
        Dictionary<ulong, ulong> activeDict,
        long timestamp)
    {
        if (levels == null)
            yield break;

        foreach (var level in levels)
        {
            if (level == null || level.Length < 2)
                continue;

            ulong price = ParsePriceToTicks(level[0]);
            ulong qty = ParseQtyToLots(level[1]);

            if (activeDict.Remove(price, out ulong oldOrderId))
            {
                yield return new EngineCommand
                {
                    CancelOrder = new CancelRequest
                    {
                        EngineOrderId = oldOrderId,
                        Timestamp = timestamp
                    }
                };
            }

            if (qty > 0)
            {
                ulong newOrderId = _currentOrderId++;
                activeDict[price] = newOrderId;

                yield return new EngineCommand
                {
                    PlaceOrder = new OrderRequest
                    {
                        ClientId = newOrderId,
                        Price = price,
                        Qty = qty,
                        Side = side,
                        OrderType = OrderType.Limit,
                        Timestamp = timestamp
                    }
                };
            }
        }
    }

    private static ulong ParsePriceToTicks(string priceText)
    {
        decimal price = decimal.Parse(priceText, CultureInfo.InvariantCulture);
        return (ulong)(price * 100m);
    }

    private static ulong ParseQtyToLots(string qtyText)
    {
        decimal qty = decimal.Parse(qtyText, CultureInfo.InvariantCulture);
        return (ulong)(qty * 100_000_000m);
    }
}