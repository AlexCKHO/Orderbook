using Trading.Oms.Application.Models;

namespace Trading.Oms.Application.Interfaces;

public interface IIdempotencyStore
{
    public Task<IdempotencyRecord?> GetAsync(string scope, uint accountId, string idempotencyKey,
        CancellationToken token);

    public void ReserveAsync(IdempotencyReservation reservation, CancellationToken token);

    public Task CompleteAsync(string scope, uint accountId, string idempotencyKey, int responseStatusCode,
        string responseJson,
        DateTimeOffset completeAtUtc, CancellationToken token);
}