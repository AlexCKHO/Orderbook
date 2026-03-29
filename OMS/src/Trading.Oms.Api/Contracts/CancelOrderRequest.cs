namespace Trading.Oms.Api.Contracts;

public record CancelOrderRequest(
    uint AccountId,
    ulong OrderId
);