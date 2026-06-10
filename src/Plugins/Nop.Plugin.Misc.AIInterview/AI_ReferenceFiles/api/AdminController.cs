using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Nop.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AdminController : ControllerBase
{
    [HttpPost("get-active-banner")]
    public IActionResult GetActiveBanner(JsonElement data, string usedFor)
    {
        return Ok();
    }
}
