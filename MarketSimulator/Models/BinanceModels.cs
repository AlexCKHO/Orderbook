using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketSimulator.Models;

public class BinanceEnvelope
{
    [JsonPropertyName("local_ts")] public long LocalTimestamp { get; set; }

    [JsonPropertyName("data")] public BinanceMessage Data { get; set; }
}

public class BinanceMessage
{
    [JsonPropertyName("stream")] public string Stream { get; set; }

    [JsonPropertyName("data")] public JsonElement Payload { get; set; }
}

public class DepthData
{
    [JsonPropertyName("e")] public string EventType { get; set; }

    [JsonPropertyName("E")] public long EventTime { get; set; }

    [JsonPropertyName("s")] public string Symbol { get; set; }

    [JsonPropertyName("U")] public long FirstUpdateId { get; set; }

    [JsonPropertyName("u")] public long FinalUpdateId { get; set; }

    [JsonPropertyName("b")] public string[][] Bids { get; set; }

    [JsonPropertyName("a")] public string[][] Asks { get; set; }
}

public class AggTradeData
{
    [JsonPropertyName("e")] public string EventType { get; set; }

    [JsonPropertyName("E")] public long EventTime { get; set; }

    [JsonPropertyName("s")] public string Symbol { get; set; }

    [JsonPropertyName("p")] public string Price { get; set; }

    [JsonPropertyName("q")] public string Qty { get; set; }

    [JsonPropertyName("m")] public bool IsBuyerMaker { get; set; }
}