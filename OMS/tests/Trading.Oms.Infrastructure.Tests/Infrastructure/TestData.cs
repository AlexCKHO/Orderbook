using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;
using Trading.Oms.Infrastructure.Persistence.Entities;

namespace Trading.Oms.Infrastructure.Tests.Infrastructure;

internal static class TestData
{
    public static readonly DateTimeOffset SubmittedAt = new(2026, 4, 7, 10, 15, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset CompletedAt = new(2026, 4, 7, 10, 16, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset ExpiresAt = new(2026, 4, 8, 10, 15, 0, TimeSpan.Zero);

    public static PlaceOrderCommand PlaceOrderCommand(
        uint accountId = 123,
        string requestId = "request-place",
        string correlationId = "correlation-place",
        string idempotencyKey = "idempotency-place")
    {
        return new PlaceOrderCommand(
            accountId,
            "BTC-USD",
            Side.Bid,
            OrderType.Limit,
            10,
            50000,
            requestId,
            correlationId,
            idempotencyKey,
            SubmittedAt);
    }

    public static CancelOrderCommand CancelOrderCommand(
        uint accountId = 123,
        ulong orderId = 987654321,
        ulong engineId = 987654321,
        string requestId = "request-cancel",
        string correlationId = "correlation-cancel",
        string idempotencyKey = "idempotency-cancel")
    {
        return new CancelOrderCommand(accountId, orderId, engineId, requestId, correlationId, idempotencyKey,
            SubmittedAt);
    }

    public static CommandAudit CommandAudit(
        string requestId = "request-1",
        string correlationId = "correlation-1",
        string idempotencyKey = "idempotency-1",
        uint accountId = 123,
        long? clientOrderId = null,
        long? engineOrderId = null,
        CommandType commandType = CommandType.PlaceOrder,
        Status status = Status.Received)
    {
        return new CommandAudit
        {
            RequestId = requestId,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
            AccountId = accountId,
            ClientOrderId = clientOrderId,
            EngineOrderId = engineOrderId,
            CommandType = commandType,
            PayloadHash = "22FE290B946440CEC858E0BDB242D5209373066358FF8B74FC6C5CD0638C10A8",
            RequestPayloadJson = "{\"symbol\":\"BTC-USD\"}",
            Status = status,
            SubmittedAtUtc = SubmittedAt,
            CompletedAtUtc = null
        };
    }

    public static CommandAuditEntity CommandAuditEntity(
        string requestId = "request-1",
        string correlationId = "correlation-1",
        string idempotencyKey = "idempotency-1",
        uint accountId = 123,
        long? orderId = null,
        CommandType commandType = CommandType.PlaceOrder,
        Status status = Status.Received)
    {
        return new CommandAuditEntity
        {
            RequestId = requestId,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
            AccountId = accountId,
            ClientOrderId = orderId,
            CommandType = commandType,
            PayloadHash = "22FE290B946440CEC858E0BDB242D5209373066358FF8B74FC6C5CD0638C10A8",
            RequestPayloadJson = "{\"symbol\":\"BTC-USD\"}",
            Status = status,
            SubmittedAtUtc = SubmittedAt,
            CompletedAtUtc = null
        };
    }

    public static IdempotencyReservation IdempotencyReservation(
        string scope = "place-order",
        uint accountId = 123,
        string idempotencyKey = "idempotency-1",
        string requestId = "request-1")
    {
        return new IdempotencyReservation
        {
            Scope = scope,
            AccountId = accountId,
            IdempotencyKey = idempotencyKey,
            RequestId = requestId,
            RequestHash = "CAA664181A4BB62365073EF5AE83B8E6328ED5F2B894447ECB1ED9C0791DD9A5",
            CreatedAtUtc = SubmittedAt,
            ExpiresAtUtc = ExpiresAt
        };
    }

    public static IdempotencyRecordEntity IdempotencyRecordEntity(
        string scope = "place-order",
        uint accountId = 123,
        string idempotencyKey = "idempotency-1",
        string requestId = "request-1",
        IdempotencyStates state = IdempotencyStates.InProgress,
        int? responseStatusCode = null,
        string? responseJson = null,
        DateTimeOffset? completedAtUtc = null)
    {
        return new IdempotencyRecordEntity
        {
            Scope = scope,
            AccountId = accountId,
            IdempotencyKey = idempotencyKey,
            RequestId = requestId,
            RequestHash = "CAA664181A4BB62365073EF5AE83B8E6328ED5F2B894447ECB1ED9C0791DD9A5",
            State = state,
            ResponseStatusCode = responseStatusCode,
            ResponseJson = responseJson,
            CreatedAtUtc = SubmittedAt,
            CompletedAtUtc = completedAtUtc,
            ExpiresAtUtc = ExpiresAt
        };
    }
}