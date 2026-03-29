using System.Diagnostics;

namespace MarketSimulator.Pacing;

public sealed class TimestampRatePacer
{
    private readonly double _speed;
    private long? _firstHistoricalTimestampMs;
    private long _replayStartTicks;
    private long _lastHistoricalTimestampMs;

    public TimestampRatePacer(double speed)
    {
        if (speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed), "Replay speed must be > 0.");

        _speed = speed;
    }

    public async Task WaitAsync(long? historicalTimestampMs)
    {
        if (!historicalTimestampMs.HasValue || historicalTimestampMs.Value <= 0)
            return;

        long ts = historicalTimestampMs.Value;

        if (!_firstHistoricalTimestampMs.HasValue)
        {
            _firstHistoricalTimestampMs = ts;
            _lastHistoricalTimestampMs = ts;
            _replayStartTicks = Stopwatch.GetTimestamp();
            return;
        }

        if (ts < _lastHistoricalTimestampMs)
            ts = _lastHistoricalTimestampMs;

        _lastHistoricalTimestampMs = ts;

        double historicalElapsedMs = ts - _firstHistoricalTimestampMs.Value;
        double targetReplayElapsedMs = historicalElapsedMs / _speed;

        while (true)
        {
            double actualReplayElapsedMs =
                (Stopwatch.GetTimestamp() - _replayStartTicks) * 1000.0 / Stopwatch.Frequency;

            double remainingMs = targetReplayElapsedMs - actualReplayElapsedMs;

            if (remainingMs <= 0)
                return;

            if (remainingMs > 2)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, remainingMs - 1)));
            else
                Thread.SpinWait(500);
        }
    }
}