using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Nop.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class SuggestionController : ControllerBase
{
    [HttpPost("new-suggestion")]
    public IActionResult NewSuggestion(JsonElement data)
    {
        string jsonInput = JsonSerializer.Serialize(data);
        JsonDocument document = JsonDocument.Parse(jsonInput);
        JsonElement root = document.RootElement;

        var body = root.GetProperty("body").GetString();
        var cluster = root.GetProperty("cluster").GetString();
        var rating = root.GetProperty("rating").GetInt64();
        var subject = root.GetProperty("subject").GetString();
        var uDetails = root.GetProperty("uDetails").GetString();

        var response = new
        {
            success = "true",
            body = new
            {
                data = new
                {
                    interviewType = "real"
                },
                interviewResponse = new
                {
                    body = new
                    {
                        content = $"Hello"
                    },
                    systemQuestion = "systemQuestion"
                }
            },
            message = "You have enough credits(179) to start the interview - api response"
        };

        var jsonString = JsonSerializer.Serialize(response);

        //return Content(jsonString, "application/json");
        return Ok();
    }
}

