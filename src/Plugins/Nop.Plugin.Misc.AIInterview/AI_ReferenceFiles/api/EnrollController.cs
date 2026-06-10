using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Nop.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
[EnableCors("_myAllowSpecificOrigins")]
public class EnrollController : ControllerBase
{

    [HttpGet("user-enrolled-courses/{id}")]
    public IActionResult UserEnrolledCourses(string id)
    {
        var response = new
        {
            success = true,
            data = Array.Empty<string>()
        };

        return Ok(response);
    }








    // GET: api/<EnrollController>
    //[HttpGet]
    //public IEnumerable<string> Get()
    //{
    //    return new string[] { "value1", "value2" };
    //}

    //// GET api/<EnrollController>/5
    //[HttpGet("{id}")]
    //public string Get(int id)
    //{
    //    return "value";
    //}

    // POST api/<EnrollController>
    [HttpPost]
    public void Post([FromBody] string value)
    {
    }

    // PUT api/<EnrollController>/5
    [HttpPut("{id}")]
    public void Put(int id, [FromBody] string value)
    {
    }

    // DELETE api/<EnrollController>/5
    [HttpDelete("{id}")]
    public void Delete(int id)
    {
    }
}

