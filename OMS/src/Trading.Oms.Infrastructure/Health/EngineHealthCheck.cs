using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orderbook;

namespace Trading.Oms.Infrastructure.Health;

public class EngineHealthCheck(MatchingEngine.MatchingEngineClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return HealthCheckResult.Healthy("Engine is reachable");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Engine unreachable");
        }
    }
}