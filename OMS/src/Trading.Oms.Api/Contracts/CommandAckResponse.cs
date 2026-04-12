using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Api.Contracts;

public record CommandAckResponse(
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    CommandType CommandType,
    Status Status,
    ulong? ClientOrderId,
    ulong? EngineOrderId,
    RejectionCode? RejectionCode,
    string? RejectionReason,
    DateTimeOffset ReceivedAtUtc
);