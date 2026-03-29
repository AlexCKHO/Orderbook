namespace MarketSimulator.Models;

public class MarketSimulatorSettings
{
    public int NoOfOrderToTest { get; set; }
    public int TestRepeat { get; set; }
    public string gRPCAddress { get; set; } = "";
    public int Concurrency { get; set; }
    public int BatchSize { get; set; }
    public bool UseHistorical { get; set; }
    public bool EnableOrderPacing { get; set; }
    public int OrderRatePerSecond { get; set; }
    public bool EnableHistoricalPacing { get; set; }
    public double ReplaySpeed { get; set; }

    public double PerStreamRate => Math.Max(1.0, OrderRatePerSecond / (double)Concurrency);
}