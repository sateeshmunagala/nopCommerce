using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Nop.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
[EnableCors("_myAllowSpecificOrigins")]
public class UserController : ControllerBase
{

    [HttpGet("best-performers")]
    public IActionResult BestPerformers()
    {
        var response = new
        {
            success = true,
            data = Array.Empty<string>()
        };

        return Ok(response);
    }

    [HttpPost("user-details-with-id")]
    public IActionResult UserDetailsWithId(JsonElement data)
    {
        // Create an anonymous object representing your JSON structure
        var responseData = new
        {
            success = true,
            data = new
            {
                additionalSettings = new { },
                _id = "67d13a30510e07b6fec09000",
                name = "Sateesh",
                mail = "umsateesh@gmail.com",
                isMailVerified = false,
                isAccountVerified = false,
                mobileVerified = false,
                openToWork = true,
                skills = new string[0],
                groups = new string[0],
                isHired = false,
                openForFreeInternship = false,
                workPreference = new string[0],
                cityPreference = new string[0],
                role = new
                {
                    cluster = "65eeb0ff14b93e4c5bc91aef",
                    _id = "647ce2e8c41bee677ea39fc4",
                    role = "student",
                    description = "this is the student role description",
                    isDeleted = false,
                    createdAt = 1685906152533,
                    lastModified = 1685906152533,
                    updatedAt = "2023-06-04T19:15:52.533Z",
                    __v = 0
                },
                password = "$2b$10$Gpf1kIXonVTZbZ02Lk56i.oLaHw1GMGA5mOkW33HfgbXdEsekZsOG",
                loginType = "email",
                whoPay = "candidate",
                referalCode = "488SAT",
                refferTo = new string[0],
                isDisabled = false,
                isDeleted = false,
                cluster = "65eeb0ff14b93e4c5bc91aef",
                previousCompanies = new string[0],
                hiredBy = new string[0],
                access = new string[0],
                socialMediaAccounts = new string[0],
                createdAt = 1741765168014,
                lastModified = 1741765168014,
                lastUsed = 1742928986591,
                updatedAt = "2025-03-25T18:56:31.715Z",
                __v = 0,
                city = "Hyderabad",
                mobile = 8019224099,
                pic = "https://reaidystorage.blob.core.windows.net/user-media/27731742321826382",
                coins = 116,
                coinUsage = new string[0]
            }
        };

        // Use System.Text.Json to serialize the object to JSON
        string jsonResponse = JsonSerializer.Serialize(responseData, new JsonSerializerOptions
        {
            WriteIndented = true, // Pretty print the JSON
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // Use camelCase for property names
        });

        // Return the JSON response with the appropriate content type
        return Content(jsonResponse, "application/json");

    }

    //[HttpPost("user-details-with-id")]
    public IActionResult UserDetailsWithId_old(JsonElement data)
    {
        string jsonString = @"{
                ""success"": true,
                ""data"": {
                    ""additionalSettings"": {},
                    ""_id"": ""67d13a30510e07b6fec09000"",
                    ""name"": ""Sateesh"",
                    ""mail"": ""umsateesh@gmail.com"",
                    ""isMailVerified"": false,
                    ""isAccountVerified"": false,
                    ""mobileVerified"": false,
                    ""openToWork"": true,
                    ""skills"": [],
                    ""groups"": [],
                    ""isHired"": false,
                    ""openForFreeInternship"": false,
                    ""workPreference"": [],
                    ""cityPreference"": [],
                    ""role"": {
                        ""cluster"": ""65eeb0ff14b93e4c5bc91aef"",
                        ""_id"": ""647ce2e8c41bee677ea39fc4"",
                        ""role"": ""student"",
                        ""description"": ""this is the student role description"",
                        ""isDeleted"": false,
                        ""createdAt"": 1685906152533,
                        ""lastModified"": 1685906152533,
                        ""updatedAt"": ""2023-06-04T19:15:52.533Z"",
                        ""__v"": 0
                    },
                    ""password"": ""$2b$10$Gpf1kIXonVTZbZ02Lk56i.oLaHw1GMGA5mOkW33HfgbXdEsekZsOG"",
                    ""loginType"": ""email"",
                    ""whoPay"": ""candidate"",
                    ""referalCode"": ""488SAT"",
                    ""refferTo"": [],
                    ""isDisabled"": false,
                    ""isDeleted"": false,
                    ""cluster"": ""65eeb0ff14b93e4c5bc91aef"",
                    ""previousCompanies"": [],
                    ""hiredBy"": [],
                    ""access"": [],
                    ""socialMediaAccounts"": [],
                    ""createdAt"": 1741765168014,
                    ""lastModified"": 1741765168014,
                    ""lastUsed"": 1742928986591,
                    ""updatedAt"": ""2025-03-25T18:56:31.715Z"",
                    ""__v"": 0,
                    ""city"": ""Hyderabad"",
                    ""mobile"": 8019224099,
                    ""pic"": ""https://reaidystorage.blob.core.windows.net/user-media/27731742321826382"",
                    ""coins"": 116,
                    ""coinUsage"": []
                }
             }";

        return Content(jsonString, "application/json");

    }

    [HttpPost("login-with-email")]
    public IActionResult LoginWithEmail(JsonElement data)
    {
        // Create an anonymous object representing your JSON structure
        var responseData = new
        {
            success = true,
            data = new
            {
                additionalSettings = new { },
                _id = "67d13a30510e07b6fec09000",
                name = "Sateesh",
                mail = "umsateesh@gmail.com",
                isMailVerified = false,
                isAccountVerified = false,
                mobileVerified = false,
                openToWork = true,
                skills = new string[0], // Empty array
                groups = new string[0], // Empty array
                isHired = false,
                openForFreeInternship = false,
                workPreference = new string[0], // Empty array
                cityPreference = new string[0], // Empty array
                role = new
                {
                    cluster = "65eeb0ff14b93e4c5bc91aef",
                    _id = "647ce2e8c41bee677ea39fc4",
                    role = "student",
                    description = "this is the student role description",
                    isDeleted = false,
                    createdAt = 1685906152533,
                    lastModified = 1685906152533,
                    updatedAt = "2023-06-04T19:15:52.533Z",
                    __v = 0
                },
                loginType = "email",
                whoPay = "candidate",
                referalCode = "488SAT",
                refferTo = new string[0], // Corrected typo and made it an array
                isDisabled = false,
                isDeleted = false,
                cluster = "65eeb0ff14b93e4c5bc91aef",
                previousCompanies = new string[0], // Empty array
                hiredBy = new string[0],  // Empty array
                access = new string[0], // Empty array
                socialMediaAccounts = new string[0], // Empty array
                createdAt = 1741765168014,
                lastModified = 1741765168014,
                lastUsed = 1742928986591,
                updatedAt = "2025-03-25T18:56:31.715Z",
                __v = 0,
                city = "Hyderabad",
                mobile = 8019224099,
                pic = "https://reaidystorage.blob.core.windows.net/user-media/27731742321826382",
                projects = new string[0], // Empty array
                coins = 116,
                coinUsage = new string[0], // Empty array
                session = new
                {
                    userId = "67d13a30510e07b6fec09000",
                    isDeleted = false,
                    _id = "68034e43f5fb90ea4c5cfab0",
                    logInTime = 1745047107670,
                    logOutTime = 1745047107670,
                    createdAt = 1745047107670,
                    updatedAt = 1745047107670,
                    __v = 0
                }
            }
        };

        // Use System.Text.Json to serialize the object to JSON
        string jsonResponse = JsonSerializer.Serialize(responseData, new JsonSerializerOptions
        {
            WriteIndented = true, // Pretty print the JSON
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // Use camelCase for property names
        });

        // Return the JSON response with the appropriate content type
        return Content(jsonResponse, "application/json");
    }

    [HttpPost("login-with-google")]
    public IActionResult LoginWithGoogle(JsonElement data)
    {
        return Ok();
    }



    // GET: api/<UserController>
    [HttpGet]
    public IEnumerable<string> Get()
    {
        return new string[] { "value1", "value2" };
    }

    // GET api/<UserController>/5
    [HttpGet("{id}")]
    public string Get(int id)
    {
        return "value";
    }

    // POST api/<UserController>
    [HttpPost]
    public void Post([FromBody] string value)
    {
    }

    // PUT api/<UserController>/5
    [HttpPut("{id}")]
    public void Put(int id, [FromBody] string value)
    {
    }

    // DELETE api/<UserController>/5
    [HttpDelete("{id}")]
    public void Delete(int id)
    {
    }

    //[HttpGet("user-interviews/{id}")]
    //public IActionResult UserInterviews(string id)
    //{
    //    var response = new
    //    {
    //        success = true,
    //        data = new List<string> { }
    //    };

    //    return Ok(response);
    //}
}

