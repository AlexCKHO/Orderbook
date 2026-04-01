using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Services;

public class CancelOrderCommandHandler
{
    private IMatchingEngineClient _matchingEngineClient;

    public async Task<CommandAckResult> HandleCancelOrderCommand(CancelOrderCommand cmd)
    {
        var (isValid, rejectCode, reason) = _validate(cmd);

        if (!isValid)
        {
            return _createResult(cmd, Status.Rejected, rejectCode, reason);
        }

        var result = await _matchingEngineClient.CancelOrderCommand(cmd);

        return _createResult(cmd, result.Status, result.RejectionCode, result.RejectionReason);
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
        RejectionCode? code = null,
        string? reason = null)
    {
        return new CommandAckResult(
            requestId: cmd.RequestId,
            correlationId: cmd.CorrelationId,
            idempotencyKey: cmd.IdempotencyKey,
            commandType: CommandType.PlaceOrder,
            status: status,
            orderId: cmd.OrderId,
            rejectionCode: code,
            rejectionReason: reason,
            receivedAtUtc: cmd.SubmittedAtUtc
        );
    }
}