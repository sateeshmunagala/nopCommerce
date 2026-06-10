using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text.Json;
using Nop.Web.Custom.Extensions.Models;

namespace Nop.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ApplicationController : ControllerBase
{

    [HttpGet("application-status")]
    public IActionResult ApplicationStatus(int interviewId = 2, string applicationId = "", string mcqId = "")
    {
        var response = new
        {
            success = "true",
            body = new
            {
                success = "true",
                recording = interviewId, //recordingid
                jobPostId = interviewId,
                isResultPublished = "true",
                data = new
                {
                    applicationId = 5,
                    mockTestStatus = "completed", // or "pending"
                    technicalInterviewStatus = "completed", // or "pending"
                    hrInterviewStatus = "completed", // or "pending"
                    hired = "true",
                    rejected = "false",
                    technicalId = 123,
                    hrId = 456,
                },

            }
        };

        var jsonString = JsonSerializer.Serialize(response);

        return Content(jsonString, "application/json");
    }

    [HttpGet("application-assessments-status/{id}")]
    public IActionResult ApplicationAssessmentsStatus(string id)
    {

        var topics = new List<TopicResult>
            {
                new() { Name = "technical_score", Score = 85 },
                new() { Name = "communication_score", Score = 92 },
                new() { Name = "professionalism_score", Score = 92 },

                new() { Name = "positiveness_score", Score = 92 },
                new() { Name = "interviewSuggestion", Score = 92 },
                new() { Name = "summerySuggestion", Score = 92 },

                new() { Name = "sociability_score", Score = 92 }
            };

        var response = new
        {
            success = "true",
            body = new
            {
                _id = id,
                success = "true",
                recording = id, //recordingid
                jobPostId = id,
                isResultPublished = "true",

                result = topics,
                questions = topics,

                interviewee = new
                {
                    name = "sateesh application controller"
                }
            }
        };

        var jsonString = JsonSerializer.Serialize(response);

        return Content(jsonString, "application/json");
    }
}
