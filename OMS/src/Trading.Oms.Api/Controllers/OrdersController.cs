using Microsoft.AspNetCore.Mvc;

namespace Trading.Oms.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    [HttpPost]
    public IActionResult PlaceOrder()
    {
        return Ok(new { status = "Order received!" });
    }

    [HttpPost]
    [Route("/api/orders/cancel")]
    public IActionResult CancelOrderRequest()
    {
        return Ok(new { status = "Order received!" });
    }
}