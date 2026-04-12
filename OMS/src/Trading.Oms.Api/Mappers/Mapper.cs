using Trading.Oms.Api.Contracts;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Api.Mappers;

public class Mapper
{
    public static CancelOrderCommand MapToCancelOrderCommand(CancelOrderRequest request, RequestMetadata metadata)
    {
        return new CancelOrderCommand(request.AccountId, request.ClientOrderId, request.EngineOrderId,
            metadata.RequestId,
            metadata.CorrelationId,
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

    public static CommandAckResponse MapToPlaceOrderCommandAckResult(
        PlaceOrderCommand cmd,
        CommandAckResult result)
    {
        return new CommandAckResponse(
            RequestId: cmd.RequestId,
            CorrelationId: cmd.CorrelationId,
            IdempotencyKey: cmd.IdempotencyKey,
            CommandType: result.CommandType,
            Status: result.Status,
            ClientOrderId: result.ClientOrderId,
            EngineOrderId: result.EngineOrderId,
            RejectionCode: result.RejectionCode,
            RejectionReason: result.RejectionReason,
            ReceivedAtUtc: result.ReceivedAtUtc
        );
    }

    public static CommandAckResponse MapToCancelOrderCommandAckResult(
        CancelOrderCommand cmd,
        CommandAckResult result)
    {
        return new CommandAckResponse(
            RequestId: cmd.RequestId,
            CorrelationId: cmd.CorrelationId,
            IdempotencyKey: cmd.IdempotencyKey,
            CommandType: result.CommandType,
            Status: result.Status,
            ClientOrderId: result.ClientOrderId,
            EngineOrderId: cmd.EngineOrderId,
            RejectionCode: result.RejectionCode,
            RejectionReason: result.RejectionReason,
            ReceivedAtUtc: result.ReceivedAtUtc
        );
    }
}