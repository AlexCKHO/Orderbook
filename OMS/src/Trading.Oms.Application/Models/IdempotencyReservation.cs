namespace Trading.Oms.Application.Models;

public sealed record IdempotencyReservation 
{
    public string Scope { get; set; }
    public uint AccountId { get; set; }
    public string IdempotencyKey { get; set; }
    public string RequestId { get; set; }
    public string RequestHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}