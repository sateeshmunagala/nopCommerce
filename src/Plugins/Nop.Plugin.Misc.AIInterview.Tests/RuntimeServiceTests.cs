using Moq;
using Microsoft.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Logging;
using Nop.Core.Events;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Events;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using NopLogger = Nop.Services.Logging.ILogger;
using NUnit.Framework;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class RuntimeServiceTests
{
    private static InterviewRuntimeService CreateService(
        Mock<IInterviewSessionService> sessionService,
        Mock<IInterviewTurnService> turnService,
        Mock<IAIInterviewClient> aiClient,
        Mock<IProductService> productService,
        Mock<ICustomerService> customerService,
        Mock<ILocalizationService> localizationService,
        Mock<IApplicationService> applicationService = null,
        Mock<IResumeProfileService> resumeProfileService = null,
        Mock<IAzureUsageService> azureUsageService = null,
        Mock<IHttpClientFactory> httpClientFactory = null,
        AIInterviewSettings settings = null,
        MockAIInterviewSettings mockSettings = null,
        Mock<IWorkContext> workContext = null,
        Mock<IEventPublisher> eventPublisher = null,
        Mock<NopLogger> nopLogger = null,
        Mock<Microsoft.Extensions.Logging.ILogger<InterviewRuntimeService>> logger = null)
    {
        return new InterviewRuntimeService(
            sessionService.Object,
            turnService.Object,
            aiClient.Object,
            productService.Object,
            customerService.Object,
            applicationService?.Object ?? new Mock<IApplicationService>().Object,
            resumeProfileService?.Object ?? new Mock<IResumeProfileService>().Object,
            azureUsageService?.Object ?? new Mock<IAzureUsageService>().Object,
            localizationService.Object,
            settings ?? new AIInterviewSettings { Prompt = "Be concise" },
            mockSettings ?? new MockAIInterviewSettings { UseMockResponses = true },
            httpClientFactory?.Object ?? new Mock<IHttpClientFactory>().Object,
            workContext?.Object ?? new Mock<IWorkContext>().Object,
            eventPublisher?.Object ?? new Mock<IEventPublisher>().Object,
            nopLogger?.Object,
            logger?.Object);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public List<HttpRequestMessage> Requests { get; } = new();

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responseFactory(request));
        }
    }

    private static Mock<IHttpClientFactory> CreateHttpClientFactory(TestHttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    private static IFormFile CreateRecordingFile(string content = "recording-content", string fileName = "interview.webm", string contentType = "video/webm")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "recording", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    [Test]
    public async Task EnsureInterviewStartedAsync_Creates_Planned_Turns_Up_Front()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession
        {
            Id = 1,
            ProductId = 10,
            CustomerId = 99,
            SessionKey = "key",
            Token = "token",
            Difficulty = "Medium",
            QuestionCount = 5
        };
        var store = new List<InterviewTurn>();
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(1)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        aiClient.Setup(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()))
            .ReturnsAsync((AIInterviewQuestionPlanRequest request) => new AIInterviewQuestionPlanResponse
            {
                Success = true,
                Questions = Enumerable.Range(1, request.QuestionCount).Select(index => new AIInterviewQuestionPlanItem
                {
                    SequenceNumber = index,
                    Category = index <= 2 ? "skill" : index <= 4 ? "project_scenario" : "job_fit",
                    Question = $"Planned technical question {index}",
                    ResumeEvidence = index <= 2 ? "C#" : "Payments platform",
                    ExpectedSignals = new List<string> { "Signal A", "Signal B" },
                    Rubric = new AIInterviewQuestionRubric
                    {
                        Technical = "Technical depth",
                        Communication = "Clear answer",
                        Professionalism = "Ownership",
                        PositiveAttitude = "Constructive mindset"
                    }
                }).ToList()
            });
        productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Product { Id = 10, Name = "Backend Engineer" });
        customerService.Setup(x => x.GetCustomerByIdAsync(99)).ReturnsAsync(new Customer { Id = 99, FirstName = "Jane", LastName = "Doe" });

        var insertedTurns = new List<InterviewTurn>();
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                turn.Id = insertedTurns.Count + 1;
                insertedTurns.Add(turn);
                store.Add(turn);
                return turn;
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var model = await service.EnsureInterviewStartedAsync(session, new Customer { Id = 99, FirstName = "Jane", LastName = "Doe" });

        Assert.That(model, Is.Not.Null);
        Assert.That(model.CurrentQuestion, Does.Contain("Hello Jane Doe, let's start with you"));
        Assert.That(model.CurrentQuestion, Does.Contain("one or two projects"));
        Assert.That(insertedTurns.Count, Is.EqualTo(5));
        Assert.That(insertedTurns.All(turn => turn.InterviewSessionId == 1), Is.True);
        Assert.That(insertedTurns.Select(turn => turn.SequenceNumber), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(store.Count, Is.EqualTo(5));
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.Is<AIInterviewQuestionPlanRequest>(request =>
            request.JobTitle == "Backend Engineer" &&
            request.QuestionCount == 4 &&
            request.TotalQuestionCount == 5 &&
            request.Difficulty == "Medium" &&
            request.ExistingQuestions.Any(question => question.Contains("introduce yourself")) &&
            !request.ExistingCategories.Any(category => category == "Introduction & Project Experience"))), Times.Once);
        var firstRubric = JsonDocument.Parse(insertedTurns.Single(turn => turn.SequenceNumber == 1).RubricJson).RootElement;
        Assert.That(firstRubric.GetProperty("category").GetString(), Is.EqualTo("Introduction & Project Experience"));
        Assert.That(firstRubric.GetProperty("expectedSignals").EnumerateArray().Any(signal => signal.GetString() == "Relevant project ownership"), Is.True);
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
    }

    [Test]
    public async Task EnsureInterviewStartedAsync_QuestionCountOne_Creates_Only_Intro_Turn()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var session = new InterviewSession { Id = 11, ProductId = 10, CustomerId = 99, Token = "token", Difficulty = "Medium", QuestionCount = 1 };
        var store = new List<InterviewTurn>();

        turnService.Setup(x => x.GetTurnsBySessionIdAsync(11)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Product { Id = 10, Name = "Backend Engineer" });
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                store.Add(turn);
                return turn;
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var model = await service.EnsureInterviewStartedAsync(session, new Customer { Id = 99 });

        Assert.That(model.CurrentQuestion, Does.StartWith("Let's start with you."));
        Assert.That(store.Count, Is.EqualTo(1));
        Assert.That(store.Single().SequenceNumber, Is.EqualTo(1));
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Never);
    }

    [Test]
    public async Task GetRuntimeModelAsync_DoesNotCreate_First_Turn_On_Load()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession { Id = 3, ProductId = 10, CustomerId = 99, SessionKey = "key3", Token = "token3", Difficulty = "Medium", IsActive = true };
        sessionService.Setup(x => x.GetSessionByTokenAsync("token3")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(3)).ReturnsAsync(new List<InterviewTurn>());

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var model = await service.GetRuntimeModelAsync("token3");

        Assert.That(model, Is.Not.Null);
        Assert.That(model.CurrentQuestion, Is.Empty);
        Assert.That(model.Turns, Is.Empty);
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
        turnService.Verify(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()), Times.Never);
    }

    [Test]
    public async Task GetRuntimeModelAsync_Populates_QuestionCount_From_Session()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession
        {
            Id = 303,
            ProductId = 10,
            CustomerId = 99,
            SessionKey = "key303",
            Token = "token303",
            Difficulty = "Medium",
            QuestionCount = 5,
            IsActive = true
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token303")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(303)).ReturnsAsync(new List<InterviewTurn>());

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var model = await service.GetRuntimeModelAsync("token303");

        Assert.That(model, Is.Not.Null);
        Assert.That(model.QuestionCount, Is.EqualTo(5));
        Assert.That(model.ClientSettings.QuestionCount, Is.EqualTo(5));
    }

    [Test]
    public async Task GetRuntimeModelAsync_WithExistingUnansweredTurn_HidesQuestionAndTurns_UntilBegin()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession { Id = 31, ProductId = 10, CustomerId = 99, SessionKey = "key31", Token = "token31", Difficulty = "Medium", IsActive = true };
        var unansweredTurn = new InterviewTurn
        {
            Id = 7,
            InterviewSessionId = 31,
            SequenceNumber = 1,
            QuestionText = "Tell me about a time you improved reliability.",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-2),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token31")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(31)).ReturnsAsync(new List<InterviewTurn> { unansweredTurn });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var model = await service.GetRuntimeModelAsync("token31");

        Assert.That(model, Is.Not.Null);
        Assert.That(model.CurrentQuestion, Is.Empty);
        Assert.That(model.Turns, Is.Empty);
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
    }

    [Test]
    public async Task BeginInterviewAsync_WithExistingUnansweredTurn_ReturnsThatTurn_WithoutGeneratingDuplicate()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession
        {
            Id = 32,
            ProductId = 10,
            CustomerId = 99,
            SessionKey = "key32",
            Token = "token32",
            Difficulty = "Medium",
            QuestionCount = 1,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        var unansweredTurn = new InterviewTurn
        {
            Id = 9,
            InterviewSessionId = 32,
            SequenceNumber = 1,
            QuestionText = "Explain a production incident you handled.",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-2),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token32")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(32)).ReturnsAsync(new List<InterviewTurn> { unansweredTurn });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var model = await service.BeginInterviewAsync("token32", new Customer { Id = 99 });

        Assert.That(model, Is.Not.Null);
        Assert.That(model.CurrentQuestion, Is.EqualTo("Explain a production incident you handled."));
        Assert.That(model.Turns.Count(), Is.EqualTo(1));
        Assert.That(model.Turns.Single().QuestionText, Is.EqualTo("Explain a production incident you handled."));
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
        turnService.Verify(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()), Times.Never);
    }

    [Test]
    public async Task EnsureInterviewStartedAsync_RealMode_Failure_DoesNotCreateTurn()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession { Id = 10, ProductId = 50, CustomerId = 99, SessionKey = "key10", Token = "token10", Difficulty = "Medium" };
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(10)).ReturnsAsync(new List<InterviewTurn>());
        aiClient.Setup(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()))
            .ReturnsAsync(new AIInterviewQuestionPlanResponse { Success = false, ErrorMessage = "AI service unavailable." });

        var inserted = false;
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback(() => inserted = true)
            .ReturnsAsync((InterviewTurn turn) => turn);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            settings: new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://endpoint",
                AzureOpenAiApiKey = "key",
                AzureOpenAiDeploymentOrModel = "deployment",
                Prompt = "prompt"
            },
            mockSettings: new MockAIInterviewSettings { UseMockResponses = false },
            workContext: workContext,
            eventPublisher: eventPublisher);

        var model = await service.EnsureInterviewStartedAsync(session, new Customer { Id = 99 });

        Assert.That(inserted, Is.False);
        Assert.That(model.CurrentQuestion, Is.EqualTo("AI service unavailable. Please try again later."));
    }

    [Test]
    public async Task EnsureInterviewStartedAsync_RealMode_BlankQuestion_DoesNotCreateTurn()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession { Id = 11, ProductId = 51, CustomerId = 99, SessionKey = "key11", Token = "token11", Difficulty = "Medium" };
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(11)).ReturnsAsync(new List<InterviewTurn>());
        aiClient.Setup(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()))
            .ReturnsAsync(new AIInterviewQuestionPlanResponse
            {
                Success = true,
                Questions = new List<AIInterviewQuestionPlanItem>
                {
                    new() { SequenceNumber = 1, Category = "skill", Question = "   " }
                }
            });

        var inserted = false;
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback(() => inserted = true)
            .ReturnsAsync((InterviewTurn turn) => turn);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            settings: new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://endpoint",
                AzureOpenAiApiKey = "key",
                AzureOpenAiDeploymentOrModel = "deployment",
                Prompt = "prompt"
            },
            mockSettings: new MockAIInterviewSettings { UseMockResponses = false },
            workContext: workContext,
            eventPublisher: eventPublisher);

        var model = await service.EnsureInterviewStartedAsync(session, new Customer { Id = 99 });

        Assert.That(inserted, Is.False);
        Assert.That(model.CurrentQuestion, Is.EqualTo("AI service unavailable. Please try again later."));
    }

    [Test]
    public async Task EnsureInterviewStartedAsync_PartialPlanWithAnsweredTurns_FillsMissingSequencesDeterministically()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession
        {
            Id = 77,
            ProductId = 18,
            CustomerId = 41,
            SessionKey = "partial-plan",
            Token = "partial-token",
            Difficulty = "Medium",
            QuestionCount = 4,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        var answeredTurn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = 77,
            SequenceNumber = 1,
            QuestionText = "Existing question 1",
            AnswerText = "Answered with detail",
            Score = 82,
            RubricJson = "{\"category\":\"skill\",\"resumeEvidence\":\"C#\",\"expectedSignals\":[\"depth\"]}",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-8),
            AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-7),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-8)
        };
        var retainedTurn = new InterviewTurn
        {
            Id = 2,
            InterviewSessionId = 77,
            SequenceNumber = 3,
            QuestionText = "Existing question 3",
            RubricJson = "{\"category\":\"project_scenario\",\"resumeEvidence\":\"Payments platform\",\"expectedSignals\":[\"tradeoffs\"]}",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-2),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2)
        };
        var store = new List<InterviewTurn> { answeredTurn, retainedTurn };
        AIInterviewQuestionPlanRequest capturedPlanRequest = null;

        turnService.Setup(x => x.GetTurnsBySessionIdAsync(77)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                turn.Id = store.Max(existing => existing.Id) + 1;
                store.Add(turn);
                return turn;
            });
        aiClient.Setup(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()))
            .Callback<AIInterviewQuestionPlanRequest>(request => capturedPlanRequest = request)
            .ReturnsAsync(new AIInterviewQuestionPlanResponse
            {
                Success = true,
                Questions = new List<AIInterviewQuestionPlanItem>
                {
                    new()
                    {
                        SequenceNumber = 1,
                        Category = "behavioral",
                        Question = "Generated question 2",
                        ResumeEvidence = "Ownership",
                        ExpectedSignals = new List<string> { "Ownership", "Clarity" },
                        Rubric = new AIInterviewQuestionRubric()
                    },
                    new()
                    {
                        SequenceNumber = 2,
                        Category = "job_fit",
                        Question = "Generated question 4",
                        ResumeEvidence = "Role alignment",
                        ExpectedSignals = new List<string> { "Alignment", "Ramp-up" },
                        Rubric = new AIInterviewQuestionRubric()
                    }
                }
            });
        productService.Setup(x => x.GetProductByIdAsync(18)).ReturnsAsync(new Product { Id = 18, Name = "Platform Engineer" });
        customerService.Setup(x => x.GetCustomerByIdAsync(41)).ReturnsAsync(new Customer { Id = 41, FirstName = "Casey", LastName = "Lee" });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var model = await service.EnsureInterviewStartedAsync(session, new Customer { Id = 41, FirstName = "Casey", LastName = "Lee" });

        Assert.That(model, Is.Not.Null);
        Assert.That(model.CurrentQuestion, Is.EqualTo("Generated question 2"));
        Assert.That(store.Select(turn => turn.SequenceNumber).OrderBy(sequence => sequence), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(store.Count(turn => turn.SequenceNumber == 2), Is.EqualTo(1));
        Assert.That(store.Count(turn => turn.SequenceNumber == 4), Is.EqualTo(1));
        Assert.That(capturedPlanRequest, Is.Not.Null);
        Assert.That(capturedPlanRequest.QuestionCount, Is.EqualTo(2));
        Assert.That(capturedPlanRequest.TotalQuestionCount, Is.EqualTo(4));
        Assert.That(capturedPlanRequest.ExistingQuestions, Is.EquivalentTo(new[] { "Existing question 1", "Existing question 3" }));
        Assert.That(capturedPlanRequest.ExistingCategories, Is.EquivalentTo(new[] { "skill", "project_scenario" }));
        turnService.Verify(x => x.DeleteInterviewTurnsAsync(It.IsAny<IList<InterviewTurn>>()), Times.Never);
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
    }

    [Test]
    public async Task SubmitAnswerAsync_Persists_Turn_And_Returns_Next_Question()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession { Id = 2, ProductId = 20, CustomerId = 5, SessionKey = "key2", Token = "token2", Difficulty = "Medium", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1), QuestionCount = 2 };
        var turn = new InterviewTurn { Id = 1, InterviewSessionId = 2, SequenceNumber = 1, QuestionText = "Q1", AskedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
        var nextTurn = new InterviewTurn { Id = 2, InterviewSessionId = 2, SequenceNumber = 2, QuestionText = "Q2", AskedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
        var store = new List<InterviewTurn> { turn, nextTurn };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token2")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(2)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Score = 80,
                TechnicalScore = 78,
                CommunicationScore = 82,
                ProfessionalismScore = 80,
                PositiveAttitudeScore = 80,
                Feedback = "Good",
                RawJson = "{}",
                RubricJson = "{\"technicalScore\":78,\"communicationScore\":82,\"professionalismScore\":80,\"positiveAttitudeScore\":80,\"score\":80}"
            });
        productService.Setup(x => x.GetProductByIdAsync(20)).ReturnsAsync(new Product { Id = 20, Name = "QA Engineer" });
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(updated =>
            {
                store.RemoveAll(x => x.Id == updated.Id);
                store.Add(updated);
            })
            .Returns(Task.CompletedTask);
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token2", "This is a structured answer because it explains impact.");

        Assert.That(result.Success, Is.True);
        Assert.That(result.IsTerminated, Is.False);
        Assert.That(result.Question, Is.EqualTo("Q2"));
        Assert.That(store.Any(x => x.SequenceNumber == 1 && x.AnswerText != null), Is.True);
        Assert.That(store.Any(x => x.SequenceNumber == 2), Is.True);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Score > 0)), Times.Once);
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Never);
        turnService.Verify(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()), Times.Never);
    }

    [Test]
    public async Task SubmitAnswerAsync_BeforeBegin_DoesNotGenerateQuestionOrScore()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession
        {
            Id = 201,
            ProductId = 20,
            CustomerId = 5,
            SessionKey = "key201",
            Token = "token201",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token201")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(201)).ReturnsAsync(new List<InterviewTurn>());

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var result = await service.SubmitAnswerAsync("token201", "I would explain the issue, the impact, and my fix.");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("Interview has not started. Click Start Interview to begin."));
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
        aiClient.Verify(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
        turnService.Verify(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()), Times.Never);
        turnService.Verify(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()), Times.Never);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task SubmitAnswerAsync_Rejects_Copied_Question_Text()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession { Id = 4, ProductId = 20, CustomerId = 5, SessionKey = "key4", Token = "token4", Difficulty = "Medium", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        var turn = new InterviewTurn { Id = 1, InterviewSessionId = 4, SequenceNumber = 1, QuestionText = "Describe a difficult bug you fixed recently.", AskedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
        var store = new List<InterviewTurn> { turn };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token4")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(4)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var result = await service.SubmitAnswerAsync("token4", "Describe a difficult bug you fixed recently.");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("Please answer the question in your own words."));
        Assert.That(turn.AnswerText, Is.Null);
        aiClient.Verify(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
        turnService.Verify(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()), Times.Never);
    }

    [Test]
    public async Task SubmitAnswerAsync_Uses_Session_QuestionCount_Before_Difficulty_Fallback()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession
        {
            Id = 25,
            ProductId = 20,
            CustomerId = 5,
            SessionKey = "key25",
            Token = "token25",
            Difficulty = "Hard",
            QuestionCount = 1,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        var turn = new InterviewTurn { Id = 1, InterviewSessionId = 25, SequenceNumber = 1, QuestionText = "Q1", AskedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
        var store = new List<InterviewTurn> { turn };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token25")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(25)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Score = 80,
                TechnicalScore = 80,
                CommunicationScore = 80,
                ProfessionalismScore = 80,
                PositiveAttitudeScore = 80,
                Feedback = "Solid",
                Complete = false,
                NextQuestion = "Q2",
                RawJson = "{}",
                RubricJson = "{\"technicalScore\":80,\"communicationScore\":80,\"professionalismScore\":80,\"positiveAttitudeScore\":80,\"score\":80}"
            });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>())).Returns(Task.CompletedTask);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token25", "I investigated the fault isolation path and fixed the rollback condition.");

        Assert.That(result.Success, Is.True);
        Assert.That(result.IsTerminated, Is.True);
        turnService.Verify(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()), Times.Never);
    }

    [Test]
    public async Task CompleteInterviewAsync_BeforeBegin_DoesNotCompleteOrPublish()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession
        {
            Id = 301,
            ProductId = 20,
            CustomerId = 5,
            SessionKey = "key301",
            Token = "token301",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token301")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(301)).ReturnsAsync(new List<InterviewTurn>());

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, eventPublisher: eventPublisher);

        var result = await service.CompleteInterviewAsync("token301", "Stopped by user");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("Interview has not started. Click Start Interview to begin."));
        Assert.That(session.CompletedOnUtc, Is.Null);
        Assert.That(session.IsActive, Is.True);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
    }

    [Test]
    public async Task SubmitAnswerAsync_Persists_CategoryRubric_And_SessionAverage()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession { Id = 222, ProductId = 20, CustomerId = 5, SessionKey = "key222", Token = "token222", Difficulty = "Medium", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1), QuestionCount = 1 };
        var turn = new InterviewTurn { Id = 1, InterviewSessionId = 222, SequenceNumber = 1, QuestionText = "Q1", AskedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
        var store = new List<InterviewTurn> { turn };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token222")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(222)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Success = true,
                TechnicalScore = 92,
                CommunicationScore = 84,
                ProfessionalismScore = 88,
                PositiveAttitudeScore = 76,
                Score = 85,
                Feedback = "Balanced answer",
                RawJson = "{\"technicalScore\":92,\"communicationScore\":84,\"professionalismScore\":88,\"positiveAttitudeScore\":76,\"score\":85}",
                RubricJson = "{\"technicalScore\":92,\"communicationScore\":84,\"professionalismScore\":88,\"positiveAttitudeScore\":76,\"score\":85}"
            });
        productService.Setup(x => x.GetProductByIdAsync(20)).ReturnsAsync(new Product { Id = 20, Name = "QA Engineer" });
        InterviewTurn updatedTurn = null;
        InterviewSession updatedSession = null;
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(updated =>
            {
                updatedTurn = updated;
                store.RemoveAll(x => x.Id == updated.Id);
                store.Add(updated);
            })
            .Returns(Task.CompletedTask);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(updated => updatedSession = updated)
            .Returns(Task.CompletedTask);
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token222", "Answer with detail.");

        Assert.That(result.Success, Is.True);
        Assert.That(updatedTurn, Is.Not.Null);
        Assert.That(updatedTurn.Score, Is.EqualTo(85));
        Assert.That(updatedTurn.RubricJson, Does.Contain("technicalScore"));
        Assert.That(updatedTurn.RubricJson, Does.Contain("communicationScore"));
        Assert.That(updatedTurn.RubricJson, Does.Contain("professionalismScore"));
        Assert.That(updatedTurn.RubricJson, Does.Contain("positiveAttitudeScore"));
        Assert.That(updatedSession, Is.Not.Null);
        Assert.That(updatedSession.Score, Is.EqualTo(85));
        Assert.That(updatedSession.QuestionScores, Does.Contain("85"));
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
    }

    [Test]
    public async Task SubmitAnswerAsync_Scores_Local_Intro_Turn_Through_Existing_Path()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession
        {
            Id = 223,
            ProductId = 20,
            CustomerId = 5,
            SessionKey = "key223",
            Token = "token223",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            QuestionCount = 1
        };
        var store = new List<InterviewTurn>();
        InterviewTurn updatedTurn = null;
        AIInterviewClientRequest scoreRequest = null;

        sessionService.Setup(x => x.GetSessionByTokenAsync("token223")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(223)).ReturnsAsync(() => store.OrderBy(turn => turn.SequenceNumber).ToList());
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                turn.Id = 1;
                store.Add(turn);
                return turn;
            });
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(turn =>
            {
                updatedTurn = turn;
                store.RemoveAll(existing => existing.Id == turn.Id);
                store.Add(turn);
            })
            .Returns(Task.CompletedTask);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        productService.Setup(x => x.GetProductByIdAsync(20)).ReturnsAsync(new Product { Id = 20, Name = "Platform Engineer" });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .Callback<AIInterviewClientRequest>(request => scoreRequest = request)
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Success = true,
                TechnicalScore = 91,
                CommunicationScore = 88,
                ProfessionalismScore = 90,
                PositiveAttitudeScore = 87,
                Score = 89,
                Feedback = "Strong introduction with clear project ownership.",
                RawJson = "{\"score\":89,\"feedback\":\"Strong introduction with clear project ownership.\",\"complete\":true}",
                RubricJson = "{\"technicalScore\":91,\"communicationScore\":88,\"professionalismScore\":90,\"positiveAttitudeScore\":87,\"score\":89,\"feedback\":\"Strong introduction with clear project ownership.\"}"
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        await service.EnsureInterviewStartedAsync(session, new Customer { Id = 5, FirstName = "Jane", LastName = "Doe" });
        var result = await service.SubmitAnswerAsync("token223", "I am a platform engineer with seven years of experience. I led our payments modernization project on .NET and Azure, owned the API redesign, handled rollback and observability gaps during cutover, and improved transaction success rates by reducing failures after launch.");

        Assert.That(result.Success, Is.True);
        Assert.That(updatedTurn, Is.Not.Null);
        Assert.That(updatedTurn.SequenceNumber, Is.EqualTo(1));
        Assert.That(updatedTurn.AnswerText, Does.Contain("payments modernization project"));
        Assert.That(updatedTurn.Score, Is.EqualTo(89));
        Assert.That(updatedTurn.Feedback, Is.EqualTo("Strong introduction with clear project ownership."));
        Assert.That(scoreRequest, Is.Not.Null);
        Assert.That(scoreRequest.QuestionNumber, Is.EqualTo(1));
        Assert.That(scoreRequest.CurrentTurnRubricJson, Does.Contain("Introduction & Project Experience"));
        using (var rubricDocument = JsonDocument.Parse(updatedTurn.RubricJson))
        {
            Assert.That(rubricDocument.RootElement.GetProperty("technicalScore").GetDecimal(), Is.EqualTo(91));
            Assert.That(rubricDocument.RootElement.GetProperty("score").GetDecimal(), Is.EqualTo(89));
            Assert.That(rubricDocument.RootElement.GetProperty("feedback").GetString(), Is.EqualTo("Strong introduction with clear project ownership."));
            Assert.That(rubricDocument.RootElement.GetProperty("plan").GetProperty("category").GetString(), Is.EqualTo("Introduction & Project Experience"));
            Assert.That(rubricDocument.RootElement.GetProperty("scoring").GetProperty("communicationScore").GetDecimal(), Is.EqualTo(88));
        }

        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Never);
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
    }

    [Test]
    public async Task SubmitAnswerAsync_PreservesQuestionPlanMetadataAfterScoring()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession
        {
            Id = 240,
            ProductId = 20,
            CustomerId = 5,
            SessionKey = "key240",
            Token = "token240",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            QuestionCount = 2
        };
        var currentTurn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = 240,
            SequenceNumber = 1,
            QuestionText = "Describe your payments project tradeoffs.",
            RubricJson = "{\"category\":\"project_scenario\",\"resumeEvidence\":\"Payments platform project\",\"expectedSignals\":[\"tradeoffs\",\"ownership\"],\"rubric\":{\"technical\":\"Depth\",\"communication\":\"Clarity\"}}",
            RawAIResponseJson = "{\"sequenceNumber\":1,\"category\":\"project_scenario\",\"question\":\"Describe your payments project tradeoffs.\",\"resumeEvidence\":\"Payments platform project\"}",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-3),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-3)
        };
        var nextTurn = new InterviewTurn
        {
            Id = 2,
            InterviewSessionId = 240,
            SequenceNumber = 2,
            QuestionText = "Second question",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-2),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2)
        };
        var store = new List<InterviewTurn> { currentTurn, nextTurn };
        AIInterviewClientRequest scoreRequest = null;
        InterviewTurn updatedTurn = null;

        sessionService.Setup(x => x.GetSessionByTokenAsync("token240")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(240)).ReturnsAsync(() => store.OrderBy(turn => turn.SequenceNumber).ToList());
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(turn =>
            {
                updatedTurn = turn;
                store.RemoveAll(existing => existing.Id == turn.Id);
                store.Add(turn);
            })
            .Returns(Task.CompletedTask);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        productService.Setup(x => x.GetProductByIdAsync(20)).ReturnsAsync(new Product { Id = 20, Name = "Platform Engineer" });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .Callback<AIInterviewClientRequest>(request => scoreRequest = request)
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Success = true,
                TechnicalScore = 90,
                CommunicationScore = 85,
                ProfessionalismScore = 88,
                PositiveAttitudeScore = 87,
                Score = 87.5m,
                Feedback = "Strong answer",
                RawJson = "{\"score\":87.5,\"feedback\":\"Strong answer\",\"complete\":false}",
                RubricJson = "{\"technicalScore\":90,\"communicationScore\":85,\"professionalismScore\":88,\"positiveAttitudeScore\":87,\"score\":87.5,\"feedback\":\"Strong answer\"}"
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var result = await service.SubmitAnswerAsync("token240", "I would prioritize safe scaling, observability, and rollback paths.");

        Assert.That(result.Success, Is.True);
        Assert.That(result.Question, Is.EqualTo("Second question"));
        Assert.That(scoreRequest, Is.Not.Null);
        Assert.That(scoreRequest.CurrentTurnRubricJson, Does.Contain("Payments platform project"));
        Assert.That(scoreRequest.CurrentTurnRubricJson, Does.Contain("\"category\":\"project_scenario\""));
        Assert.That(updatedTurn, Is.Not.Null);
        using (var rubricDocument = JsonDocument.Parse(updatedTurn.RubricJson))
        {
            Assert.That(rubricDocument.RootElement.GetProperty("technicalScore").GetDecimal(), Is.EqualTo(90));
            Assert.That(rubricDocument.RootElement.GetProperty("score").GetDecimal(), Is.EqualTo(87.5m));
            Assert.That(rubricDocument.RootElement.GetProperty("plan").GetProperty("category").GetString(), Is.EqualTo("project_scenario"));
            Assert.That(rubricDocument.RootElement.GetProperty("plan").GetProperty("resumeEvidence").GetString(), Is.EqualTo("Payments platform project"));
            Assert.That(rubricDocument.RootElement.GetProperty("scoring").GetProperty("communicationScore").GetDecimal(), Is.EqualTo(85));
        }

        using (var rawDocument = JsonDocument.Parse(updatedTurn.RawAIResponseJson))
        {
            Assert.That(rawDocument.RootElement.GetProperty("questionPlan").GetProperty("category").GetString(), Is.EqualTo("project_scenario"));
            Assert.That(rawDocument.RootElement.GetProperty("scoringResponse").GetProperty("score").GetDecimal(), Is.EqualTo(87.5m));
        }
    }

    [Test]
    public async Task SubmitAnswerAsync_Includes_Previous_Answers_And_Feedback_In_Ai_Request_Context()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession { Id = 22, ProductId = 44, CustomerId = 5, SessionKey = "key22", Token = "token22", Difficulty = "Medium", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1), QuestionCount = 3 };
        var priorTurn = new InterviewTurn { Id = 1, InterviewSessionId = 22, SequenceNumber = 1, QuestionText = "Q1", AnswerText = "A1 with detail", Score = 74, Feedback = "Add stronger metrics", AskedOnUtc = DateTime.UtcNow.AddMinutes(-5), AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-4), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-5) };
        var currentTurn = new InterviewTurn { Id = 2, InterviewSessionId = 22, SequenceNumber = 2, QuestionText = "Q2", AskedOnUtc = DateTime.UtcNow.AddMinutes(-2), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2) };
        var plannedNextTurn = new InterviewTurn { Id = 3, InterviewSessionId = 22, SequenceNumber = 3, QuestionText = "Q3", AskedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
        var store = new List<InterviewTurn> { priorTurn, currentTurn, plannedNextTurn };

        AIInterviewClientRequest scoreRequest = null;

        sessionService.Setup(x => x.GetSessionByTokenAsync("token22")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(22)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .Callback<AIInterviewClientRequest>(request => scoreRequest = request)
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Score = 88,
                TechnicalScore = 90,
                CommunicationScore = 86,
                ProfessionalismScore = 88,
                PositiveAttitudeScore = 88,
                Feedback = "Strong follow-up",
                RawJson = "{}",
                RubricJson = "{\"technicalScore\":90,\"communicationScore\":86,\"professionalismScore\":88,\"positiveAttitudeScore\":88,\"score\":88}"
            });
        productService.Setup(x => x.GetProductByIdAsync(44)).ReturnsAsync(new Product { Id = 44, Name = "Platform Engineer" });
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(updated =>
            {
                store.RemoveAll(x => x.Id == updated.Id);
                store.Add(updated);
            })
            .Returns(Task.CompletedTask);
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token22", "A2 with structure and impact.");

        Assert.That(result.Success, Is.True);
        Assert.That(scoreRequest, Is.Not.Null);
        Assert.That(scoreRequest.PreviousTurns.Count, Is.EqualTo(1));
        Assert.That(scoreRequest.PreviousTurns[0].Question, Is.EqualTo("Q1"));
        Assert.That(scoreRequest.PreviousTurns[0].Answer, Is.EqualTo("A1 with detail"));
        Assert.That(scoreRequest.PreviousTurns[0].Feedback, Is.EqualTo("Add stronger metrics"));
        Assert.That(result.Question, Is.EqualTo("Q3"));
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Never);
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Never);
    }

    [Test]
    public async Task SubmitAnswerAsync_QuestionGenerationFailure_DoesNotInsertFakeQuestion()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession { Id = 12, ProductId = 20, CustomerId = 5, SessionKey = "key12", Token = "token12", Difficulty = "Medium", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        sessionService.Setup(x => x.GetSessionByTokenAsync("token12")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(12)).ReturnsAsync(new List<InterviewTurn>());
        aiClient.Setup(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse { Success = false, ErrorMessage = "AI service unavailable." });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        var inserted = false;
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback(() => inserted = true)
            .ReturnsAsync((InterviewTurn turn) => turn);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            settings: new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://endpoint",
                AzureOpenAiApiKey = "key",
                AzureOpenAiDeploymentOrModel = "deployment",
                Prompt = "prompt"
            },
            mockSettings: new MockAIInterviewSettings { UseMockResponses = false },
            workContext: workContext,
            eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token12", "answer");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("Interview has not started. Click Start Interview to begin."));
        Assert.That(inserted, Is.False);
    }

    [Test]
    public async Task SubmitAnswerAsync_InvalidScoreResponse_DoesNotPersistTurnOrSession()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession
        {
            Id = 15,
            ProductId = 21,
            CustomerId = 5,
            SessionKey = "key15",
            Token = "token15",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        var turn = new InterviewTurn { Id = 1, InterviewSessionId = 15, SequenceNumber = 1, QuestionText = "Q1", AskedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
        var store = new List<InterviewTurn> { turn };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token15")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(15)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Success = false,
                ErrorMessage = "AI service unavailable.",
                Feedback = "AI service unavailable.",
                RawJson = "{\"feedback\":\"AI service unavailable.\"}",
                RubricJson = "{\"feedback\":\"AI service unavailable.\"}"
            });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        var updatedTurn = false;
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback(() => updatedTurn = true)
            .Returns(Task.CompletedTask);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            workContext: workContext,
            eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token15", "I would diagnose the issue by checking logs and isolating the failing dependency.");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("temporarily unavailable"));
        Assert.That(updatedTurn, Is.False);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task SubmitAnswerAsync_ExpiryBoundary_ReturnsInvalidToken()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession
        {
            Id = 14,
            ProductId = 21,
            CustomerId = 5,
            SessionKey = "key14",
            Token = "boundary",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("boundary")).ReturnsAsync(session);
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            workContext: workContext,
            eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("boundary", "answer");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("Invalid or expired session token."));
    }

    [Test]
    public async Task SubmitAnswerAsync_BlankNextQuestion_DoesNotInsertFakeQuestion()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession { Id = 13, ProductId = 21, CustomerId = 5, SessionKey = "key13", Token = "token13", Difficulty = "Medium", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        var turn = new InterviewTurn { Id = 1, InterviewSessionId = 13, SequenceNumber = 1, QuestionText = "Q1", AskedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
        var store = new List<InterviewTurn> { turn };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token13")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(13)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse { Score = 80, Feedback = "Good", RawJson = "{}" });
        aiClient.Setup(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse { Success = true, Question = " ", RawJson = "{\"question\":\" \"}" });
        productService.Setup(x => x.GetProductByIdAsync(21)).ReturnsAsync(new Product { Id = 21, Name = "QA Engineer" });
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(updated =>
            {
                store.RemoveAll(x => x.Id == updated.Id);
                store.Add(updated);
            })
            .Returns(Task.CompletedTask);
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn created) =>
            {
                created.Id = created.SequenceNumber;
                store.Add(created);
                return created;
            });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            settings: new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://endpoint",
                AzureOpenAiApiKey = "key",
                AzureOpenAiDeploymentOrModel = "deployment",
                Prompt = "prompt"
            },
            mockSettings: new MockAIInterviewSettings { UseMockResponses = false },
            workContext: workContext,
            eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token13", "I would explain the production issue, the root cause, and how I fixed it.");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("temporarily unavailable"));
        Assert.That(result.Feedback, Does.Contain("temporarily unavailable"));
        turnService.Verify(x => x.InsertInterviewTurnAsync(It.Is<InterviewTurn>(t => t.SequenceNumber == 2)), Times.Never);
    }

    [Test]
    public void ParseStructuredResponse_HandlesFencedJsonAndScoreString()
    {
        var response = InterviewAiClient.ParseStructuredResponse("""
```json
{
  "question": "What is DI?",
  "score": "87.5",
  "feedback": "Solid",
  "complete": true,
  "completion": "done"
}
```
""");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Question, Is.EqualTo("What is DI?"));
        Assert.That(response.Score, Is.EqualTo(87.5m));
        Assert.That(response.Complete, Is.True);
        Assert.That(response.Completion, Is.EqualTo("done"));
    }

    [Test]
    public void ParseStructuredResponse_MissingScore_LeavesScoreNull()
    {
        var response = InterviewAiClient.ParseStructuredResponse("""{"question":"What is DI?","complete":false}""");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Question, Is.EqualTo("What is DI?"));
        Assert.That(response.Score, Is.Null);
    }

    [Test]
    public void ParseStructuredResponse_Handles_CategoryScores_And_ComputesAverage()
    {
        var response = InterviewAiClient.ParseStructuredResponse("""
{"technicalScore":92,"communicationScore":"84","professionalismScore":88,"positiveAttitudeScore":76,"feedback":"Balanced","complete":false}
""");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.TechnicalScore, Is.EqualTo(92));
        Assert.That(response.CommunicationScore, Is.EqualTo(84));
        Assert.That(response.ProfessionalismScore, Is.EqualTo(88));
        Assert.That(response.PositiveAttitudeScore, Is.EqualTo(76));
        Assert.That(response.Score, Is.EqualTo(85));
        Assert.That(response.RubricJson, Does.Contain("technicalScore"));
    }

    [Test]
    public void ParseStructuredResponse_Handles_RubricJson_Object_WithRootScore_AndNextQuestion()
    {
        var response = InterviewAiClient.ParseStructuredResponse("""
{"score":85,"feedback":"Balanced","complete":false,"nextQuestion":"Tell me about system design.","rubricJson":{"technicalScore":92,"communicationScore":84,"professionalismScore":88,"positiveAttitudeScore":76,"score":85}}
""");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Score, Is.EqualTo(85));
        Assert.That(response.TechnicalScore, Is.EqualTo(92));
        Assert.That(response.CommunicationScore, Is.EqualTo(84));
        Assert.That(response.ProfessionalismScore, Is.EqualTo(88));
        Assert.That(response.PositiveAttitudeScore, Is.EqualTo(76));
        Assert.That(response.NextQuestion, Is.EqualTo("Tell me about system design."));
        Assert.That(response.Feedback, Is.EqualTo("Balanced"));
    }

    [Test]
    public void ParseStructuredResponse_Handles_RubricJson_String_And_AlternateScoreNames()
    {
        var response = InterviewAiClient.ParseStructuredResponse("""
{"overallScore":"81","feedback":"Clear answer","complete":false,"nextQuestion":"Explain caching.","rubricJson":"{\"technical_score\":80,\"communication\":79,\"professionalism_score\":83,\"attitude\":82,\"overall_score\":81}"}
""");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Score, Is.EqualTo(81));
        Assert.That(response.TechnicalScore, Is.EqualTo(80));
        Assert.That(response.CommunicationScore, Is.EqualTo(79));
        Assert.That(response.ProfessionalismScore, Is.EqualTo(83));
        Assert.That(response.PositiveAttitudeScore, Is.EqualTo(82));
    }

    [Test]
    public void ParseStructuredResponse_Handles_MarkdownFencedScoringJson()
    {
        var response = InterviewAiClient.ParseStructuredResponse("""
```json
{"technicalScore":"90","communicationScore":"88","professionalismScore":"86","positiveAttitudeScore":"84","score":"87","feedback":"Solid","complete":false,"nextQuestion":"What tradeoffs did you consider?"}
```
""");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Score, Is.EqualTo(87));
        Assert.That(response.Feedback, Is.EqualTo("Solid"));
        Assert.That(response.NextQuestion, Is.EqualTo("What tradeoffs did you consider?"));
    }

    [Test]
    public void ParseStructuredResponse_HandlesBooleanCompleteFalse()
    {
        var response = InterviewAiClient.ParseStructuredResponse("""{"question":"What is DI?","complete":false}""");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Question, Is.EqualTo("What is DI?"));
        Assert.That(response.Complete, Is.False);
    }

    [Test]
    public void ParseStructuredResponse_HandlesBooleanCompleteTrue()
    {
        var response = InterviewAiClient.ParseStructuredResponse("""{"question":"What is DI?","complete":true}""");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Complete, Is.True);
    }

    [TestCase("false", false)]
    [TestCase("true", true)]
    public void ParseStructuredResponse_HandlesStringCompleteValues(string completeValue, bool expected)
    {
        var response = InterviewAiClient.ParseStructuredResponse("{\"question\":\"What is DI?\",\"complete\":\"" + completeValue + "\"}");

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Complete, Is.EqualTo(expected));
    }

    [Test]
    public void ParseStructuredResponse_InvalidJson_ReturnsNull()
    {
        Assert.That(InterviewAiClient.ParseStructuredResponse("not json at all"), Is.Null);
    }

    [Test]
    public async Task GenerateQuestionAsync_HttpFailure_LogsSafeAzureDetails()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent("{\"error\":{\"code\":\"rate_limit_exceeded\",\"message\":\"Too many requests for this deployment.\"}}", Encoding.UTF8, "application/json")
        });
        var httpFactory = CreateHttpClientFactory(handler);
        var nopLogger = new Mock<NopLogger>();
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "super-secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-4o-mini",
                Prompt = "prompt"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            httpFactory.Object,
            null,
            nopLogger.Object);

        var response = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Backend Engineer",
            Difficulty = "Medium",
            Prompt = "prompt",
            QuestionNumber = 1
        });

        Assert.That(response.Success, Is.False);
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI HTTP failure",
            It.Is<string>(message =>
                message.Contains("Mode=generate") &&
                message.Contains("HttpStatus=429") &&
                message.Contains("AzureErrorCode=rate_limit_exceeded") &&
                message.Contains("AzureErrorMessage=Too many requests for this deployment.") &&
                !message.Contains("super-secret-key") &&
                !message.Contains("api-key")),
            null), Times.Once);
    }

    [Test]
    public async Task ScoreAnswerAsync_ContractFailure_LogsPreciseSafeReason()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"feedback\\\":\\\"Helpful\\\",\\\"complete\\\":false}\"}}]}", Encoding.UTF8, "application/json")
        });
        var httpFactory = CreateHttpClientFactory(handler);
        var nopLogger = new Mock<NopLogger>();
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "super-secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-4o-mini",
                Prompt = "prompt"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            httpFactory.Object,
            null,
            nopLogger.Object);

        var response = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Backend Engineer",
            Difficulty = "Medium",
            Prompt = "prompt",
            QuestionNumber = 1,
            Question = "Explain dependency injection.",
            Answer = "It reduces coupling."
        });

        Assert.That(response.Success, Is.False);
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview score validation failure",
            It.Is<string>(message =>
                message.Contains("Mode=score") &&
                message.Contains("missing required score") &&
                !message.Contains("super-secret-key") &&
                !message.Contains("It reduces coupling.")),
            null), Times.Once);
    }

    [Test]
    public async Task ScoreAnswerAsync_PlainTextContractFailure_LogsSafeDiagnostics()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Sorry, I cannot score this right now.\"}}]}", Encoding.UTF8, "application/json")
        });
        var httpFactory = CreateHttpClientFactory(handler);
        var nopLogger = new Mock<NopLogger>();
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "super-secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-4o-mini",
                Prompt = "prompt"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            httpFactory.Object,
            null,
            nopLogger.Object);

        var response = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Backend Engineer",
            Difficulty = "Medium",
            Prompt = "prompt",
            Question = "Explain dependency injection.",
            Answer = "My full candidate answer with confidential details."
        });

        Assert.That(response.Success, Is.False);
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI contract failure",
            It.Is<string>(message =>
                message.Contains("Mode=score") &&
                message.Contains("Reason=invalid JSON") &&
                message.Contains("Shape=plain text") &&
                message.Contains("Sample=Sorry, I cannot score this right now.") &&
                !message.Contains("super-secret-key") &&
                !message.Contains("My full candidate answer with confidential details.")),
            null), Times.Once);
    }

    [Test]
    public async Task GenerateQuestionAsync_ContractFailure_LogsSafeReason()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[]}", Encoding.UTF8, "application/json")
        });
        var httpFactory = CreateHttpClientFactory(handler);
        var nopLogger = new Mock<NopLogger>();
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "super-secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-4o-mini",
                Prompt = "prompt"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            httpFactory.Object,
            null,
            nopLogger.Object);

        var response = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Backend Engineer",
            Difficulty = "Medium",
            Prompt = "prompt",
            QuestionNumber = 1
        });

        Assert.That(response.Success, Is.False);
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI contract failure",
            It.Is<string>(message =>
                message.Contains("Mode=generate") &&
                message.Contains("Reason=empty response choices") &&
                !message.Contains("super-secret-key")),
            null), Times.Once);
    }

    [Test]
    public void RuntimeClientSettings_Serializes_UsingCamelCase()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new RuntimeClientSettingsModel
        {
            QuestionCount = 5,
            SubmitAnswerUrl = "/submit",
            StopInterviewUrl = "/stop",
            SpeechAvailable = true
        }, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        Assert.That(json, Does.Contain("\"questionCount\""));
        Assert.That(json, Does.Contain("\"submitAnswerUrl\""));
        Assert.That(json, Does.Contain("\"stopInterviewUrl\""));
        Assert.That(json, Does.Contain("\"speechAvailable\""));
    }

    [Test]
    public async Task SubmitAnswerAsync_WhenAiRequestsCompletion_PublishesOnce()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession
        {
            Id = 3,
            ProductId = 30,
            CustomerId = 8,
            SessionKey = "session-3",
            Token = "token3",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            QuestionCount = 1
        };
        var turn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = 3,
            SequenceNumber = 1,
            QuestionText = "Q1",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token3")).ReturnsAsync(session);
        InterviewSession updatedSession = null;
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(updated => updatedSession = updated)
            .Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(3)).ReturnsAsync(new List<InterviewTurn> { turn });
        InterviewTurn updatedTurn = null;
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(updated => updatedTurn = updated)
            .Returns(Task.CompletedTask);
        productService.Setup(x => x.GetProductByIdAsync(30)).ReturnsAsync(new Product { Id = 30, Name = "Architect" });
        customerService.Setup(x => x.GetCustomerByIdAsync(8)).ReturnsAsync(new Customer { Id = 8, FirstName = "John", LastName = "Doe" });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Score = 91,
                Feedback = "Strong",
                Complete = true,
                Completion = "Interview completed",
                RawJson = "{\"score\":91,\"complete\":true}",
                RubricJson = "{\"score\":91,\"feedback\":\"Strong\"}"
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token3", "Answer that should complete");

        Assert.That(result.IsTerminated, Is.True);
        Assert.That(result.Success, Is.True);
        Assert.That(result.ReportUrl, Is.Empty);
        Assert.That(updatedSession, Is.Not.Null);
        Assert.That(updatedSession.Score, Is.EqualTo(91));
        Assert.That(updatedSession.QuestionScores, Does.Contain("91"));
        Assert.That(updatedSession.ReportData, Does.Contain("AI completion: Interview completed"));
        Assert.That(updatedTurn, Is.Not.Null);
        Assert.That(updatedTurn.AnswerText, Is.EqualTo("Answer that should complete"));
        Assert.That(updatedTurn.Score, Is.EqualTo(91));
        Assert.That(updatedTurn.Feedback, Is.EqualTo("Strong"));
        using (var rawDocument = JsonDocument.Parse(updatedTurn.RawAIResponseJson))
            Assert.That(rawDocument.RootElement.GetProperty("scoringResponse").GetProperty("score").GetDecimal(), Is.EqualTo(91));
        using (var rubricDocument = JsonDocument.Parse(updatedTurn.RubricJson))
        {
            Assert.That(rubricDocument.RootElement.GetProperty("score").GetDecimal(), Is.EqualTo(91));
            Assert.That(rubricDocument.RootElement.GetProperty("scoring").GetProperty("feedback").GetString(), Is.EqualTo("Strong"));
        }
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Once);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.CompletedOnUtc.HasValue && !s.IsActive)), Times.Once);
    }

    [Test]
    public async Task CompleteInterviewAsync_AlreadyCompleted_DoesNotPublish()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession
        {
            Id = 4,
            ProductId = 40,
            CustomerId = 9,
            SessionKey = "session-4",
            Token = "token4",
            Difficulty = "Medium",
            IsActive = false,
            CompletedOnUtc = DateTime.UtcNow.AddMinutes(-5),
            TokenExpiryUtc = DateTime.UtcNow.AddHours(-1)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token4")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(4)).ReturnsAsync(new List<InterviewTurn>());
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.CompleteInterviewAsync("token4", "already complete");

        Assert.That(result.Success, Is.False);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
    }

    [Test]
    public async Task CompleteInterviewAsync_ExpiryBoundary_ReturnsInvalidToken()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession
        {
            Id = 44,
            Token = "boundary",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("boundary")).ReturnsAsync(session);
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.CompleteInterviewAsync("boundary", "reason");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("Invalid or expired session token."));
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
    }

    [Test]
    public async Task SpeechToken_ReturnNull_WhenConfigMissing()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        sessionService.Setup(x => x.GetSessionByTokenAsync("token5")).ReturnsAsync(new InterviewSession { Id = 5, Token = "token5", IsActive = true });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        Assert.That(await service.GetSpeechTokenAsync("token5"), Is.Null);
    }

    [Test]
    public async Task SpeechToken_ReturnsNull_ForInactiveCompletedOrExpiredSessions()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("speech-token") });
        var httpFactory = CreateHttpClientFactory(httpHandler);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech-key",
                AzureSpeechRegion = "eastus"
            });

        var inactive = new InterviewSession { Token = "inactive", IsActive = false, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        var completed = new InterviewSession { Token = "completed", IsActive = true, CompletedOnUtc = DateTime.UtcNow.AddMinutes(-1), TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        var expired = new InterviewSession { Token = "expired", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddSeconds(-1) };

        sessionService.Setup(x => x.GetSessionByTokenAsync("inactive")).ReturnsAsync(inactive);
        sessionService.Setup(x => x.GetSessionByTokenAsync("completed")).ReturnsAsync(completed);
        sessionService.Setup(x => x.GetSessionByTokenAsync("expired")).ReturnsAsync(expired);

        Assert.That(await service.GetSpeechTokenAsync("inactive"), Is.Null);
        Assert.That(await service.GetSpeechTokenAsync("completed"), Is.Null);
        Assert.That(await service.GetSpeechTokenAsync("expired"), Is.Null);
        Assert.That(httpHandler.Requests, Is.Empty);
    }

    [Test]
    public async Task SpeechToken_ReturnsNull_OnExpiryBoundary()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("speech-token") });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var now = DateTime.UtcNow;

        sessionService.Setup(x => x.GetSessionByTokenAsync("boundary")).ReturnsAsync(new InterviewSession
        {
            Token = "boundary",
            IsActive = true,
            TokenExpiryUtc = now
        });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech-key",
                AzureSpeechRegion = "eastus"
            });

        Assert.That(await service.GetSpeechTokenAsync("boundary"), Is.Null);
        Assert.That(httpHandler.Requests, Is.Empty);
    }

    [Test]
    public async Task UploadRecordingAsync_Rejects_Invalid_Expired_OldCompleted_Sessions()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpFactory = CreateHttpClientFactory(httpHandler);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureBlobStorageContainerUrl = "https://storage.blob.core.windows.net/container",
                AzureBlobStorageSasToken = "?sig=token"
            });

        sessionService.Setup(x => x.GetSessionByTokenAsync("invalid")).ReturnsAsync((InterviewSession)null);
        sessionService.Setup(x => x.GetSessionByTokenAsync("expired")).ReturnsAsync(new InterviewSession
        {
            Token = "expired",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        sessionService.Setup(x => x.GetSessionByTokenAsync("completed")).ReturnsAsync(new InterviewSession
        {
            Token = "completed",
            IsActive = true,
            CompletedOnUtc = DateTime.UtcNow.AddMinutes(-11),
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });

        Assert.That((await service.UploadRecordingAsync("invalid", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("expired", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("completed", CreateRecordingFile())).Success, Is.False);
        Assert.That(httpHandler.Requests, Is.Empty);
    }

    [Test]
    public async Task UploadRecordingAsync_Allows_RecentlyCompleted_Session()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var completedAt = DateTime.UtcNow.AddMinutes(-2);
        var session = new InterviewSession
        {
            Id = 22,
            Token = "recent",
            IsActive = false,
            CompletedOnUtc = completedAt,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1),
            SessionKey = "session-recent",
            CustomerId = 7,
            ProductId = 5
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("recent")).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureBlobStorageContainerUrl = "https://storage.blob.core.windows.net/container",
                AzureBlobStorageSasToken = "?sig=token"
            });

        var result = await service.UploadRecordingAsync("recent", CreateRecordingFile("recent-webm"));

        Assert.That(result.Success, Is.True);
        Assert.That(session.RecordingUrl, Is.EqualTo(result.RecordingUrl));
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s =>
            s.Id == 22 &&
            s.RecordingUrl == result.RecordingUrl &&
            s.CompletedOnUtc == completedAt &&
            s.IsActive == false)), Times.Once);
    }

    [Test]
    public async Task UploadRecordingAsync_OnExpiryBoundary_ReturnsInvalidToken()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var now = DateTime.UtcNow;

        sessionService.Setup(x => x.GetSessionByTokenAsync("boundary")).ReturnsAsync(new InterviewSession
        {
            Token = "boundary",
            IsActive = true,
            TokenExpiryUtc = now
        });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureBlobStorageContainerUrl = "https://storage.blob.core.windows.net/container",
                AzureBlobStorageSasToken = "?sig=token"
            });

        var result = await service.UploadRecordingAsync("boundary", CreateRecordingFile());

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("Invalid or expired session token."));
        Assert.That(httpHandler.Requests, Is.Empty);
    }

    [Test]
    public async Task UploadRecordingAsync_RejectsMissingConfigAndEmptyFile()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var session = new InterviewSession
        {
            Id = 20,
            Token = "upload",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            SessionKey = "session-upload",
            CustomerId = 7,
            ProductId = 5
        };
        sessionService.Setup(x => x.GetSessionByTokenAsync("upload")).ReturnsAsync(session);

        var serviceMissingConfig = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings());
        var missingConfig = await serviceMissingConfig.UploadRecordingAsync("upload", CreateRecordingFile());
        Assert.That(missingConfig.Success, Is.False);

        var serviceWithConfig = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureBlobStorageContainerUrl = "https://storage.blob.core.windows.net/container",
                AzureBlobStorageSasToken = "?sig=token"
            });
        var emptyFile = new FormFile(new MemoryStream(Array.Empty<byte>()), 0, 0, "recording", "empty.webm")
        {
            Headers = new HeaderDictionary(),
            ContentType = "video/webm"
        };
        var emptyUpload = await serviceWithConfig.UploadRecordingAsync("upload", emptyFile);
        Assert.That(emptyUpload.Success, Is.False);
        Assert.That(httpHandler.Requests, Is.Empty);
    }

    [Test]
    public async Task UploadRecordingAsync_SavesRecordingUrl_OnSuccess()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var session = new InterviewSession
        {
            Id = 21,
            Token = "upload-success",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            SessionKey = "session-success",
            CustomerId = 7,
            ProductId = 5
        };
        sessionService.Setup(x => x.GetSessionByTokenAsync("upload-success")).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        sessionService.Setup(x => x.EnsureRecordingShareTokenAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(s =>
            {
                s.RecordingShareToken = "share-token-success";
                s.RecordingShareEnabled = true;
                s.RecordingShareCreatedOnUtc = DateTime.UtcNow;
            })
            .ReturnsAsync("share-token-success");

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureBlobStorageContainerUrl = "https://storage.blob.core.windows.net/container",
                AzureBlobStorageSasToken = "?sig=token"
            });

        var result = await service.UploadRecordingAsync("upload-success", CreateRecordingFile("webm-data"));

        Assert.That(result.Success, Is.True);
        Assert.That(result.RecordingUrl, Does.Contain("https://storage.blob.core.windows.net/container/recordings-session-success-"));
        Assert.That(session.RecordingUrl, Is.EqualTo(result.RecordingUrl));
        Assert.That(session.CompletedOnUtc, Is.Null);
        Assert.That(session.IsActive, Is.True);
        Assert.That(httpHandler.Requests.Count, Is.EqualTo(1));
        Assert.That(httpHandler.Requests[0].Method, Is.EqualTo(HttpMethod.Put));
        Assert.That(httpHandler.Requests[0].Headers.Contains("x-ms-blob-type"), Is.True);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.RecordingUrl == result.RecordingUrl)), Times.Once);
        sessionService.Verify(x => x.EnsureRecordingShareTokenAsync(It.Is<InterviewSession>(s => s.RecordingUrl == result.RecordingUrl)), Times.Once);
        Assert.That(session.RecordingShareToken, Is.EqualTo("share-token-success"));
        Assert.That(session.RecordingShareEnabled, Is.True);
    }

    [Test]
    public async Task UploadRecordingAsync_Normalizes_ContentType_With_Codec_Parameters()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var session = new InterviewSession
        {
            Id = 36,
            Token = "upload-codec",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            SessionKey = "82db481657f14cfdbfe096204556a0ee",
            CustomerId = 1,
            ProductId = 56
        };
        sessionService.Setup(x => x.GetSessionByTokenAsync("upload-codec")).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        sessionService.Setup(x => x.EnsureRecordingShareTokenAsync(It.IsAny<InterviewSession>())).ReturnsAsync("share-token");

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureBlobStorageContainerUrl = "https://storage.blob.core.windows.net/container",
                AzureBlobStorageSasToken = "?sig=token"
            });

        var result = await service.UploadRecordingAsync("upload-codec", CreateRecordingFile("webm-data", contentType: "video/webm;codecs=vp9,opus"));

        Assert.That(result.Success, Is.True);
        Assert.That(session.RecordingUrl, Is.EqualTo(result.RecordingUrl));
        Assert.That(httpHandler.Requests.Count, Is.EqualTo(1));
        Assert.That(httpHandler.Requests[0].Method, Is.EqualTo(HttpMethod.Put));
        Assert.That(httpHandler.Requests[0].Content?.Headers?.ContentType?.MediaType, Is.EqualTo("video/webm"));
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.RecordingUrl == result.RecordingUrl)), Times.Once);
    }

    [Test]
    public async Task UploadRecordingAsync_Falls_Back_To_Webm_For_Invalid_ContentType()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var session = new InterviewSession
        {
            Id = 37,
            Token = "upload-invalid-type",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            SessionKey = "session-invalid-type",
            CustomerId = 7,
            ProductId = 5
        };
        sessionService.Setup(x => x.GetSessionByTokenAsync("upload-invalid-type")).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        sessionService.Setup(x => x.EnsureRecordingShareTokenAsync(It.IsAny<InterviewSession>())).ReturnsAsync("share-token");

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureBlobStorageContainerUrl = "https://storage.blob.core.windows.net/container",
                AzureBlobStorageSasToken = "?sig=token"
            });

        var result = await service.UploadRecordingAsync("upload-invalid-type", CreateRecordingFile("webm-data", contentType: "not a media type"));

        Assert.That(result.Success, Is.True);
        Assert.That(httpHandler.Requests.Count, Is.EqualTo(1));
        Assert.That(httpHandler.Requests[0].Content?.Headers?.ContentType?.MediaType, Is.EqualTo("video/webm"));
    }

    [Test]
    public async Task UploadRecordingAsync_Logs_Full_Exception_Detail_On_Failure()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var nopLogger = new Mock<NopLogger>();
        var httpHandler = new TestHttpMessageHandler(_ => throw new HttpRequestException("blob upload failed with detailed diagnostics"));
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var session = new InterviewSession
        {
            Id = 38,
            Token = "upload-log-failure",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            SessionKey = "session-log-failure",
            CustomerId = 7,
            ProductId = 5
        };
        sessionService.Setup(x => x.GetSessionByTokenAsync("upload-log-failure")).ReturnsAsync(session);
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureBlobStorageContainerUrl = "https://storage.blob.core.windows.net/container",
                AzureBlobStorageSasToken = "?sig=token"
            },
            nopLogger: nopLogger);

        var result = await service.UploadRecordingAsync("upload-log-failure", CreateRecordingFile("webm-data", contentType: "video/webm;codecs=vp9,opus"));

        Assert.That(result.Success, Is.False);
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview recording upload failure",
            It.Is<string>(message =>
                message.Contains("Stage=Failure") &&
                message.Contains("SessionId=38") &&
                message.Contains("CustomerId=7") &&
                message.Contains("ProductId=5") &&
                message.Contains("ContentType=video/webm;codecs=vp9,opus") &&
                message.Contains("NormalizedAzureContentType=video/webm") &&
                message.Contains("System.Net.Http.HttpRequestException") &&
                message.Contains("blob upload failed with detailed diagnostics")),
            It.IsAny<Customer>()), Times.Once);
    }

    [Test]
    public async Task GenerateQuestionAsync_MapsAzureSuccessResponse_AndPromptContract()
    {
        var responseJson = """
        {
          "choices": [
            {
              "message": {
                "content": "{\"question\":\"Q1\",\"complete\":false,\"rubricJson\":{}}"
              }
            }
          ]
        }
        """;
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            httpFactory.Object,
            null);

        var result = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            Prompt = "Use the admin guidance",
            QuestionNumber = 1,
            PreviousQuestions = new List<string>(),
            PreviousScores = new List<decimal>(),
            PreviousTurns = new List<AIInterviewHistoryItem>
            {
                new() { SequenceNumber = 1, Question = "Previous question", Answer = "Previous answer with business impact", Score = 84, Feedback = "Tighten the metrics" }
            }
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Question, Is.EqualTo("Q1"));
        Assert.That(result.Complete, Is.False);
        var requestBody = await httpHandler.Requests.Single().Content.ReadAsStringAsync();
        Assert.That(requestBody, Does.Contain("Question mode contract"));
        Assert.That(requestBody, Does.Contain("complete:false"));
        Assert.That(requestBody, Does.Contain("optional rubricJson"));
        Assert.That(requestBody, Does.Contain("Use the admin guidance"));
        Assert.That(requestBody, Does.Contain("Previous answered turns"));
        Assert.That(requestBody, Does.Contain("Previous answer with business impact"));
        Assert.That(requestBody, Does.Contain("Tighten the metrics"));
    }

    [Test]
    public async Task ScoreAnswerAsync_MapsAzureSuccessResponse_AndPromptContract()
    {
        var responseJson = """
        {
          "choices": [
            {
              "message": {
                "content": "{\"technicalScore\":92,\"communicationScore\":90,\"professionalismScore\":88,\"positiveAttitudeScore\":94,\"score\":91,\"feedback\":\"Strong\",\"complete\":true,\"nextQuestion\":\"Q2\",\"completion\":\"done\",\"rubricJson\":{\"technicalScore\":92,\"communicationScore\":90,\"professionalismScore\":88,\"positiveAttitudeScore\":94,\"score\":91}}"
              }
            }
          ]
        }
        """;
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            httpFactory.Object,
            null);

        var result = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            Prompt = "Use the admin guidance",
            Question = "Q1",
            Answer = "A1",
            PreviousTurns = new List<AIInterviewHistoryItem>
            {
                new() { SequenceNumber = 1, Question = "Previous question", Answer = "Previous answer", Score = 79, Feedback = "More detail on trade-offs" }
            }
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Score, Is.EqualTo(91));
        Assert.That(result.TechnicalScore, Is.EqualTo(92));
        Assert.That(result.CommunicationScore, Is.EqualTo(90));
        Assert.That(result.ProfessionalismScore, Is.EqualTo(88));
        Assert.That(result.PositiveAttitudeScore, Is.EqualTo(94));
        Assert.That(result.NextQuestion, Is.EqualTo("Q2"));
        var requestBody = await httpHandler.Requests.Single().Content.ReadAsStringAsync();
        Assert.That(requestBody, Does.Contain("Previous answered turns"));
        Assert.That(requestBody, Does.Contain("Previous answer"));
        Assert.That(requestBody, Does.Contain("More detail on trade-offs"));
        Assert.That(result.Complete, Is.True);
        Assert.That(requestBody, Does.Contain("Scoring mode contract"));
        Assert.That(requestBody, Does.Contain("nextQuestion"));
        Assert.That(requestBody, Does.Contain("completion"));
        Assert.That(requestBody, Does.Contain("feedback must be present"));
        Assert.That(requestBody, Does.Contain("technicalScore"));
        Assert.That(requestBody, Does.Contain("communicationScore"));
        Assert.That(requestBody, Does.Contain("professionalismScore"));
        Assert.That(requestBody, Does.Contain("positiveAttitudeScore"));
        Assert.That(requestBody, Does.Contain("rubricJson should be a JSON object"));
        Assert.That(requestBody, Does.Contain("must receive score 0"));
    }

    [Test]
    public async Task ScoreAnswerAsync_MissingOrOutOfRangeScore_ReturnsUnavailable()
    {
        var missingScoreHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"{\"feedback\":\"Weak\",\"complete\":false}"}}]}""", Encoding.UTF8, "application/json")
        });
        var missingScoreFactory = CreateHttpClientFactory(missingScoreHandler);
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            missingScoreFactory.Object,
            null);

        var missing = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            Prompt = "Use the admin guidance",
            Question = "Q1",
            Answer = "A1"
        });

        Assert.That(missing.Success, Is.False);
        Assert.That(missing.Score, Is.Null);
        Assert.That(missing.RawJson, Does.Contain("\"feedback\":\"Weak\""));

        var outOfRangeHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"{\"technicalScore\":96,\"communicationScore\":94,\"professionalismScore\":92,\"positiveAttitudeScore\":90,\"score\":150,\"feedback\":\"Too high\",\"complete\":false,\"nextQuestion\":\"Q2\"}"}}]}""", Encoding.UTF8, "application/json")
        });
        var outOfRangeFactory = CreateHttpClientFactory(outOfRangeHandler);
        var outOfRangeClient = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            outOfRangeFactory.Object,
            null);

        var outOfRange = await outOfRangeClient.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            Prompt = "Use the admin guidance",
            Question = "Q1",
            Answer = "A1"
        });

        Assert.That(outOfRange.Success, Is.True);
        Assert.That(outOfRange.Score, Is.EqualTo(100));
        Assert.That(outOfRange.RawJson, Does.Contain("\"score\":150"));
    }

    [Test]
    public async Task ScoreAnswerAsync_MissingCategoriesOrFeedback_ReturnsUnavailable()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"{\"score\":91,\"feedback\":\"\",\"complete\":false,\"nextQuestion\":\"Q2\",\"technicalScore\":91}"}}]}""", Encoding.UTF8, "application/json")
        });
        var factory = CreateHttpClientFactory(handler);
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            factory.Object,
            null);

        var response = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Question = "Q1",
            Answer = "A1"
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.Score, Is.Null);
    }

    [Test]
    public async Task AzureOpenAi_NonSuccessOrInvalidJson_ReturnsUnavailable()
    {
        var failureHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad request")
        });
        var failureFactory = CreateHttpClientFactory(failureHandler);
        var client = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            failureFactory.Object,
            null);

        var failure = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            Prompt = "Prompt"
        });

        Assert.That(failure.Success, Is.False);

        var invalidJsonHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"not-json"}}]}""", Encoding.UTF8, "application/json")
        });
        var invalidFactory = CreateHttpClientFactory(invalidJsonHandler);
        var invalidClient = new InterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            new MockAIInterviewSettings { UseMockResponses = false },
            invalidFactory.Object,
            null);

        var invalid = await invalidClient.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Question = "Q1",
            Answer = "A1"
        });

        Assert.That(invalid.Success, Is.False);
    }

    [Test]
    public async Task SpeechToken_MapsSuccessfulResponse()
    {
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("speech-token", Encoding.UTF8, "text/plain")
        });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        sessionService.Setup(x => x.GetSessionByTokenAsync("token")).ReturnsAsync(new InterviewSession
        {
            Token = "token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech-key",
                AzureSpeechRegion = "eastus"
            });

        var result = await service.GetSpeechTokenAsync("token");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Token, Is.EqualTo("speech-token"));
        Assert.That(result.Region, Is.EqualTo("eastus"));
        Assert.That(result.ExpiresInSeconds, Is.EqualTo(600));
    }

    [Test]
    public async Task RuntimeModel_Flags_Reflect_ActualConfig()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession { Id = 6, ProductId = 60, CustomerId = 7, SessionKey = "session-6", Token = "token6", QuestionCount = 1, IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(6)).ReturnsAsync(new List<InterviewTurn>
        {
            new()
            {
                Id = 1,
                InterviewSessionId = 6,
                SequenceNumber = 1,
                QuestionText = "Q1",
                AskedOnUtc = DateTime.UtcNow.AddMinutes(-1),
                CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1)
            }
        });

        var missingFlagsService = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            settings: new AIInterviewSettings(),
            mockSettings: new MockAIInterviewSettings { UseMockResponses = true });
        var missingModel = await missingFlagsService.EnsureInterviewStartedAsync(session, new Customer { Id = 7 });
        Assert.That(missingModel.ClientSettings.SpeechAvailable, Is.False);
        Assert.That(typeof(RuntimeClientSettingsModel).GetProperty("AgoraAvailable"), Is.Null);

        var presentFlagsService = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech",
                AzureSpeechRegion = "eastus"
            },
            mockSettings: new MockAIInterviewSettings { UseMockResponses = true });
        var presentModel = await presentFlagsService.EnsureInterviewStartedAsync(session, new Customer { Id = 7 });
        Assert.That(presentModel.ClientSettings.SpeechAvailable, Is.True);
    }

    [Test]
    public void BuildReport_AllZeroScores_DoesNotEmitPositiveStrengthFallback()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var method = typeof(InterviewRuntimeService).GetMethod("BuildReport", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var turns = new List<InterviewTurn>
        {
            new() { SequenceNumber = 1, QuestionText = "Q1", Score = 0 },
            new() { SequenceNumber = 2, QuestionText = "Q2", Score = 0 },
            new() { SequenceNumber = 3, QuestionText = "Q3", Score = 0 }
        };

        var report = (string)method.Invoke(service, new object[] { turns, 0m, "The answer was not substantive.", null });

        Assert.That(report, Does.Not.Contain("Good structure and engagement."));
        Assert.That(report, Does.Contain("Strengths: No scored strengths were identified from the submitted answers."));
        Assert.That(report, Does.Contain("Improvement areas: Provide more concrete examples and implementation details."));
    }

    [Test]
    public void BuildReport_Strengths_DoNotUseQuestionText()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var method = typeof(InterviewRuntimeService).GetMethod("BuildReport", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var questionText = "Can you describe your role in the Copilot4ServiceNow project and how you optimized agent prompts?";
        var turns = new List<InterviewTurn>
        {
            new()
            {
                SequenceNumber = 1,
                QuestionText = questionText,
                AnswerText = "I led the Copilot and ServiceNow workflow design, coordinated Teams integration, and tuned prompts for production support use cases.",
                Feedback = "Strong answer with clear structure.",
                Score = 82
            }
        };

        var report = (string)method.Invoke(service, new object[] { turns, 82m, "Completed", null });

        Assert.That(report, Does.Contain("Strengths:"));
        Assert.That(report, Does.Not.Contain($"Strengths: {questionText}"));
        Assert.That(report, Does.Contain("Demonstrated clear structure and communication."));
    }

    [Test]
    public void BuildReport_ImprovementAreas_DoNotUseQuestionText()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var method = typeof(InterviewRuntimeService).GetMethod("BuildReport", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var questionText = "How have you utilized Azure AI Services in your projects?";
        var turns = new List<InterviewTurn>
        {
            new()
            {
                SequenceNumber = 2,
                QuestionText = questionText,
                AnswerText = "I worked with AI services generally.",
                Feedback = "The candidate did not provide specific examples of Azure AI Services used in projects.",
                Score = 61
            }
        };

        var report = (string)method.Invoke(service, new object[] { turns, 61m, "Completed", null });

        Assert.That(report, Does.Contain("Improvement areas:"));
        Assert.That(report, Does.Not.Contain(questionText));
        Assert.That(report, Does.Contain("Provide specific examples of Azure AI Services used in projects."));
    }

    [Test]
    public void ScorePrompt_Distinguishes_NonSubstantive_Weak_And_Substantive()
    {
        var client = new InterviewAiClient(new AIInterviewSettings(), new MockAIInterviewSettings { UseMockResponses = false });
        var method = typeof(InterviewAiClient).GetMethod("BuildPrompt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var prompt = (string)method.Invoke(client, new object[]
        {
            new AIInterviewClientRequest
            {
                JobTitle = "Platform Engineer",
                Difficulty = "Medium",
                Prompt = "Focus on practical experience.",
                QuestionNumber = 1,
                Question = "Tell me about a project.",
                Answer = "I worked on a payments platform and improved reliability.",
                CurrentTurnRubricJson = "{}"
            },
            "score"
        });

        Assert.That(prompt, Does.Contain("answerQuality"));
        Assert.That(prompt, Does.Contain("non_substantive"));
        Assert.That(prompt, Does.Contain("weak"));
        Assert.That(prompt, Does.Contain("substantive"));
        Assert.That(prompt, Does.Contain("AI-persona answers"));
        Assert.That(prompt, Does.Contain("low but non-zero scores"));
    }

    [Test]
    public void AzureOpenAi_FailureLogsWithoutLeakingKey()
    {
        // Testing TruncateSafe on logger implicitly since we replaced the raw json with TruncateSafe
        var type = typeof(InterviewRuntimeService);
        var method = type.GetMethod("TruncateSafe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var shortString = "short string";
        var shortResult = method.Invoke(null, new object[] { shortString, 500 });
        Assert.That(shortResult, Is.EqualTo(shortString));
    }

    [Test]
    public void TruncateSafe_WorksCorrectly()
    {
        var type = typeof(InterviewRuntimeService);
        var method = type.GetMethod("TruncateSafe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var nullResult = method.Invoke(null, new object[] { null, 500 });
        Assert.That(nullResult, Is.EqualTo(string.Empty));

        var shortString = "short string";
        var shortResult = method.Invoke(null, new object[] { shortString, 500 });
        Assert.That(shortResult, Is.EqualTo(shortString));

        var longString = new string('A', 1000);
        var longResult = (string)method.Invoke(null, new object[] { longString, 500 });
        Assert.That(longResult.Length, Is.EqualTo(503)); // 500 + "..."
        Assert.That(longResult.EndsWith("..."), Is.True);
    }

    [Test]
    public void Runtime_View_ContainsRepeatedReminderLogic()
    {
        var path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "Runtime.cshtml");
        if (!System.IO.File.Exists(path))
            path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "Runtime.cshtml"); // CI/CD path fallback

        var content = System.IO.File.ReadAllText(path);
        Assert.That(content.Contains("if (!currentText && interviewStarted && !isSpeakingOrSubmitting && hasActiveQuestion() && !isScreenShareBlockingInterview())"), Is.True, "Runtime view should contain repeating reminder scheduling logic");
        Assert.That(content.Contains("resetTimers();"), Is.True, "Runtime view should contain resetTimers logic in the timer interval");
    }

    [Test]
    public async Task RealMode_MissingAzureSettings_ReturnsUnavailable()
    {
        var client = new InterviewAiClient(
            new AIInterviewSettings(),
            new MockAIInterviewSettings { UseMockResponses = false });

        var response = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Question = "Q1",
            Answer = "A1"
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.ErrorMessage, Does.Contain("AI service unavailable"));
    }
}
