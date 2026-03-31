using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Services;

public class CancelOrderCommandHandler
{
    public async Task<CommandAckResult> HandlePlaceOrderCommand(CancelOrderCommand cancelOrderCommand)
    {
        var (isValid, rejectCode, reason) = _validate(cancelOrderCommand);

        if (!isValid)
        {
            return _createResult(cancelOrderCommand, Status.Rejected, null, rejectCode, reason);
        }
        
    }

    private static (bool IsValid, RejectionCode? Code, string? Reason) _validate(CancelOrderCommand cmd)
    {
        if (cmd.AccountId <= 0)
            return (false, RejectionCode.INVALID_ACCOUNT_ID, "Account ID is invalid.");
        if (cmd.OrderId <= 0)
            return (false, RejectionCode.INVALID_OREDER_ID, "Order ID is invalid.");
        if (cmd.OrderId >> 32 != cmd.AccountId)
            return (false, RejectionCode.INVALID_OREDER_ID, "Order ID is invalid.");

        return (true, null, null);
    }

    private static CommandAckResult _createResult(
        CancelOrderCommand cmd,
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