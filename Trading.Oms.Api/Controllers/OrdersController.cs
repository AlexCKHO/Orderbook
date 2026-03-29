using Microsoft.AspNetCore.Mvc;

namespace OMS.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    [HttpPost]
    public IActionResult PostOrder()
    {
        return Ok(new { status = "Order received!" });
    }
}