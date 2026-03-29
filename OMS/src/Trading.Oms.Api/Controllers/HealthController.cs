using Microsoft.AspNetCore.Mvc;

namespace Trading.Oms.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ready" });
    }
}