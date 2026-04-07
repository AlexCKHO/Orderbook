using System.Text.Json;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Exceptions;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Services;

public class CancelOrderCommandHandler(
    IMatchingEngineClient matchingEngineClient,
    IIdempotencyRepository idempotencyRepository,
    IHashingService hashingService) : ICancelOrderCommandHandler
{
    private readonly IMatchingEngineClient _matchingEngineClient = matchingEngineClient;
    private readonly IIdempotencyRepository _idempotencyRepository = idempotencyRepository;
    private readonly IHashingService _hashingService = hashingService;


    public async Task<CommandAckResult> HandleAsync(CancelOrderCommand cmd, CancellationToken token)
    {
        const string scope = "POST:/api/orders/cancel";

        var (isValid, rejectCode, reason) = _validate(cmd);

        if (!isValid)
        {
            return _createResult(cmd, Status.Rejected, rejectCode, reason);
        }

        // 2. Idempotency Check

        var currentCmdHash = _hashingService.HashCancelOrderCommand(cmd.AccountId, cmd.OrderId);

        var record = await _idempotencyRepository.GetAsync(scope, cmd.AccountId, cmd.IdempotencyKey, token);

        if (record is not null)
        {
            return _handleExistingIdempotency(record, currentCmdHash);
        }

        // 3. Reservation
        var reserve = new IdempotencyReservation
        {
            Scope = scope,
            AccountId = cmd.AccountId,
            IdempotencyKey = cmd.IdempotencyKey,
            RequestId = cmd.RequestId,
            RequestHash = currentCmdHash,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(24)
        };

        await _idempotencyRepository.ReserveAsync(reserve, token);

        // 4. Execution

        try
        {
            var engineResult = await _matchingEngineClient.CancelOrderCommand(cmd);

            var finalResult = _createResult(cmd, engineResult.Status, engineResult.RejectionCode,
                engineResult.RejectionReason);

            // 5. Completion
            await _idempotencyRepository.CompleteAsync(
                reserve.Scope,
                reserve.AccountId,
                reserve.IdempotencyKey,
                _mapStatusToStatusCode(finalResult.Status),
                JsonSerializer.Serialize(finalResult),
                DateTimeOffset.UtcNow,
                token
            );

            return finalResult;
        }
        catch (Exception)
        {
            await _idempotencyRepository.FailAsync(reserve.Scope,
                reserve.AccountId,
                reserve.IdempotencyKey,
                _mapStatusToStatusCode(Status.Unknown),
                DateTimeOffset.UtcNow,
                token);

            throw;
        }
    }

    private static (bool IsValid, RejectionCode? Code, string? Reason) _validate(CancelOrderCommand cmd)
    {
        if (cmd.AccountId <= 0)
            return (false, RejectionCode.InvalidAccountId, "Account ID is invalid.");
        if (cmd.OrderId == 0)
            return (false, RejectionCode.InvalidOrderId, "Order ID is invalid.");
        if (cmd.OrderId >> 32 != cmd.AccountId)
            return (false, RejectionCode.InvalidOrderId, "Order ID is invalid.");

        return (true, null, null);
    }

    private static int _mapStatusToStatusCode(Status status) => status switch
    {
        Status.Submitted => 200,
        Status.Rejected => 400,
        _ => 500
    };

    private static CommandAckResult _handleExistingIdempotency(IdempotencyRecord record, string currentHash)
    {
        if (!currentHash.Equals(record.RequestHash))
        {
            // possible malicious attack
            throw new IdempotencyConflictException(
                "Idempotency key reused with a different payload.");
        }

        if (record.State == IdempotencyStates.InProgress)
        {
            throw new IdempotencyConflictException("Order is currently being processed.");
        }

        if (record.State == IdempotencyStates.Failed)
        {
            throw new IdempotencyConflictException("Previous request failed; retry with a new idempotency key.");
        }

        if (record.State == IdempotencyStates.Completed)
        {
            if (string.IsNullOrWhiteSpace(record.ResponseJson))
            {
                throw new IdempotencyConflictException(
                    "Failed to deserialize the cached idempotency result.");
            }

            return JsonSerializer
                       .Deserialize<CommandAckResult>(record.ResponseJson) ??
                   throw new IdempotencyConflictException("Failed to deserialize the cached idempotency result.");
        }

        if (string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            throw new IdempotencyConflictException($"Record found in {record.State} state but missing response data.");
        }

        throw new IdempotencyConflictException($"Unknown idempotency state: {record.State}");
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
            commandType: CommandType.CancelOrder,
            status: status,
            orderId: cmd.OrderId,
            rejectionCode: code,
            rejectionReason: reason,
            receivedAtUtc: cmd.SubmittedAtUtc
        );
    }
}