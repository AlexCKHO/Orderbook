using System.Formats.Asn1;
using System.Text.Json;
using Trading.Oms.Api.Oms.Domain.Interface;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Services;

public class PlaceOrderCommandHandler(
    IOrderSequenceAllocator orderSequenceAllocator,
    IOrderIdComposer orderIdComposer,
    IMatchingEngineClient matchingEngineClient,
    IIdempotencyStore idempotencyStore,
    IHashingService hashingService
) : IPlaceOrderCommandHandler
{
    private readonly IOrderSequenceAllocator _orderSequenceAllocator = orderSequenceAllocator;
    private readonly IOrderIdComposer _orderIdComposer = orderIdComposer;
    private readonly IMatchingEngineClient _matchingEngineClient = matchingEngineClient;
    private readonly IIdempotencyStore _idempotencyStore = idempotencyStore;
    private readonly IHashingService _hashingService = hashingService;

    public async Task<CommandAckResult> HandleAsync(PlaceOrderCommand cmd, CancellationToken token)
    {
        string scope = "place-order";

        var (isValid, rejectCode, reason) = _validate(cmd);

        if (!isValid)
        {
            return _createResult(cmd, Status.Rejected, null, rejectCode, reason);
        }

        var currentCmdHash = _hashingService.HashPlaceOrderCommand(cmd.AccountId, cmd.Symbol, cmd.Side,
            cmd.OrderType, cmd.Quantity, cmd.Price);

        var idempotencyRecord =
            await _idempotencyStore.GetAsync(scope, cmd.AccountId, cmd.IdempotencyKey, token);

        var currentTime = DateTimeOffset.UtcNow;

        if (idempotencyRecord is not null)
        {
            // Order already being processed
            if (idempotencyRecord.State == IdempotencyStates.InProgress)
            {
                throw new  Exception("Duplicate order requested.");
            }
            // Malicious attack
            else if (!currentCmdHash.Equals(idempotencyRecord.RequestHash))
            {
                throw new Exception("Idempotency key already used with different payload");
            }
            // Duplicate request
            else if (currentCmdHash.Equals(idempotencyRecord.RequestHash))
            {
                var jsonString = idempotencyRecord.ResponseJson;

                var replayResult = JsonSerializer.Deserialize<CommandAckResult>(jsonString);

                return replayResult;
            }
        }

        var reserve = new IdempotencyReservation();
        reserve.Scope = scope;
        reserve.AccountId = cmd.AccountId;
        reserve.IdempotencyKey = cmd.IdempotencyKey;
        reserve.RequestId = cmd.RequestId;
        reserve.RequestHash = currentCmdHash;
        reserve.CreatedAtUtc = currentTime;
        reserve.ExpiresAtUtc = currentTime.AddHours(24);


        await _idempotencyStore.ReserveAsync(reserve, token);


        var sequence = await _orderSequenceAllocator.AllocateNextSequenceForAccount(cmd.AccountId);
        var orderId = _orderIdComposer.Compose(cmd.AccountId, sequence);

        EnginePlaceOrderResult result = await _matchingEngineClient.PlaceOrderCommand(cmd, orderId);

        var finalResult = _createResult(cmd, result.Status, orderId, result.RejectionCode, result.RejectionReason);

        var resultJson = JsonSerializer.Serialize(finalResult);

        await _idempotencyStore.CompleteAsync(reserve.Scope, reserve.AccountId, reserve.IdempotencyKey, null,
            resultJson,
            DateTimeOffset.UtcNow, token
        );

        return finalResult;
    }

    private static (bool IsValid, RejectionCode? Code, string? Reason) _validate(PlaceOrderCommand cmd
    )
    {
        if (cmd.AccountId <= 0)
            return (false, RejectionCode.INVALID_ACCOUNT_ID, "Account ID must be positive.");

        if (cmd is { IdempotencyKey: null })
            return (false, RejectionCode.INVALID_IDEMPOTENCY_KEY, "IdempotencyKey must not be null.");

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

        if (cmd is { OrderType: OrderType.Market, Price: not null })
            return (false, RejectionCode.PRICE_REQUIRED, "Market orders must not have price.");

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