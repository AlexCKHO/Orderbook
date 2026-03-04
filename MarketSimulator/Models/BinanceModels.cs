using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.IO;
using Orderbook;


namespace MarketSimulator.Models;

public class BinanceMessage
{
    [JsonPropertyName("stream")] public string Stream { get; set; }

    [JsonPropertyName("data")] public JsonElement Data { get; set; }
}

public class DepthData
{
    [JsonPropertyName("b")] public string[][] Bids { get; set; }

    [JsonPropertyName("a")] public string[][] Asks { get; set; }
}

public class AggTradeData
{
    [JsonPropertyName("p")] public string Price { get; set; }

    [JsonPropertyName("q")] public string Qty { get; set; }

    [JsonPropertyName("m")] public bool IsBuyerMaker { get; set; }
}