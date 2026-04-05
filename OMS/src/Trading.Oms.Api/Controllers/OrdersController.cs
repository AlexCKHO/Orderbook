using Microsoft.AspNetCore.Mvc;
using Trading.Oms.Api.Contracts;
using Trading.Oms.Api.Mappers;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Exceptions;
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
    private readonly IPlaceOrderCommandHandler _placeOrderCommandHandler = placeOrderCommandHandler;
    private readonly ICancelOrderCommandHandler _cancelOrderCommandHandler = cancelOrderCommandHandler;


    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request, CancellationToken token)
    {
        string? correlationId = Request.Headers["X-Correlation-ID"].FirstOrDefault();
        string? idempotencyKey = Request.Headers["X-Idempotency-Key"].FirstOrDefault();

        if (String.IsNullOrEmpty(idempotencyKey))
        {
            return BadRequest("Missing Idempotency Key");
        }

        RequestMetadata metadata = new RequestMetadata(
            requestId: Guid.NewGuid().ToString(),
            correlationId: correlationId ?? Guid.NewGuid().ToString(),
            idempotencyKey: idempotencyKey,
            submittedAtUtc: DateTimeOffset.UtcNow
        );

        try
        {
            PlaceOrderCommand command = Mapper.MapToPlaceOrderCommand(request, metadata);

            CommandAckResult result = await _placeOrderCommandHandler.HandleAsync(command, token);

            CommandAckResponse response = Mapper.MapToPlaceOrderCommandAckResult(command, result);

            return result.Status switch
            {
                Status.Submitted => Ok(response),
                Status.Rejected => BadRequest(response),
                _ => StatusCode(500, "Unexpected command Status")
            };
        }
        catch (IdempotencyConflictException idempotencyConflictException)
        {
            return StatusCode(StatusCodes.Status409Conflict, idempotencyConflictException.Message);
        }
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> CancelOrder([FromBody] CancelOrderRequest request, CancellationToken token)
    {
        string? correlationId = Request.Headers["X-Correlation-ID"].FirstOrDefault();
        string? idempotencyKey = Request.Headers["X-Idempotency-Key"].FirstOrDefault();

        if (String.IsNullOrEmpty(idempotencyKey))
        {
            return BadRequest("Missing Idempotency Key");
        }

        RequestMetadata metadata = new RequestMetadata(
            requestId: Guid.NewGuid().ToString(),
            correlationId: correlationId ?? Guid.NewGuid().ToString(),
            idempotencyKey: idempotencyKey,
            submittedAtUtc: DateTimeOffset.UtcNow
        );

        try
        {
            CancelOrderCommand command = Mapper.MapToCancelOrderCommand(request, metadata);

            CommandAckResult result = await _cancelOrderCommandHandler.HandleAsync(command, token);

            CommandAckResponse response = Mapper.MapToCancelOrderCommandAckResult(command, result);

            return result.Status switch
            {
                Status.Submitted => Ok(response),
                Status.Rejected => BadRequest(response),
                _ => StatusCode(500, "Unexpected command Status")
            };
        }
        catch (IdempotencyConflictException idempotencyConflictException)
        {
            return StatusCode(StatusCodes.Status409Conflict, idempotencyConflictException.Message);
        }
    }
}