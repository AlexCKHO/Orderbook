namespace Trading.Oms.Api.Contracts;

public record CancelOrderRequest(
    uint AccountId,
    ulong ClientOrderId,
    ulong EngineOrderId
);