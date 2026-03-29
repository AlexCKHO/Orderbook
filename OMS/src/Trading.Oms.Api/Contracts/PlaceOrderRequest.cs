namespace Trading.Oms.Api.Contracts;

public enum Side
{
    Bid = 1,
    Ask = 2
}

public enum OrderType
{
    Limit = 1,
    Market = 2
}

public sealed record PlaceOrderRequest(
    uint AccountId,
    string Symbol,
    Side Side,
    OrderType OrderType,
    ulong Quantity,
    ulong? Price
);