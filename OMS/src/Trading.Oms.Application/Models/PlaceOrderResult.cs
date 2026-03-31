using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Models;

public class PlaceOrderResult
{
    private string RequestId { get; }
    string CorrelationId { get; }
    string IdempotencyKey { get; }
    CommandType CommandType { get; }
    Status Status { get; }
    ulong OrderId { get; }
    RejectionCode? RejectionCode { get; }
    string? RejectionReason { get; }
    DateTimeOffset ReceivedAtUtc { get; }

    public PlaceOrderResult(string requestId, string correlationId, string idempotencyKey, CommandType commandType,
        Status status, ulong orderId, RejectionCode? rejectionCode, string? rejectionReason,
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