using Microsoft.AspNetCore.Mvc;
using Trading.Oms.Api.Contracts;

namespace Trading.Oms.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    [HttpPost]
    public IActionResult PlaceOrder(PlaceOrderRequest request)
    {
        return Ok(new
        {
            status = "Order received!", Response = new CommandAckResponse(
                RequestId: "1",
                CorrelationId:"1",
                IdempotencyKey:"1",
                CommandType: CommandType.PlaceOrder,
                Status: Status.Submitted,
                OrderId: 123,
                RejectionCode:null,
                RejectionReason: null,
                ReceivedAtUtc: DateTimeOffset.Now)
        });
    }

    [HttpPost]
    [Route("/api/orders/cancel")]
    public IActionResult CancelOrderRequest(CancelOrderRequest request)
    {
        return Ok(new
        {
            status = "Order received!", Response = new CommandAckResponse(
                RequestId: "1",
                CorrelationId:"1",
                IdempotencyKey:"1",
                CommandType: CommandType.CancelOrder,
                Status: Status.Submitted,
                OrderId: 123,
                RejectionCode:null,
                RejectionReason: null,
                ReceivedAtUtc: DateTimeOffset.Now)
        });
    }
}