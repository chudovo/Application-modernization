using Microsoft.AspNetCore.Mvc;

namespace OrderDesk.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    // Kept as a controller so the response body stays { "Status": "ok" }, which existing monitors parse.
    [HttpGet("api/health")]
    public IActionResult Get() => Ok(new { Status = "ok" });
}
