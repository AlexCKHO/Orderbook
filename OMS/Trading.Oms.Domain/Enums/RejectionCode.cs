namespace Trading.Oms.Domain.Enums;

public enum RejectionCode
{
    INVALID_SYMBOL = 1,
    ORDER_NOT_FOUND = 2,
    INVALID_ORDER_TYPE = 3,
    PRICE_REQUIRED = 4,
    ENGINE_UNAVAILABLE = 5
}