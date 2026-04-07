using System.Text.Json;
using Trading.Oms.Api.Oms.Domain.Interface;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Exceptions;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Services;

public class PlaceOrderCommandHandler(
    IOrderSequenceAllocator orderSequenceAllocator,
    IOrderIdComposer orderIdComposer,
    IMatchingEngineClient matchingEngineClient,
    IIdempotencyRepository idempotencyRepository,
    IHashingService hashingService
) : IPlaceOrderCommandHandler
{
    private readonly IOrderSequenceAllocator _orderSequenceAllocator = orderSequenceAllocator;
    private readonly IOrderIdComposer _orderIdComposer = orderIdComposer;
    private readonly IMatchingEngineClient _matchingEngineClient = matchingEngineClient;
    private readonly IIdempotencyRepository _idempotencyRepository = idempotencyRepository;
    private readonly IHashingService _hashingService = hashingService;

    public async Task<CommandAckResult> HandleAsync(PlaceOrderCommand cmd, CancellationToken token)
    {
        // not valid → return result
        // replay → return result
        // success → return result
        // exception → throw

        const string scope = "POST:/api/orders";

        // 1. Initial Validation Guard
        var (isValid, rejectCode, reason) = _validate(cmd);
        if (!isValid)
        {
            return _createResult(cmd, Status.Rejected, null, rejectCode, reason);
        }

        // 2. Idempotency Check
        var currentCmdHash = _hashingService.HashPlaceOrderCommand(
            cmd.AccountId, cmd.Symbol, cmd.Side, cmd.OrderType, cmd.Quantity, cmd.Price);

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
            var sequence = await _orderSequenceAllocator.AllocateNextSequenceForAccount(cmd.AccountId);
            var orderId = _orderIdComposer.Compose(cmd.AccountId, sequence);

            var engineResult = await _matchingEngineClient.PlaceOrderCommand(cmd, orderId);


            var finalResult = _createResult(cmd, engineResult.Status, orderId, engineResult.RejectionCode,
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
                .Deserialize<CommandAckResult>(record.ResponseJson);
        }

        if (string.IsNullOrWhiteSpace(record.ResponseJson))
        {
            throw new IdempotencyConflictException($"Record found in {record.State} state but missing response data.");
        }

        throw new IdempotencyConflictException($"Unknown idempotency state: {record.State}");
    }

    private static int _mapStatusToStatusCode(Status status) => status switch
    {
        Status.Submitted => 200,
        Status.Rejected => 400,
        _ => 500
    };

    private static (bool IsValid, RejectionCode? Code, string? Reason) _validate(PlaceOrderCommand cmd
    )
    {
        if (cmd.AccountId <= 0)
            return (false, RejectionCode.InvalidAccountId, "Account ID must be positive.");

// Simplified: catches null, empty, and whitespace in one go
        if (string.IsNullOrWhiteSpace(cmd.IdempotencyKey))
            return (false, RejectionCode.InvalidIdempotencyKey, "IdempotencyKey is required and cannot be empty.");

        if (!TradingOptions.Symbols.Contains(cmd.Symbol))
            return (false, RejectionCode.InvalidSymbol, $"Symbol {cmd.Symbol} is not supported.");

        if (cmd.Quantity <= 0)
            return (false, RejectionCode.InvalidQuantity, "Quantity must be greater than zero.");

        if (cmd is { Side: not (Side.Ask or Side.Bid) })
            return (false, RejectionCode.InvalidSide, "Side must be either Ask or Bid.");

        if (cmd is { OrderType: not (OrderType.Limit or OrderType.Market) })
            return (false, RejectionCode.InvalidOrderType, "Order type must be either Limit or Market.");

// Using property patterns for concise multi-condition checks
        if (cmd is { OrderType: OrderType.Limit, Price: null or <= 0 })
            return (false, RejectionCode.PriceRequired, "Limit orders must have a positive price.");

        if (cmd is { OrderType: OrderType.Market, Price: not null })
            return (false, RejectionCode.PriceNotAllowedForMarket, "Market orders must not have a price.");
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