using Trading.Oms.Application.Models;

namespace Trading.Oms.Application.Interfaces;

public interface IIdempotencyStore
{
    public Task<IdempotencyRecord?> GetAsync(string scope, uint accountId, string idempotencyKey,
        CancellationToken token);

    public Task ReserveAsync(IdempotencyReservation reservation, CancellationToken token);

    public Task CompleteAsync(string scope, uint accountId, string idempotencyKey, int? responseStatusCode,
        string responseJson,
        DateTimeOffset completeAtUtc, CancellationToken token);

    public Task FailAsync(string scope, uint accountId, string idempotencyKey, int? responseStatusCode,
        DateTimeOffset completeAtUtc, CancellationToken token);
}