using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Api.Contracts;

public record CommandAckResponse(
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    CommandType CommandType,
    Status Status,
    ulong OrderId,
    RejectionCode? RejectionCode,
    string? RejectionReason,
    DateTimeOffset ReceivedAtUtc
);