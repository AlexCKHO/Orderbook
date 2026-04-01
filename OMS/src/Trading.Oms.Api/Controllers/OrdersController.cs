using Microsoft.AspNetCore.Mvc;
using Trading.Oms.Api.Contracts;
using Trading.Oms.Api.Mappers;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Models;
using Trading.Oms.Application.Services;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private PlaceOrderCommandHandler _placeOrderCommandHandler;

    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
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
        

        PlaceOrderCommand command = Mapper.MapToPlaceOrderCommand(request, metadata);

        CommandAckResult result = await _placeOrderCommandHandler.HandleAsync(command);

        CommandAckResponse response = Mapper.MapToPlaceOrderCommandAckResult(command, result);

        return response.Status switch
        {
            Status.Submitted => Ok(response),
            Status.Rejected => BadRequest(response),
            _ => StatusCode(500, "Unexpected command Status")
        };
    }

    [HttpPost]
    [Route("cancel")]
    public IActionResult CancelOrder(CancelOrderRequest request)
    {
        return Ok(new CommandAckResponse(
            RequestId: "1",
            CorrelationId: "1",
            IdempotencyKey: "1",
            CommandType: CommandType.CancelOrder,
            Status: Status.Submitted,
            OrderId: 123,
            RejectionCode: null,
            RejectionReason: null,
            ReceivedAtUtc: DateTimeOffset.UtcNow)
        );
    }
}