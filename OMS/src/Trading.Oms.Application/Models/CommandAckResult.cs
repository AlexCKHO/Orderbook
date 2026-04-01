using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Models;

public class CommandAckResult
{
    public string RequestId { get; }
    public string CorrelationId { get; }
    public string IdempotencyKey { get; }
    public CommandType CommandType { get; }
    public Status Status { get; }
    public ulong? OrderId { get; }
    public RejectionCode? RejectionCode { get; }
    public string? RejectionReason { get; }
    public DateTimeOffset ReceivedAtUtc { get; }

    public CommandAckResult(string requestId, string correlationId, string idempotencyKey, CommandType commandType,
        Status status, ulong? orderId, RejectionCode? rejectionCode, string? rejectionReason,
        DateTimeOffset receivedAtUtc)
    {
        RequestId = requestId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        CommandType = commandType;
        Status = status;
        OrderId = orderId;
        RejectionCode = rejectionCode;
        RejectionReason = rejectionReason;
        ReceivedAtUtc = receivedAtUtc;
    }
}