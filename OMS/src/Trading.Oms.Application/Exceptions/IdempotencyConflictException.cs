namespace Trading.Oms.Application.Exceptions;

public class IdempotencyConflictException(string message) : Exception(message);