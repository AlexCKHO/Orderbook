using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Commands;


public class PlaceOrderCommand
{
    public uint AccountId { get; }

    // public uint OrderId { get; }
    public  string Symbol { get; }
    public  Side Side { get; }
    public  OrderType OrderType { get; }
    public  ulong Quantity { get; }
    public  ulong? Price { get; }
    public  string RequestId { get; }
    public  string CorrelationId { get; }
    public  string IdempotencyKey { get; }
    public  DateTimeOffset SubmittedAtUtc { get; }


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