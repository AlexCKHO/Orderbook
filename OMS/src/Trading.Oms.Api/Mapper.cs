using Trading.Oms.Api.Contracts;
using Trading.Oms.Application.Commands;

namespace Trading.Oms.Api;

public class Mapper
{
    public static CancelOrderCommand MapToCancelOrderCommand(CancelOrderRequest request, string requestId,
        string correlationId, string idempotencyKey, DateTimeOffset submittedAtUtc)
    {
        return new CancelOrderCommand(request.AccountId, request.OrderId, requestId, correlationId, idempotencyKey,
            submittedAtUtc);
    }

    public static PlaceOrderCommand MapToPlaceOrderCommand(PlaceOrderRequest request, string requestId,
        string correlationId, string idempotencyKey, DateTimeOffset submittedAtUtc)
    {
        return new PlaceOrderCommand(request.AccountId, request.Symbol, request.Side, request.OrderType,
            request.Quantity, request.Price, requestId, correlationId, idempotencyKey, submittedAtUtc);
    }
}