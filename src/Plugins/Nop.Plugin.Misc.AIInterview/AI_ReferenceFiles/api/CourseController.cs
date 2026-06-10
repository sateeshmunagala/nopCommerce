using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Nop.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
[EnableCors("_myAllowSpecificOrigins")]
public class CourseController : ControllerBase
{

    [HttpGet("all-courses")]
    public IActionResult AllCourses()
    {
        string jsonString = @"{
                ""success"": true,
                ""data"": [
                    {
                        ""creditsRequired"": 10,
                        ""cluster"": ""65eeb0ff14b93e4c5bc91aef"",
                        ""_id"": ""652918d935b14c1ea909b20b"",
                        ""title"": ""Flutter"",
                        ""description"": ""Flutter is an open-source UI software development kit (SDK) created by Google for building natively compiled applications for mobile, web, and desktop from a single codebase. Flutter is known for its fast development, expressive and flexible design, and high-performance capabilities."",
                        ""image"": ""https://reaidystorage.blob.core.windows.net/spotmiespublic/1.png"",
                        ""thumbnail"": ""https://i.ytimg.com/vi/-rQ_OmPj300/hqdefault.jpg"",
                        ""sort"": 0,
                        ""benefits"": [],
                        ""outComes"": [],
                        ""requirements"": [],
                        ""CourseFor"": [],
                        ""comingSoon"": false,
                        ""isActive"": true
                    },
                    {
                        ""creditsRequired"": 10,
                        ""cluster"": ""65eeb0ff14b93e4c5bc91aef"",
                        ""_id"": ""6528e9771dde3fc4c7814c83"",
                        ""title"": ""Deep Learning"",
                        ""description"": ""Deep learning is a subfield of machine learning that focuses on training artificial neural networks, known as deep neural networks, to perform tasks that involve pattern recognition, data analysis, and decision making. Deep learning has gained immense popularity and made significant advancements in various applications, including computer vision, natural language processing, speech recognition, and reinforcement learning. "",
                        ""image"": ""https://reaidystorage.blob.core.windows.net/spotmiespublic/deep-learning(2).png"",
                        ""thumbnail"": ""https://online.york.ac.uk/wp-content/uploads/2021/09/Illustration-of-a-brain-with-cogs-inside-and-pathways-outside-and-deep-learning-written-above-1030x579.jpg"",
                        ""sort"": 0,
                        ""benefits"": [],
                        ""outComes"": [],
                        ""requirements"": [
                            ""Machine Learning"",
                            ""Artificial Intelligence"",
                            ""Numpys and Pandas""
                        ],
                        ""CourseFor"": [],
                        ""comingSoon"": false,
                        ""isActive"": true
                    }
                ]
            }";

        return Content(jsonString, "application/json");
    }


    // GET: api/<CourseController>
    [HttpGet]
    public IEnumerable<string> Get()
    {
        return new string[] { "value1", "value2" };
    }

    // GET api/<CourseController>/5
    [HttpGet("{id}")]
    public string Get(int id)
    {
        return "value";
    }

    // POST api/<CourseController>
    [HttpPost]
    public void Post([FromBody] string value)
    {
    }

    // PUT api/<CourseController>/5
    [HttpPut("{id}")]
    public void Put(int id, [FromBody] string value)
    {
    }

    // DELETE api/<CourseController>/5
    [HttpDelete("{id}")]
    public void Delete(int id)
    {
    }
}

