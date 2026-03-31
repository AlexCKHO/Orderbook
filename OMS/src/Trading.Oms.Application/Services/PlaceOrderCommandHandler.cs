using Trading.Oms.Api.Oms.Domain.Interface;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Services;

public class PlaceOrderCommandHandler
{
    private IOrderSequenceAllocator _orderSequenceAllocator;
    private IOrderIdComposer _orderIdComposer;


    public async Task<CommandAckResult> HandlePlaceOrderCommand(PlaceOrderCommand placeOrderCommand)
    {
        var (isValid, rejectCode, reason) = _validate(placeOrderCommand);

        if (!isValid)
        {
            return _createResult(placeOrderCommand, Status.Rejected, null, rejectCode, reason);
        }

        var sequence = await _orderSequenceAllocator.AllocateNextSequenceForAccount(placeOrderCommand.AccountId);
        var orderId = _orderIdComposer.Compose(placeOrderCommand.AccountId, sequence);


        return _createResult(placeOrderCommand, Status.Submitted, orderId);
    }

    private static (bool IsValid, RejectionCode? Code, string? Reason) _validate(PlaceOrderCommand cmd)
    {
        if (cmd.AccountId <= 0)
            return (false, RejectionCode.INVALID_ACCOUNT_ID, "Account ID must be positive.");

        if (!TradingOptions.Symbols.Contains(cmd.Symbol))
            return (false, RejectionCode.INVALID_SYMBOL, $"Symbol {cmd.Symbol} is not supported.");

        if (cmd.Quantity <= 0)
            return (false, RejectionCode.INVALID_QUANTITY, "Quantity must be greater than zero.");

        if (cmd is { Side: not (Side.Ask or Side.Bid) })
            return (false, RejectionCode.INVALID_SIDE, "Side must be either Ask or Bid.");

        if (cmd is { OrderType: not (OrderType.Limit or OrderType.Market) })
            return (false, RejectionCode.INVALID_ORDER_TYPE, "Order type must be either Limit or Market.");

        if (cmd is { OrderType: OrderType.Limit, Price: null or <= 0 })
            return (false, RejectionCode.PRICE_REQUIRED, "Limit orders must have a positive price.");

        return (true, null, null);
    }

    private static CommandAckResult _createResult(
        PlaceOrderCommand cmd,
        Status status,
        ulong? orderId = null,
        RejectionCode? code = null,
        string? reason = null)
    {
        return new CommandAckResult(
            requestId: cmd.RequestId,
            correlationId: cmd.CorrelationId,
            idempotencyKey: cmd.IdempotencyKey,
            commandType: CommandType.PlaceOrder,
            status: status,
            orderId: orderId,
            rejectionCode: code,
            rejectionReason: reason,
            receivedAtUtc: cmd.SubmittedAtUtc
        );
    }
}