using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Nop.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
public class SubscriptionsController : ControllerBase
{

    [HttpGet("user-subscriptions/{id}")]
    public IActionResult UserSubscriptions(string id)
    {
        var responseData = new
        {
            success = true,
            subscriptions = new object[]
            {
                new
                {
                    _id = "67d13a30510e07b6fec0900e",
                    userId = new
                    {
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
                        lastUsed = 1745047102049,
                        updatedAt = "2025-04-19T07:18:27.646Z",
                        __v = 0,
                        city = "Hyderabad",
                        mobile = 8019224099,
                        pic = "https://reaidystorage.blob.core.windows.net/user-media/27731742321826382",
                        projects = new string[0]
                    },
                    planId = new
                    {
                        priceSpecification = new
                        {
                            amount = 0,
                            currencyCode = "INR"
                        },
                        _id = "67c2ff5b13b4c4ab8b1d0914",
                        name = "Free Plan",
                        credits = 400,
                        planApplicableFor = "647ce2e8c41bee677ea39fc4",
                        duration = "month",
                        isDeleted = false,
                        features = new object[] // Array of features
                        {
                            new {
                                name = "AI Resume Analyser",
                                limit = 2,
                                included = true,
                                _id = "67d7b9ec43d740ff541b4272"
                            },
                            new {
                                name = "AI Interviewer",
                                limit = 2,
                                included = true,
                                _id = "67d7b9ec43d740ff541b4273"
                            },
                            new {
                                name = "MCQ Assessments",
                                limit = 5,
                                included = true,
                                _id = "67d7b9ec43d740ff541b4274"
                            },
                            new {
                                name = "Job Application",
                                limit = 0,
                                included = true,
                                _id = "67d7b9ec43d740ff541b4275"
                            }
                        },
                        createdAt = "2025-03-01T12:36:43.242Z",
                        updatedAt = "2025-03-17T05:58:04.448Z",
                        __v = 0
                    },
                    startDate = 1741765168070,
                    endDate = 1744357168070,
                    featureUsage = new string[0],
                    status = 7,
                    isActivated = true,
                    isDeleted = false,
                    createdAt = "2025-03-12T07:39:28.244Z",
                    updatedAt = "2025-03-12T07:39:28.244Z",
                    __v = 0
                }
            }
        };

        string jsonResponse = JsonSerializer.Serialize(responseData, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        return Content(jsonResponse, "application/json");
    }

    // below is old version
    //[HttpGet("user-subscriptions/{id}")]
    public IActionResult UserSubscriptions_old(string id)
    {
        string jsonString = @"{
                ""success"": true,
                ""subscriptions"": [
                    {
                        ""_id"": ""67d13a30510e07b6fec0900e"",
                        ""userId"": {
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
                            ""pic"": ""https://reaidystorage.blob.core.windows.net/user-media/27731742321826382""
                        },
                        ""planId"": {
                            ""priceSpecification"": {
                                ""amount"": 0,
                                ""currencyCode"": ""INR""
                            },
                            ""_id"": ""67c2ff5b13b4c4ab8b1d0914"",
                            ""name"": ""Free Plan"",
                            ""credits"": 20,
                            ""planApplicableFor"": ""647ce2e8c41bee677ea39fc4"",
                            ""duration"": ""month"",
                            ""isDeleted"": false,
                            ""features"": [
                                {
                                    ""name"": ""AI Resume Analyser"",
                                    ""limit"": 2,
                                    ""included"": true,
                                    ""_id"": ""67d7b9ec43d740ff541b4272""
                                },
                                {
                                    ""name"": ""AI Interviewer"",
                                    ""limit"": 0,
                                    ""included"": true,
                                    ""_id"": ""67d7b9ec43d740ff541b4273""
                                },
                                {
                                    ""name"": ""MCQ Assessments"",
                                    ""limit"": 0,
                                    ""included"": true,
                                    ""_id"": ""67d7b9ec43d740ff541b4274""
                                },
                                {
                                    ""name"": ""Job Application"",
                                    ""limit"": 0,
                                    ""included"": true,
                                    ""_id"": ""67d7b9ec43d740ff541b4275""
                                }
                            ],
                            ""createdAt"": ""2025-03-01T12:36:43.242Z"",
                            ""updatedAt"": ""2025-03-17T05:58:04.448Z"",
                            ""__v"": 0
                        },
                        ""startDate"": 1741765168070,
                        ""endDate"": 1744357168070,
                        ""featureUsage"": [],
                        ""status"": 7,
                        ""isActivated"": true,
                        ""isDeleted"": false,
                        ""createdAt"": ""2025-03-12T07:39:28.244Z"",
                        ""updatedAt"": ""2025-03-12T07:39:28.244Z"",
                        ""__v"": 0
                    }
                ]
            }";

        return Content(jsonString, "application/json");
    }

}

