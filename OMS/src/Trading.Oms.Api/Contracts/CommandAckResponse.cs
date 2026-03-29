namespace Trading.Oms.Api.Contracts;

public enum RejectionCode
{
    INVALID_SYMBOL = 1,
    ORDER_NOT_FOUND = 2,
    INVALID_ORDER_TYPE = 3,
    PRICE_REQUIRED = 4,
    ENGINE_UNAVAILABLE = 5
}

public enum Status
{
    Unknown = 0,
    Submitted = 1,
    Rejected = 2
}

public enum CommandType
{

    PlaceOrder = 1,
    CancelOrder = 2
}


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