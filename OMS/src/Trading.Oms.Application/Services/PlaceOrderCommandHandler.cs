using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Services;

public class PlaceOrderCommandHandler
{
    public PlaceOrderResult HandlePlaceOrderCommand(PlaceOrderCommand placeOrderCommand)
    {
        if (placeOrderCommand.AccountId <= 0)
        {
            return new PlaceOrderResult(
                requestId: placeOrderCommand.RequestId,
                correlationId: placeOrderCommand.CorrelationId,
                idempotencyKey: placeOrderCommand.IdempotencyKey,
                commandType: CommandType.PlaceOrder,
                status: Status.Rejected,
                orderId:,
                rejectionCode: RejectionCode.INVALID_ACCOUNT_ID,
                rejectionReason: "Account ID is invalid",
                receivedAtUtc: placeOrderCommand.SubmittedAtUtc
            );
        }

        if (!TradingOptions.Symbols.Contains(placeOrderCommand.Symbol))
        {
            return new PlaceOrderResult(
                requestId: placeOrderCommand.RequestId,
                correlationId: placeOrderCommand.CorrelationId,
                idempotencyKey: placeOrderCommand.IdempotencyKey,
                commandType: CommandType.PlaceOrder,
                status: Status.Rejected,
                orderId:,
                rejectionCode: RejectionCode.INVALID_ACCOUNT_ID,
                rejectionReason: "Account ID is invalid",
                receivedAtUtc: placeOrderCommand.SubmittedAtUtc
            );
        }

        if (placeOrderCommand.Quantity <= 0)
        {
            return new PlaceOrderResult(
                requestId: placeOrderCommand.RequestId,
                correlationId: placeOrderCommand.CorrelationId,
                idempotencyKey: placeOrderCommand.IdempotencyKey,
                commandType: CommandType.PlaceOrder,
                status: Status.Rejected,
                orderId:,
                rejectionCode: RejectionCode.INVALID_QUANTITY,
                rejectionReason: "Quantity cannot be less than or equal to zero",
                receivedAtUtc: placeOrderCommand.SubmittedAtUtc
            );
        }

        if (placeOrderCommand is { OrderType: not (OrderType.Limit or OrderType.Market) })
        {
            return new PlaceOrderResult(
                requestId: placeOrderCommand.RequestId,
                correlationId: placeOrderCommand.CorrelationId,
                idempotencyKey: placeOrderCommand.IdempotencyKey,
                commandType: CommandType.PlaceOrder,
                status: Status.Rejected,
                orderId:,
                rejectionCode: RejectionCode.INVALID_ORDER_TYPE,
                rejectionReason: "Side need to be either Ask or Bid",
                receivedAtUtc: placeOrderCommand.SubmittedAtUtc
            );
        }


        if (placeOrderCommand is { OrderType: OrderType.Limit, Price: null or >= 0 })
        {
            return new PlaceOrderResult(
                requestId: placeOrderCommand.RequestId,
                correlationId: placeOrderCommand.CorrelationId,
                idempotencyKey: placeOrderCommand.IdempotencyKey,
                commandType: CommandType.PlaceOrder,
                status: Status.Rejected,
                orderId:,
                rejectionCode: RejectionCode.INVALID_QUANTITY,
                rejectionReason: "Quantity cannot be less than or equal to zero",
                receivedAtUtc: placeOrderCommand.SubmittedAtUtc
            );
        }


        if (placeOrderCommand is { Side: not (Side.Ask or Side.Bid) })
        {
            return new PlaceOrderResult(
                requestId: placeOrderCommand.RequestId,
                correlationId: placeOrderCommand.CorrelationId,
                idempotencyKey: placeOrderCommand.IdempotencyKey,
                commandType: CommandType.PlaceOrder,
                status: Status.Rejected,
                orderId:,
                rejectionCode: RejectionCode.INVALID_SIDE,
                rejectionReason: "Side need to be either Ask or Bid",
                receivedAtUtc: placeOrderCommand.SubmittedAtUtc
            );
        }


        return new PlaceOrderResult(
            requestId: placeOrderCommand.RequestId,
            correlationId: placeOrderCommand.CorrelationId,
            idempotencyKey: placeOrderCommand.IdempotencyKey,
            commandType: CommandType.PlaceOrder,
            status: Status.Submitted,
            orderId:,
            rejectionCode: null,
            rejectionReason: null,
            receivedAtUtc: placeOrderCommand.SubmittedAtUtc
        );
    }
}