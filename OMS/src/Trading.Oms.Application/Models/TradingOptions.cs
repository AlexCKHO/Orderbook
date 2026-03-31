namespace Trading.Oms.Application.Models;

public class TradingOptions
{
    public static HashSet<string> Symbols { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "BTC-USD",
        "ETH-USD",
        "SOL-USD"
    };
}