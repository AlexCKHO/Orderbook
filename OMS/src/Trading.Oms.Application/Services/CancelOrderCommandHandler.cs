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
    IHashingService hashingService,
    ICommandAuditRepository commandAuditRepository) : ICancelOrderCommandHandler
{
    private readonly IMatchingEngineClient _matchingEngineClient = matchingEngineClient;
    private readonly IIdempotencyRepository _idempotencyRepository = idempotencyRepository;
    private readonly IHashingService _hashingService = hashingService;
    private readonly ICommandAuditRepository _commandAuditRepository = commandAuditRepository;


    public async Task<CommandAckResult> HandleAsync(CancelOrderCommand cmd, CancellationToken token)
    {
        const string scope = "POST:/api/orders/cancel";

        var (isValid, rejectCode, reason) = _validate(cmd);

        if (!isValid)
        {
            return _createResult(cmd, Status.Rejected, rejectCode, reason);
        }

        // 2. Idempotency Check

        var currentCmdHash = _hashingService.HashCancelOrderCommand(cmd.AccountId, cmd.ClientOrderId);

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

        var commandAudit = new CommandAudit()
        {
            RequestId = cmd.RequestId,
            CorrelationId = cmd.CorrelationId,
            IdempotencyKey = cmd.IdempotencyKey,
            ClientOrderId = (long)cmd.ClientOrderId,
            EngineOrderId = (long)cmd.EngineOrderId,
            AccountId = cmd.AccountId,
            CommandType = CommandType.CancelOrder,
            PayloadHash = currentCmdHash,
            RequestPayloadJson = JsonSerializer.Serialize(cmd),
            Status = Status.Received,
            SubmittedAtUtc = cmd.SubmittedAtUtc,
        };


        // 4. Execution

        try
        {
            await _commandAuditRepository.InsertReceivedAsync(commandAudit, token);

            var engineResult = await _matchingEngineClient.CancelOrderCommand(cmd);

            var completedAt = DateTimeOffset.UtcNow;

            var finalResult = _createResult(cmd, engineResult.Status, engineResult.RejectionCode,
                engineResult.RejectionReason);

            // 5. Completion


            await _idempotencyRepository.CompleteAsync(
                reserve.Scope,
                reserve.AccountId,
                reserve.IdempotencyKey,
                _mapStatusToStatusCode(finalResult.Status),
                JsonSerializer.Serialize(finalResult),
                completedAt,
                token
            );
            await _commandAuditRepository.CompletedAsync(cmd.RequestId, engineResult.Status,
                (long)cmd.ClientOrderId, (long)cmd.EngineOrderId,
                engineResult.RejectionCode, engineResult.RejectionReason, completedAt, token);


            return finalResult;
        }
        catch (Exception ex)
        {
            await _idempotencyRepository.FailAsync(reserve.Scope,
                reserve.AccountId,
                reserve.IdempotencyKey,
                _mapStatusToStatusCode(Status.Unknown),
                DateTimeOffset.UtcNow,
                token);

            await _commandAuditRepository.MarkFailedAsync(cmd.RequestId, Status.Failed, cmd.ClientOrderId, ex.Message,
                DateTimeOffset.UtcNow, token);

            throw;
        }
    }

    private static (bool IsValid, RejectionCode? Code, string? Reason) _validate(CancelOrderCommand cmd)
    {
        if (cmd.AccountId <= 0)
            return (false, RejectionCode.InvalidAccountId, "Account ID is invalid.");
        if (cmd.ClientOrderId == 0)
            return (false, RejectionCode.InvalidOrderId, "Order ID is invalid.");
        if (cmd.ClientOrderId >> 32 != cmd.AccountId)
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
            clientOrderId: cmd.ClientOrderId,
            engineOrderId: cmd.EngineOrderId,
            rejectionCode: code,
            rejectionReason: reason,
            receivedAtUtc: cmd.SubmittedAtUtc
        );
    }
}