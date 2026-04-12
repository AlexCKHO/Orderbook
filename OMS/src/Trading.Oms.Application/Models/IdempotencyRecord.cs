using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Application.Models;

public sealed record IdempotencyRecord
{
    public required string Scope { get; set; }
    public uint AccountId { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestId { get; set; }
    public required string RequestHash { get; set; }

    public IdempotencyStates State { get; set; }

    // HTTP status code
    public int? ResponseStatusCode { get; set; }

    // InProgress, response can be null
    public string? ResponseJson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}