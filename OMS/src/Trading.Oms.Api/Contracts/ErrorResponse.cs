namespace Trading.Oms.Api.Contracts;

public record ErrorResponse(
    string Error,
    string Code,
    string RequestId,
    string CorrelationId);