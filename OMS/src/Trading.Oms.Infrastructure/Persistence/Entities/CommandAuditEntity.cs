using Trading.Oms.Domain.Enums;


namespace Trading.Oms.Infrastructure.Persistence.Entities;

public class CommandAuditEntity

{
    public long Id { get; set; }
    public required string RequestId { get; set; }
    public required string CorrelationId { get; set; }
    public required string IdempotencyKey { get; set; }
    public required uint AccountId { get; set; }
    public long? OrderId { get; set; }
    public required CommandType CommandType { get; set; }
    public required string PayloadHash { get; set; }
    public required string RequestPayloadJson { get; set; }
    public Status Status { get; set; }
    public RejectionCode? RejectionCode { get; set; }
    public string? RejectionReason { get; set; }
    public required DateTimeOffset SubmittedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}