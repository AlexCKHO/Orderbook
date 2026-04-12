using System.ComponentModel.DataAnnotations;
using System.Net;
using Trading.Oms.Api.Contracts;
using Trading.Oms.Application.Exceptions;

namespace Trading.Oms.Api.Middleware;

public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (status, code) = exception switch
        {
            IdempotencyConflictException => (HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT"),
            ValidationException => (HttpStatusCode.BadRequest, "VALIDATION_FAILED"),
            EngineUnavailableException => (HttpStatusCode.ServiceUnavailable, "ENGINE_DOWN"),
            _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR")
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)status;

        var requestId = context.Items["RequestId"]?.ToString() ?? "unknown";
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";

        return context.Response.WriteAsJsonAsync(new ErrorResponse(
            exception.Message, code, requestId, correlationId));
    }
}