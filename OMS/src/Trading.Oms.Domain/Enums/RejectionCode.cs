namespace Trading.Oms.Domain.Enums;

public enum RejectionCode
{
    InvalidSymbol = 1,
    OrderNotFound = 2,
    InvalidOrderType = 3,
    PriceRequired = 4,
    EngineUnavailable = 5,
    InvalidAccountId = 6,
    InvalidOrderId = 7,
    InvalidQuantity = 8,
    InvalidSide = 9,
    InvalidIdempotencyKey = 10,
    PriceNotAllowedForMarket = 11
}