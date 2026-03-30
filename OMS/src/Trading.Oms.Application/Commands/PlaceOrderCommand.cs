using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Commands;


public class PlaceOrderCommand
{
    uint AccountId { get; }

    // uint OrderId { get; }
    string Symbol { get; }
    Side Side { get; }
    OrderType OrderType { get; }
    ulong Quantity { get; }
    ulong? Price { get; }
    string RequestId { get; }
    string CorrelationId { get; }
    string IdempotencyKey { get; }
    DateTimeOffset SubmittedAtUtc { get; }


    public PlaceOrderCommand(uint accountId, string symbol, Side side, OrderType orderType, ulong quantity,
        ulong? price, string requestId, string correlationId, string idempotencyKey, DateTimeOffset submittedAtUtc)
    {
        AccountId = accountId;
        Symbol = symbol;
        Side = side;
        OrderType = orderType;
        Quantity = quantity;
        Price = price;
        RequestId = requestId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        SubmittedAtUtc = submittedAtUtc;
    }
}