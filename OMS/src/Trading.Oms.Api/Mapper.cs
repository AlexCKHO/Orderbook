using Trading.Oms.Api.Contracts;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Models;

namespace Trading.Oms.Api;

public class Mapper
{
    public static CancelOrderCommand MapToCancelOrderCommand(CancelOrderRequest request, RequestMetadata metadata)
    {
        return new CancelOrderCommand(request.AccountId, request.OrderId, metadata.RequestId, metadata.CorrelationId,
            metadata.IdempotencyKey,
            metadata.SubmittedAtUtc);
    }

    public static PlaceOrderCommand MapToPlaceOrderCommand(PlaceOrderRequest request, RequestMetadata metadata)
    {
        return new PlaceOrderCommand(request.AccountId, request.Symbol, request.Side, request.OrderType,
            request.Quantity, request.Price, metadata.RequestId, metadata.CorrelationId,
            metadata.IdempotencyKey,
            metadata.SubmittedAtUtc);
    }
}