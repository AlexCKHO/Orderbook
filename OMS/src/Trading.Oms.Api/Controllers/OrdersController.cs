using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Trading.Oms.Api.Contracts;
using Trading.Oms.Api.Mappers;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(
    IPlaceOrderCommandHandler placeOrderCommandHandler,
    ICancelOrderCommandHandler cancelOrderCommandHandler) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request, CancellationToken token)
    {
        string? correlationId = Request.Headers["X-Correlation-ID"].FirstOrDefault();
        string? idempotencyKey = Request.Headers["X-Idempotency-Key"].FirstOrDefault();

        string requestId = Guid.NewGuid().ToString();
        correlationId ??= Guid.NewGuid().ToString();
        
        HttpContext.Items["RequestId"] = requestId;
        HttpContext.Items["CorrelationId"] = correlationId;
        
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            throw new ValidationException("Missing X-Idempotency-Key header");
        }

        RequestMetadata metadata = new(requestId, correlationId, idempotencyKey, DateTimeOffset.UtcNow);

        PlaceOrderCommand command = Mapper.MapToPlaceOrderCommand(request, metadata);
        CommandAckResult result = await placeOrderCommandHandler.HandleAsync(command, token); 
        CommandAckResponse response = Mapper.MapToPlaceOrderCommandAckResult(command, result);

        return result.Status switch
        {
            Status.Submitted => Ok(response),
            Status.Rejected => BadRequest(response), 
            _ => StatusCode(500, "Unexpected command Status")
        };
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> CancelOrder([FromBody] CancelOrderRequest request, CancellationToken token)
    {
        string? correlationId = Request.Headers["X-Correlation-ID"].FirstOrDefault();
        string? idempotencyKey = Request.Headers["X-Idempotency-Key"].FirstOrDefault();

        string requestId = Guid.NewGuid().ToString();
        correlationId ??= Guid.NewGuid().ToString();

        HttpContext.Items["RequestId"] = requestId;
        HttpContext.Items["CorrelationId"] = correlationId;

        if (string.IsNullOrEmpty(idempotencyKey))
        {
            throw new ValidationException("Missing X-Idempotency-Key header");
        }

        RequestMetadata metadata = new(requestId, correlationId, idempotencyKey, DateTimeOffset.UtcNow);

        CancelOrderCommand command = Mapper.MapToCancelOrderCommand(request, metadata);
        CommandAckResult result = await cancelOrderCommandHandler.HandleAsync(command, token);
        CommandAckResponse response = Mapper.MapToCancelOrderCommandAckResult(command, result);

        return result.Status switch
        {
            Status.Submitted => Ok(response),
            Status.Rejected => BadRequest(response),
            _ => StatusCode(500, "Unexpected command Status")
        };
    }
}