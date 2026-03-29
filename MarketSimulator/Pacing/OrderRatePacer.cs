using System.Diagnostics;

namespace MarketSimulator.Pacing;

public class OrderRatePacer
{
    private readonly double _ordersPerSecond;
    private readonly long _startTicks;
    private long _sentOrders;

    public OrderRatePacer(double ordersPerSecond)
    {
        if (ordersPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(ordersPerSecond), "Random order rate must be > 0.");

        _ordersPerSecond = ordersPerSecond;
        _startTicks = Stopwatch.GetTimestamp();
    }

    public async Task WaitAsync(int sentCount)
    {
        _sentOrders += sentCount;

        double targetElapsedMs = _sentOrders * 1000.0 / _ordersPerSecond;

        while (true)
        {
            double actualElapsedMs =
                (Stopwatch.GetTimestamp() - _startTicks) * 1000.0 / Stopwatch.Frequency;

            double remainingMs = targetElapsedMs - actualElapsedMs;

            if (remainingMs <= 0)
                return;

            if (remainingMs > 2)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, remainingMs - 1)));
            else
                Thread.SpinWait(500);
        }
    }
}