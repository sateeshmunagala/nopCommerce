using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Nop.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
[EnableCors("_myAllowSpecificOrigins")]
public class PaymentController : ControllerBase
{
    [HttpPost("redeem-promo-code")]
    public IActionResult RedeemPromoCode(JsonElement data)
    {
        var response = new
        {
            success = false,
            message = "Coupon code is not valid"
        };

        return Ok(response);
    }
}
