using Microsoft.AspNetCore.Mvc;
using Trading.Oms.Api.Contracts;
using Trading.Oms.Api.Mappers;
using Trading.Oms.Application.Commands;
using Trading.Oms.Application.Models;
using Trading.Oms.Domain.Enums;

namespace Trading.Oms.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    [HttpPost]
    public IActionResult PlaceOrder(PlaceOrderRequest request)
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
            submittedAtUtc: DateTimeOffset.Now
        );

       PlaceOrderCommand command = Mapper.MapToPlaceOrderCommand(request, metadata);
       
        
        
        
        return Ok(new CommandAckResponse(
            RequestId: "1",
            CorrelationId: "1",
            IdempotencyKey: "1",
            CommandType: CommandType.PlaceOrder,
            Status: Status.Submitted,
            OrderId: 0,
            RejectionCode: null,
            RejectionReason: null,
            ReceivedAtUtc: DateTimeOffset.Now)
        );
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