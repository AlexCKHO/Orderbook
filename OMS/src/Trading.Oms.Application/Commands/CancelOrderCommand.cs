namespace Trading.Oms.Application.Commands;

public class CancelOrderCommand
{
    public uint AccountId { get; }
    public ulong OrderId { get; }
    public string RequestId { get; }
    public string CorrelationId { get; }
    public string IdempotencyKey { get; }
    public DateTimeOffset SubmittedAtUtc { get; }

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