using System.Data;
using System.Diagnostics;
using System.Text.Json;
using AgoraIO.Media;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Configuration;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Web.Custom.Extensions.Models;
using Nop.Web.Factories;
using OpenAI.Chat;
using ILogger = Nop.Services.Logging.ILogger;



namespace Nop.Web.Controllers;

[Route("api/[controller]")]
[ApiController]
//[AllowAnonymous]
//[EnableCors("_myAllowSpecificOrigins")]
public class InterviewController : ControllerBase
{
    protected readonly ICustomerModelFactory _customerModelFactory;
    protected readonly ICustomerService _customerService;
    protected readonly IWorkContext _workContext;
    protected readonly IStoreContext _storeContext;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IShoppingCartModelFactory _shoppingCartModelFactory;
    protected readonly IProductService _productService;
    protected readonly IProductAttributeFormatter _productAttributeFormatter;
    protected readonly IDownloadService _downloadService;
    protected readonly IProductAttributeParser _productAttributeParser;
    protected readonly IProductAttributeService _productAttributeService;
    protected readonly ILocalizationService _localizationService;

    protected readonly AppSettings _appSettings;
    protected readonly ISettingService _settingService;
    protected readonly ILogger _logger;

    private const string AppId = "260ad2d30a7e4989958b5a44c72e3816";
    private const string AppCertificate = "YOUR_APP_CERTIFICATE";

    public InterviewController(ICustomerModelFactory customerModelFactory,
         ICustomerService customerService,
         IWorkContext workContext,
         IStoreContext storeContext,
         IShoppingCartService shoppingCartService,
         IShoppingCartModelFactory shoppingCartModelFactory,
         IProductService productService,
         IProductAttributeFormatter productAttributeFormatter,
         AppSettings appSettings,
         ISettingService settingService,
         IDownloadService downloadService,
         IProductAttributeParser productAttributeParser,
         IProductAttributeService productAttributeService,
         ILocalizationService localizationService,
         ILogger logger)
    {
        _customerModelFactory = customerModelFactory;
        _customerService = customerService;
        _workContext = workContext;
        _storeContext = storeContext;
        _shoppingCartService = shoppingCartService;
        _shoppingCartModelFactory = shoppingCartModelFactory;
        _productService = productService;
        _productAttributeFormatter = productAttributeFormatter;

        _appSettings = appSettings;
        _settingService = settingService;
        _downloadService = downloadService;
        _productAttributeParser = productAttributeParser;
        _productAttributeService = productAttributeService;
        _localizationService = localizationService;
        _logger = logger;
    }

    [HttpGet("continue/{id}")]
    public string Continue(int id)
    {
        Debug.WriteLine("This message will appear in the Output window.");
        return "value";
    }

    [HttpGet("user-interviews/{id}")]
    public IActionResult UserInterviews(string id)
    {
        Debug.WriteLine(id);

        var response = new
        {
            success = true,
            data = Array.Empty<string>()
        };

        return Ok(response);
    }

    [HttpPost("interview-invitation")]
    public IActionResult InterviewInvitation(JsonElement data)
    {
        return Ok();
    }

    [HttpPost("validate-custom-settings")]
    public IActionResult ValidateCustomSettings(JsonElement data)
    {
        return Ok();
    }


    [HttpPost("validate-custom-settings2")]
    public IActionResult ValidateCustomSettings2(JsonElement data)
    {
        return Ok();
    }

    [HttpPost("continue-interview_old")]
    public async Task<IActionResult> ContinueInterview_old([FromBody] ContinueInterviewModel model)
    {
        var maxInterviewQuestionsToAsk = 5;
        var isInterviewCompleted = false;
        var isRequestSuccess = true;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var customerName = $"{customer.FirstName} {customer.LastName}";

        var cartItemId = GetCartItemIdByInterviewId(model.InterviewId);
        var cartItem = await _shoppingCartService.GetShoppingCartItemByIdAsync(cartItemId);

        ArgumentNullException.ThrowIfNull(cartItem);

        var attributesXml = cartItem.AttributesXml;
        var rentalStartDate = DateTime.UtcNow;
        var existingInterviewInfo = cartItem.InterviewInfo;

        var newInterviewInfoToSave = string.Empty;
        var alreadyAskedQuestionIds = new List<int>();

        //serialize and then deserialize existing interview info
        if (!string.IsNullOrEmpty(existingInterviewInfo))
        {
            var lstInterviewInfo = JsonSerializer.Deserialize<List<Nop.Web.Custom.Extensions.Models.QuestionAnswer>>(existingInterviewInfo);

            if (lstInterviewInfo != null)
            {
                var interviewInfo = new Nop.Web.Custom.Extensions.Models.QuestionAnswer
                {
                    Id = lstInterviewInfo.Max(x => x.Id) + 1,
                    Question = model.Question.Replace("Here is your next question ", ""),
                    Answer = model.UserResponse,
                    QuestionId = string.IsNullOrEmpty(model.SourceQuestionId) ? 0 : Convert.ToInt32(model.SourceQuestionId)
                };

                lstInterviewInfo.Add(interviewInfo);
                newInterviewInfoToSave = JsonSerializer.Serialize(lstInterviewInfo);
                alreadyAskedQuestionIds = lstInterviewInfo.Select(x => x.QuestionId).ToList();
            }
        }
        else
        {
            // first interview question to user
            var lstInterviewInfoNew = new List<Nop.Web.Custom.Extensions.Models.QuestionAnswer>();
            var interviewInfo = new Nop.Web.Custom.Extensions.Models.QuestionAnswer
            {
                Id = 1,
                Question = model.Question == "" ? $"Hello {customerName}! . Can you introduce yourself ? " : "",
                Answer = model.UserResponse,
                QuestionId = 0,
            };

            lstInterviewInfoNew.Add(interviewInfo);

            newInterviewInfoToSave = JsonSerializer.Serialize(lstInterviewInfoNew);
            alreadyAskedQuestionIds = lstInterviewInfoNew.Select(x => x.QuestionId).ToList();
        }

        var warnings = await _shoppingCartService.UpdateShoppingCartItemCustomAsync(customer, cartItem.Id, attributesXml: cartItem.AttributesXml,
              customerEnteredPrice: 0,
              interviewInfo: newInterviewInfoToSave);

        var (skill, level) = await GetCurrentInterviewDetailsByShoppingCartItem(cartItem);

        var (questionToUser, questionId, interviewStatus) = await GenerateInterviewQuestionBySkill(skill, level, alreadyAskedQuestionIds);

        if (interviewStatus == "MaximumLimitReached")
        {
            isInterviewCompleted = true;
        }
        if (interviewStatus == "QuestionsNotFound")
        {
            //return StatusCode((int)HttpStatusCode.InternalServerError, new { message = "Questions not found for the Skill and Level you selected." });
            isRequestSuccess = false;
        }
        if (interviewStatus == "QuestionsExhausted")
        {
            isInterviewCompleted = true;
            //return StatusCode((int)HttpStatusCode.InternalServerError, new { message = "Internal Server Error" });
        }
        if (interviewStatus == "Success")
        {
            questionToUser = "Here is your next question " + questionToUser;
        }

        var response = new
        {
            success = isRequestSuccess,
            data = new
            {
                body = new
                {
                    success = "true",
                    content = questionToUser,
                    sourceQuestionId = questionId,
                },
                content = "real",
                interviewDone = isInterviewCompleted,
                _id = 1,
                interviewId = 1
            },
            message = "Continue Interview - api response"
        };

        var jsonString = JsonSerializer.Serialize(response);
        return Content(jsonString, "application/json");
    }

    [HttpPost("new-interview_old")]
    public async Task<IActionResult> NewInterview_old([FromBody] NewInterviewModel data, int model = 3, string id = "")
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var customerName = $"{customer.FirstName} {customer.LastName}";

        var cartItemId = GetCartItemIdByInterviewId(id);
        var cartItem = await _shoppingCartService.GetShoppingCartItemByIdAsync(cartItemId);
        var shoppingCartItemId = cartItem.Id;

        ArgumentNullException.ThrowIfNull(cartItem);

        var attributesXml = cartItem.AttributesXml;
        var rentalStartDate = DateTime.UtcNow;

        var warnings = await _shoppingCartService.UpdateShoppingCartItemCustomAsync(customer, cartItem.Id, attributesXml: attributesXml,
              customerEnteredPrice: 0,
              rentalStartDate: rentalStartDate,
              interviewStatus: "Started",
              interviewInfo: "");

        var (skill, level) = await GetCurrentInterviewDetailsByShoppingCartItem(cartItem);

        var firstQuestion = "Can you introduce your self ?";
        var introductionMessage = "This is introduction message";

        var response = new
        {
            success = "true",
            data = new
            {
                result = new
                {
                    interviewType = "real",
                    _id = $"{customer.CustomerGuid}_{shoppingCartItemId}",
                    title = "Resume Based Interview",
                    id = $"{customer.CustomerGuid}_{shoppingCartItemId}",
                    interviewId = $"{customer.CustomerGuid}_{shoppingCartItemId}",
                    model = 3,
                    description = "Efficient Initial Screening: A resume interview efficiently screens candidates based on their resume, providing a quick assessment of qualifications and potential alignment with the job.",
                    name = $"{customer.FirstName} {customer.LastName}",
                    companyurl = "",
                    technology = skill,
                    levelOfInterview = level,
                    noOfQuestions = 5,
                    role = "",
                    company = "",
                    customQuestions = Array.Empty<string>(),
                    resume = ""
                },
                interviewResponse = new
                {
                    body = new
                    {
                        content = $"Hello {customerName}! {firstQuestion}"
                    },
                    systemQuestion = introductionMessage
                }
            },
            message = "You have enough credits(179) to start the interview - api response"
        };

        var jsonString = JsonSerializer.Serialize(response);

        return Content(jsonString, "application/json");
    }

    [HttpPut("update-interview/{id}")]
    public async Task<IActionResult> UpdateInterview([FromBody] UpdateInterviewModel model, string id)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();

        var cartItemId = GetCartItemIdByInterviewId(id);
        var cartItem = await _shoppingCartService.GetShoppingCartItemByIdAsync(cartItemId);

        ArgumentNullException.ThrowIfNull(cartItem);

        var attributesXml = cartItem.AttributesXml;

        DateTime? rentalEndDate = null;
        DateTime? rentalStartDate = null;
        var interviewStatus = cartItem.InterviewStatus;

        if (model.IsInterviewCompleted.HasValue && (bool)model.IsInterviewCompleted)
        {
            rentalEndDate = DateTime.UtcNow;
            interviewStatus = "Completed";
        }
        if (!string.IsNullOrEmpty(model.Recording))
        {
            rentalStartDate = cartItem.RentalStartDateUtc;
            rentalEndDate = cartItem.RentalEndDateUtc;
        }

        //DateTime myDate = new DateTime(Convert.ToInt64(model.InterviewCompletedAt));

        var warnings = await _shoppingCartService.UpdateShoppingCartItemCustomAsync(customer, cartItemId, attributesXml: attributesXml,
              customerEnteredPrice: 0,
              rentalEndDate: rentalEndDate,
              interviewStatus: interviewStatus,
              interviewInfo: cartItem.InterviewInfo,
              recordingURL: model.Recording);

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

        return Content(jsonString, "application/json");
    }

    [HttpPost("result")]
    public IActionResult InterviewResult([FromBody] InterviewResultModel model)
    {
        return Ok();
    }

    [HttpPost("resume-interview/{id}")]
    public IActionResult ResumeInterview(JsonElement data)
    {
        return Ok();
    }

    [HttpGet("get-interview-by-id/{id}")]
    public IActionResult GetInterviewById(string id)
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
            data = new
            {
                success = "true",
                recording = id, //recordingid
                jobPostId = id,
                isResultPublished = "true",
                technology = "python",
                createdAt = System.DateTime.Now,

                result = topics,
                questions = topics,
                interviewee = new
                {
                    name = "sateesh Interview Controller"
                }
            }
        };

        var jsonString = JsonSerializer.Serialize(response);

        return Content(jsonString, "application/json");
    }

    #region Agora methods

    [HttpPost("new-interview")]
    public async Task<IActionResult> NewInterview([FromBody] NewInterviewModel request)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();

        await _logger.InformationAsync($"Interview Started for customer: {customer.Email}", customer: customer);

        var cartItemId = Convert.ToInt32(request.InterviewId);
        var cartItem = await _shoppingCartService.GetShoppingCartItemByIdAsync(cartItemId);
        var shoppingCartItemId = cartItem.Id;

        ArgumentNullException.ThrowIfNull(cartItem);

        var attributesXml = cartItem.AttributesXml;
        var rentalStartDate = DateTime.UtcNow;

        var (skill, level) = await GetCurrentInterviewDetailsByShoppingCartItem(cartItem);

        var (questions, firstGeneratedQuestion) = await GenerateInterviewQuestionBySkillAsJsonAsync(skill, level);

        var warnings = await _shoppingCartService.UpdateShoppingCartItemCustomAsync(customer, cartItem.Id, attributesXml: attributesXml,
              customerEnteredPrice: 0,
              rentalStartDate: rentalStartDate,
              interviewStatus: "Started",
              interviewInfo: questions);

        var firstQuestion = $"Hello {customer.FirstName}! {firstGeneratedQuestion.Question} ?";

        var response = new
        {
            questionId = firstGeneratedQuestion.Id,
            question = firstQuestion,
            interviewDone = false
        };

        var jsonString = JsonSerializer.Serialize(response);

        return Content(jsonString, "application/json");
    }

    [HttpPost("continue-interview")]
    public async Task<IActionResult> ContinueInterview([FromBody] ContinueInterviewModel model)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var customerName = $"{customer.FirstName} {customer.LastName}";

        //model.InterviewId = "1119";
        var cartItemId = Convert.ToInt32(model.InterviewId);
        var cartItem = await _shoppingCartService.GetShoppingCartItemByIdAsync(cartItemId);

        ArgumentNullException.ThrowIfNull(cartItem);

        var attributesXml = cartItem.AttributesXml;

        // Step 1: Load JSON from DB
        var existingInterviewInfo = cartItem.InterviewInfo;
        var questionList = JsonSerializer.Deserialize<List<Nop.Web.Custom.Extensions.Models.QuestionAnswer>>(existingInterviewInfo);

        // Step 2: Update the answered question
        int answeredQuestionId = model.Id;
        string userAnswer = model.Answer;
        int rating = 0, relevancy = 0, communication = 0;

        var answeredItem = questionList.FirstOrDefault(q => q.Id == answeredQuestionId);
        if (answeredItem != null)
        {
            answeredItem.Answer = userAnswer;
            answeredItem.AnswerRating = rating;
            answeredItem.AnswerRelavancy = relevancy;
            answeredItem.CommunicationRating = communication;
        }

        // Step 3: Save updated JSON back to DB
        string updatedJson = JsonSerializer.Serialize(questionList);
        var warnings = await _shoppingCartService.UpdateShoppingCartItemCustomAsync(customer, cartItem.Id, attributesXml: cartItem.AttributesXml,
                      customerEnteredPrice: 0,
                      interviewInfo: updatedJson);

        // Step 4: Fetch the next unanswered question
        var nextQuestion = questionList
            .Where(q => string.IsNullOrWhiteSpace(q.Answer))
            .OrderBy(q => q.Id) // Maintain original order
            .FirstOrDefault();

        int questionId = 0;
        string nextQuestionToAsk = "";
        bool isInterviewCompleted = false;

        if (nextQuestion != null)
        {
            questionId = nextQuestion.Id;
            nextQuestionToAsk = nextQuestion.Question;
        }
        else
        {
            //mark interview completed
            await _shoppingCartService.UpdateShoppingCartItemCustomAsync(customer, cartItem.Id, attributesXml: cartItem.AttributesXml,
                customerEnteredPrice: 0, interviewStatus: "Completed", interviewInfo: updatedJson);

            await _logger.InformationAsync($"Interview Completed for customer: {customer.Email}", customer: await _workContext.GetCurrentCustomerAsync());
            isInterviewCompleted = true;
        }

        var response = new
        {
            questionId = questionId,
            question = nextQuestionToAsk,
            interviewDone = isInterviewCompleted
        };

        // Serialize the anonymous object to a JSON string and return it.
        var jsonString = JsonSerializer.Serialize(response);

        return Content(jsonString, "application/json");
    }

    [HttpPost("complete-interview")]
    public async Task<IActionResult> CompleteInterview([FromBody] ContinueInterviewModel model)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();

        var cartItemId = Convert.ToInt32(model.InterviewId);
        var cartItem = await _shoppingCartService.GetShoppingCartItemByIdAsync(cartItemId);

        ArgumentNullException.ThrowIfNull(cartItem);

        var attributesXml = cartItem.AttributesXml;

        DateTime? rentalEndDate = null;
        var rentalStartDate = cartItem.RentalStartDateUtc;
        var interviewStatus = cartItem.InterviewStatus;

        if (!string.IsNullOrEmpty(model.RecordingURL))
        {
            rentalEndDate = cartItem.RentalEndDateUtc;
        }

        //mark interview completed and save recording url
        var warnings = await _shoppingCartService.UpdateShoppingCartItemCustomAsync(customer, cartItemId, attributesXml: cartItem.AttributesXml,
              customerEnteredPrice: 0,
              rentalEndDate: cartItem.RentalEndDateUtc,
              interviewStatus: interviewStatus,
              interviewInfo: cartItem.InterviewInfo,
              recordingURL: model.RecordingURL,
              cartType: ShoppingCartType.Interview);

        return Ok();
    }

    public async Task<(string, Custom.Extensions.Models.QuestionAnswer)> GenerateInterviewQuestionBySkillAsJsonAsync(string skill, string level)
    {
        var store = await _storeContext.GetCurrentStoreAsync();

        var interViewSatusOrWarning = string.Empty;
        var maxInterviewQuestionsToAsk = Convert.ToInt32((await _settingService.GetSettingAsync("interviewsettings.maxinterviewquestionstoask", store.Id)).Value);

        // Step 1: Load all questions from DB or source
        var settingName = $"interviewsettings.{skill.ToLower()}questions";
        var skillQuestions = await _settingService.GetSettingAsync(settingName, store.Id);
        var questions = JsonSerializer.Deserialize<List<InterviewQuestions>>(skillQuestions.Value);
        var questionsByLevel = questions.Where(x => x.Level.Equals(level, StringComparison.CurrentCultureIgnoreCase)).ToList();

        // Step 2: Randomly select 5 or 10 unique questions
        //int numberOfQuestionsToAsk = 5;
        var selectedQuestions = questionsByLevel
            .OrderBy(q => Guid.NewGuid()) // Shuffle
            .Take(maxInterviewQuestionsToAsk)
            .ToList();

        // Step 3: Map to your JSON structure
        var jsonList = selectedQuestions.Select((q, index) => new Custom.Extensions.Models.QuestionAnswer
        {
            Id = index + 1,
            QuestionId = q.Id,
            Question = q.Question,
            Answer = "",
            AnswerRating = 0,
            AnswerRelavancy = 0,
            CommunicationRating = 0
        }).ToList();

        // Step 4: Serialize to JSON
        string jsonString = JsonSerializer.Serialize(jsonList);
        return (jsonString, jsonList.First());
    }

    public static string GenerateRtcTokenWithUid(string channelName, uint uid, uint expireInSeconds = 7200)
    {
        if (string.IsNullOrEmpty(channelName))
            throw new ArgumentException("channelName must not be null or empty.");

        // Calculate privilege expiration timestamp (Unix time in seconds)
        uint currentTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        uint privilegeExpiredTs = currentTimestamp + expireInSeconds;

        // Role: Publisher = can send audio/video
        var role = RtcTokenBuilder.Role.RolePublisher;
        // In some versions, role might be an enum like Role.PUBLISHER, adjust accordingly

        // Build token
        // This method should come from the AgoraDynamicKey library (or your port of it)
        string token = RtcTokenBuilder.buildTokenWithUID(
            AppId,
            AppCertificate,
            channelName,
            uid,
            role,
            privilegeExpiredTs
        );

        return token;
    }

    #endregion

    #region OpenAI methods

    public ChatClient CreateAnAzureOpenAIClient()
    {
        var endpoint = new Uri("https://satee-m4sj22jh-eastus2.openai.azure.com/");
        var deploymentName = "gpt-4o-mini";
        var model = "gpt-4o-mini";

        string AZURE_OPENAI_API_KEY = "YOUR_AZURE_AI_KEY_PLACEHOLDER";

        AzureOpenAIClient azureClient = new(endpoint, new AzureKeyCredential(AZURE_OPENAI_API_KEY));

        ChatClient chatClient = azureClient.GetChatClient(deploymentName);

        return chatClient;
    }

    #endregion

    public int GetCartItemIdByInterviewId(string id)
    {
        if (!string.IsNullOrEmpty(id))
            return Convert.ToInt32(id.Split("_")[1]);

        return 0;
    }

    public async Task<(string, int, string)> GenerateInterviewQuestionBySkill(string skill, string level, List<int> alreadyAskedQuestionIds)
    {
        var store = await _storeContext.GetCurrentStoreAsync();

        var interViewSatusOrWarning = string.Empty;
        var maxInterviewQuestionsToAsk = Convert.ToInt32((await _settingService.GetSettingAsync("interviewsettings.maxinterviewquestionstoask", store.Id)).Value);

        if (maxInterviewQuestionsToAsk == alreadyAskedQuestionIds.Count)
        {
            //maximum questions asked
            return (string.Empty, 0, "MaximumLimitReached");
        }

        var settingName = $"interviewsettings.{skill.ToLower()}questions";
        var skillQuestions = await _settingService.GetSettingAsync(settingName, store.Id);
        var questions = JsonSerializer.Deserialize<List<InterviewQuestions>>(skillQuestions.Value);
        var questionsByLevel = questions.Where(x => x.Level.Equals(level, StringComparison.CurrentCultureIgnoreCase)).ToList();

        var generatedNumbers = new List<int>();
        var random = new Random();
        int randomNumber;
        var rangeExhausted = false;

        do
        {
            var minRange = questionsByLevel.OrderBy(x => x.Id).First().Id;
            var maxRange = questionsByLevel.OrderByDescending(x => x.Id).First().Id;

            var noOfQuestions = questionsByLevel.Count;
            randomNumber = random.Next(minRange, maxRange + 1);

            generatedNumbers.Add(randomNumber);
            generatedNumbers.Add(0); // add 0 because askedQuestionIds contains 0

            // Sort both lists to compare their contents regardless of order
            var sortedInitial = alreadyAskedQuestionIds.Distinct().OrderBy(n => n).ToList();
            var sortedGenerated = generatedNumbers.Distinct().OrderBy(n => n).ToList();

            if (sortedInitial.SequenceEqual(sortedGenerated))
            {
                if (alreadyAskedQuestionIds.Count == maxInterviewQuestionsToAsk)
                {
                    //Generated numbers list is the same as the initial numbers list. Range exhausted. Breaking the loop
                    rangeExhausted = true;
                    break;
                }
                else
                {
                    randomNumber = 0; // Reset randomNumber to avoid returning a number that is already asked
                }
            }
            // Optional safeguard
            if (generatedNumbers.Count > noOfQuestions + 10)
            {
                //Safeguard triggered: Generated a large number of values without containing all unique initial numbers. Breaking
                break;
            }

        } while (alreadyAskedQuestionIds.Contains(randomNumber));

        Console.WriteLine($"Finally, {randomNumber} is not present in the list!");

        if (rangeExhausted)
            return (string.Empty, 0, "QuestionsExhausted");

        var singleQuestion = questionsByLevel.FirstOrDefault(x => x.Id == randomNumber);

        if (singleQuestion is null)
            return (string.Empty, 0, "QuestionsNotFound");

        return (singleQuestion?.Question ?? string.Empty, singleQuestion.Id, "Success");
    }

    public async Task<(string, string)> GetCurrentInterviewDetailsByShoppingCartItem(ShoppingCartItem cartItem)
    {
        var currentLanguage = await _workContext.GetWorkingLanguageAsync();
        string resumeURL = null;

        var product = await _productService.GetProductByIdAsync(cartItem.ProductId);
        var attributeInfo = await _productAttributeFormatter.FormatAttributesAsync(product, cartItem.AttributesXml);

        var data = attributeInfo.Split(new[] { "<br />" }, StringSplitOptions.RemoveEmptyEntries);

        var skill = data[0].Split(new[] { ":" }, StringSplitOptions.RemoveEmptyEntries)[1].Trim();
        var level = data[1].Split(new[] { ":" }, StringSplitOptions.RemoveEmptyEntries)[1].Trim();

        if (data.Length > 2 && data[2].Contains(':'))
            resumeURL = data[2].Split(new[] { ":" }, 2, StringSplitOptions.RemoveEmptyEntries)[1].Trim();

        // Optional: log or validate resumeURL
        if (string.IsNullOrWhiteSpace(resumeURL))
            resumeURL = "No resume uploaded";

        var attributeValues = await _productAttributeParser.ParseProductAttributeMappingsAsync(cartItem.AttributesXml);

        foreach (var attributeValue in attributeValues)
        {
            if (attributeValue.AttributeControlType == AttributeControlType.FileUpload)
            {
                //attributeValue.
                //var downloadId = Convert.ToInt32(attrValue.Value);
                //var download = _downloadService.GetDownloadById(downloadId);
                // You now have access to the file metadata and binary
            }
        }

        foreach (var attribute in await _productAttributeParser.ParseProductAttributeMappingsAsync(cartItem.AttributesXml))
        {
            var productAttribute = await _productAttributeService.GetProductAttributeByIdAsync(attribute.ProductAttributeId);
            var attributeName = await _localizationService.GetLocalizedAsync(productAttribute, a => a.Name, currentLanguage.Id);

            //attributes without values
            if (!attribute.ShouldHaveValues())
            {
                foreach (var value in _productAttributeParser.ParseValues(cartItem.AttributesXml, attribute.Id))
                {
                    if (attribute.AttributeControlType == AttributeControlType.FileUpload)
                    {
                        //file upload
                        _ = Guid.TryParse(value, out var downloadGuid);
                        var download = await _downloadService.GetDownloadByGuidAsync(downloadGuid);
                        if (download != null)
                        {
                            var fileName = $"{download.Filename ?? download.DownloadGuid.ToString()}{download.Extension}";
                        }
                    }
                }
            }
        }

        return (skill, level);
    }
}
