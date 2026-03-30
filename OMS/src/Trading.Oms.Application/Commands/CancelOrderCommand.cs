namespace Trading.Oms.Application.Commands;

public class CancelOrderCommand
{
    uint AccountId { get; }
    ulong OrderId { get; }
    string RequestId { get; }
    string CorrelationId { get; }
    string IdempotencyKey { get; }
    DateTimeOffset SubmittedAtUtc { get; }

    public CancelOrderCommand(uint accountId, ulong orderId, string requestId, string correlationId,
        string idempotencyKey, DateTimeOffset submittedAtUtc)
    {
        AccountId = accountId;
        OrderId = orderId;
        RequestId = requestId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        SubmittedAtUtc = submittedAtUtc;
    }
}