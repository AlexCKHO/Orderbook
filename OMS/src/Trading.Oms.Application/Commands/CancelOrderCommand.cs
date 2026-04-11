using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;

namespace Trading.Oms.Application.Commands;

public class CancelOrderCommand
{
    public uint AccountId { get; }
    public ulong ClientOrderId { get; }
    public ulong EngineOrderId { get; }
    public string RequestId { get; }
    public string CorrelationId { get; }
    public string IdempotencyKey { get; }
    public DateTimeOffset SubmittedAtUtc { get; }

    public CancelOrderCommand(uint accountId, ulong clientOrderId, ulong engineOrderId, string requestId,
        string correlationId,
        string idempotencyKey, DateTimeOffset submittedAtUtc)
    {
        AccountId = accountId;
        ClientOrderId = clientOrderId;
        EngineOrderId = engineOrderId;
        RequestId = requestId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        SubmittedAtUtc = submittedAtUtc;
    }
}