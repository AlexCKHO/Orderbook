namespace Trading.Oms.Application.Models;

public class RequestMetadata
{
    public string RequestId { get; }
    public string CorrelationId { get; }
    public string IdempotencyKey { get; }
    public DateTimeOffset SubmittedAtUtc { get; }

    public RequestMetadata(string requestId, string correlationId, string idempotencyKey, DateTimeOffset submittedAtUtc)
    {
        RequestId = requestId;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey;
        SubmittedAtUtc = submittedAtUtc;
    }
}