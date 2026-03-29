using Microsoft.AspNetCore.Mvc;

namespace OMS.Controllers;

[ApiController]
[Route("health")]
public class HealthController : Controller
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ready" });
    }
}