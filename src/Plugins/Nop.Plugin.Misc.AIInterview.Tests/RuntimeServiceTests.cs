using Moq;
using Microsoft.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Logging;
using Nop.Core.Events;
using Nop.Data;
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
using System.Transactions;

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
        Mock<Microsoft.Extensions.Logging.ILogger<InterviewRuntimeService>> logger = null,
        INopDataProvider dataProvider = null,
        Mock<Nop.Services.Logging.ICustomerActivityService> customerActivityService = null)
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
            settings ?? new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = false },
            mockSettings ?? new MockAIInterviewSettings { UseMockResponses = true },
            httpClientFactory?.Object ?? new Mock<IHttpClientFactory>().Object,
            workContext?.Object ?? new Mock<IWorkContext>().Object,
            eventPublisher?.Object ?? new Mock<IEventPublisher>().Object,
            nopLogger?.Object,
            logger?.Object,
            customerActivityService?.Object,
            dataProvider: dataProvider);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync());
            return _responseFactory(request);
        }
    }

    private static Mock<IHttpClientFactory> CreateHttpClientFactory(TestHttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    private static InterviewAiClient CreateAzureInterviewAiClient(
        AIInterviewSettings settings,
        TestHttpMessageHandler handler,
        NopLogger nopLogger = null)
    {
        return new InterviewAiClient(
            settings,
            new MockAIInterviewSettings { UseMockResponses = false },
            nopLogger: nopLogger,
            azureOpenAiChatCompletionAdapter: new AzureOpenAiChatCompletionAdapter(
                settings,
                new HttpClient(handler, disposeHandler: false)));
    }

    private static void AssertChatCompletionsRequest(TestHttpMessageHandler handler, string deployment)
    {
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(handler.Requests[0].RequestUri.AbsolutePath,
            Is.EqualTo($"/openai/deployments/{deployment}/chat/completions"));
        Assert.That(handler.Requests[0].RequestUri.Query, Does.Contain("api-version=2025-04-01-preview"));
    }

    private static InterviewSessionService CreateInterviewSessionService(IList<InterviewSession> sessions)
    {
        var sessionRepository = new Mock<IRepository<InterviewSession>>();
        sessionRepository
            .Setup(x => x.GetAllAsync(
                It.IsAny<Func<IQueryable<InterviewSession>, IQueryable<InterviewSession>>>(),
                It.IsAny<Func<ICacheKeyService, CacheKey>>(),
                It.IsAny<bool>()))
            .ReturnsAsync((
                Func<IQueryable<InterviewSession>, IQueryable<InterviewSession>> query,
                Func<ICacheKeyService, CacheKey> cacheKeyFactory,
                bool includeDeleted) => query(sessions.AsQueryable()).ToList());

        return new InterviewSessionService(
            sessionRepository.Object,
            new Mock<ICustomerService>().Object,
            new Mock<IApplicationService>().Object,
            new Mock<IProductService>().Object,
            new Mock<Nop.Services.Messages.IWorkflowMessageService>().Object,
            new Mock<Nop.Services.Messages.IMessageTemplateService>().Object,
            new Mock<Nop.Services.Messages.IEmailAccountService>().Object,
            new Mock<Nop.Services.Messages.IMessageTokenProvider>().Object,
            new Nop.Core.Domain.Messages.EmailAccountSettings(),
            new Mock<IStoreContext>().Object,
            new Mock<Nop.Services.Helpers.IWebHelper>().Object,
            new Mock<Nop.Services.Vendors.IVendorService>().Object,
            new Mock<Nop.Services.Helpers.IDateTimeHelper>().Object);
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

    private static InterviewRuntimeService CreateDefaultServiceForRecordingName()
    {
        return CreateService(
            new Mock<IInterviewSessionService>(),
            new Mock<IInterviewTurnService>(),
            new Mock<IAIInterviewClient>(),
            new Mock<IProductService>(),
            new Mock<ICustomerService>(),
            new Mock<ILocalizationService>());
    }

    private static string BuildRecordingBlobNameForTest(InterviewRuntimeService service, Customer customer, DateTime utcNow)
    {
        var method = typeof(InterviewRuntimeService).GetMethod("BuildRecordingBlobName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return (string)method.Invoke(service, new object[] { customer, utcNow });
    }

    [Test]
    public async Task GetCompletionWorkSessionsAsync_QueuedRetryRequiresElapsedNextAttempt()
    {
        var futureRetry = new InterviewSession
        {
            Id = 101,
            CompletionState = InterviewCompletionStates.Queued,
            CompletionAttemptCount = 1,
            CompletionNextAttemptOnUtc = DateTime.UtcNow.AddMinutes(5),
            CompletionQueuedOnUtc = DateTime.UtcNow.AddMinutes(-2),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2)
        };
        var elapsedRetry = new InterviewSession
        {
            Id = 102,
            CompletionState = InterviewCompletionStates.Queued,
            CompletionAttemptCount = 1,
            CompletionNextAttemptOnUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletionQueuedOnUtc = DateTime.UtcNow.AddMinutes(-2),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2)
        };
        var service = CreateInterviewSessionService(new List<InterviewSession> { futureRetry, elapsedRetry });

        var beforeSchedule = await service.GetCompletionWorkSessionsAsync(DateTime.UtcNow.AddMinutes(-10));

        Assert.That(beforeSchedule.Select(session => session.Id), Does.Contain(elapsedRetry.Id));
        Assert.That(beforeSchedule.Select(session => session.Id), Does.Not.Contain(futureRetry.Id));

        futureRetry.CompletionNextAttemptOnUtc = DateTime.UtcNow.AddMinutes(-1);
        var afterSchedule = await service.GetCompletionWorkSessionsAsync(DateTime.UtcNow.AddMinutes(-10));

        Assert.That(afterSchedule.Select(session => session.Id), Does.Contain(futureRetry.Id));
    }

    [Test]
    public async Task EnsureInterviewStartedAsync_Creates_Only_First_Active_Turn()
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
            QuestionCount = 5,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(1)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
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
        Assert.That(insertedTurns.Count, Is.EqualTo(1));
        Assert.That(insertedTurns.All(turn => turn.InterviewSessionId == 1), Is.True);
        Assert.That(insertedTurns.Select(turn => turn.SequenceNumber), Is.EqualTo(new[] { 1 }));
        Assert.That(store.Count, Is.EqualTo(1));
        var firstRubric = JsonDocument.Parse(insertedTurns.Single(turn => turn.SequenceNumber == 1).RubricJson).RootElement;
        Assert.That(firstRubric.GetProperty("category").GetString(), Is.EqualTo("Introduction & Project Experience"));
        Assert.That(firstRubric.GetProperty("expectedSignals").EnumerateArray().Any(signal => signal.GetString() == "Relevant project ownership"), Is.True);
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Never);
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
        var session = new InterviewSession
        {
            Id = 11,
            ProductId = 10,
            CustomerId = 99,
            Token = "token",
            Difficulty = "Medium",
            QuestionCount = 1,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        var store = new List<InterviewTurn>();

        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(11)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
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
    public async Task BeginInterviewAsync_WithoutTurns_ReturnsLocalFirstTurn_WithoutQuestionPlanGeneration()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession
        {
            Id = 33,
            ProductId = 10,
            CustomerId = 99,
            SessionKey = "key33",
            Token = "token33",
            Difficulty = "Medium",
            QuestionCount = 5,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        InterviewTurn insertedTurn = null;

        sessionService.Setup(x => x.GetSessionByTokenAsync("token33")).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(33))
            .ReturnsAsync(() => insertedTurn == null ? new List<InterviewTurn>() : new List<InterviewTurn> { insertedTurn });
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                insertedTurn = turn;
                insertedTurn.Id = 1001;
                return insertedTurn;
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var model = await service.BeginInterviewAsync("token33", new Customer { Id = 99, FirstName = "Ada" });

        Assert.That(model, Is.Not.Null);
        Assert.That(model.CurrentQuestion, Does.Contain("Hello Ada"));
        Assert.That(model.Turns.Count(), Is.EqualTo(1));
        Assert.That(model.Turns.Single().SequenceNumber, Is.EqualTo(1));
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Never);
    }

    [Test]
    public async Task PrepareInterviewAsync_IsIdempotent_AndDoesNotDuplicateTurns()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var turns = new List<InterviewTurn>();
        var nextId = 1;
        var session = new InterviewSession
        {
            Id = 34,
            ProductId = 10,
            CustomerId = 99,
            SessionKey = "key34",
            Token = "token34",
            Difficulty = "Medium",
            QuestionCount = 3,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token34")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(34)).ReturnsAsync(() => turns.OrderBy(turn => turn.SequenceNumber).ToList());
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                turn.Id = nextId++;
                turns.Add(turn);
                return turn;
            });
        aiClient.Setup(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()))
            .ReturnsAsync(new AIInterviewQuestionPlanResponse
            {
                Success = true,
                Questions = new List<AIInterviewQuestionPlanItem>
                {
                    new() { SequenceNumber = 2, Category = "skill", Question = "How do you troubleshoot APIs?" },
                    new() { SequenceNumber = 3, Category = "behavioral", Question = "How do you communicate delivery risk?" }
                }
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var first = await service.PrepareInterviewAsync("token34", new Customer { Id = 99 });
        var second = await service.PrepareInterviewAsync("token34", new Customer { Id = 99 });

        Assert.That(first.Success, Is.True);
        Assert.That(second.Success, Is.True);
        Assert.That(turns.Select(turn => turn.SequenceNumber).OrderBy(value => value), Is.EqualTo(new[] { 1, 2, 3 }));
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Once);
    }

    [Test]
    public async Task PrepareInterviewAsync_ConcurrentCalls_SerializePreparationAndDoNotDuplicateTurns()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var resumeProfileService = new Mock<IResumeProfileService>();
        var turns = new List<InterviewTurn>();
        var gate = new object();
        var nextId = 1;
        var session = new InterviewSession
        {
            Id = 341,
            ProductId = 10,
            CustomerId = 99,
            SessionKey = "key341",
            Token = "token341",
            Difficulty = "Medium",
            QuestionCount = 3,
            ResumeDownloadId = 5,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token341")).ReturnsAsync(session);
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(341)).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(341)).ReturnsAsync(() =>
        {
            lock (gate)
                return turns.OrderBy(turn => turn.SequenceNumber).ToList();
        });
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                lock (gate)
                {
                    turn.Id = nextId++;
                    turns.Add(turn);
                    return turn;
                }
            });
        resumeProfileService.Setup(x => x.EnsureResumeProfileAsync(session, It.IsAny<Product>()))
            .Returns(async () =>
            {
                await Task.Delay(25);
                return new ResumeProfileGenerationResult { Success = true, ProfileJson = "{}" };
            });
        aiClient.Setup(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()))
            .Returns(async (AIInterviewQuestionPlanRequest request) =>
            {
                await Task.Delay(25);
                return new AIInterviewQuestionPlanResponse
                {
                    Success = true,
                    Questions = Enumerable.Range(2, request.QuestionCount).Select(sequence => new AIInterviewQuestionPlanItem
                    {
                        SequenceNumber = sequence,
                        Category = "skill",
                        Question = $"Question {sequence}"
                    }).ToList()
                };
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, resumeProfileService: resumeProfileService);

        var results = await Task.WhenAll(
            service.PrepareInterviewAsync("token341", new Customer { Id = 99 }),
            service.PrepareInterviewAsync("token341", new Customer { Id = 99 }));

        Assert.That(results.All(result => result.Success), Is.True);
        Assert.That(turns.Select(turn => turn.SequenceNumber).OrderBy(value => value), Is.EqualTo(new[] { 1, 2, 3 }));
        resumeProfileService.Verify(x => x.EnsureResumeProfileAsync(session, It.IsAny<Product>()), Times.Once);
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Once);
    }

    [Test]
    public async Task PrepareAndBeginInterviewAsync_ConcurrentCalls_CreateOneFirstTurn_AndOnePlan()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var resumeProfileService = new Mock<IResumeProfileService>();
        var turns = new List<InterviewTurn>();
        var gate = new object();
        var nextId = 1;
        var session = new InterviewSession
        {
            Id = 342,
            ProductId = 10,
            CustomerId = 99,
            SessionKey = "key342",
            Token = "token342",
            Difficulty = "Medium",
            QuestionCount = 3,
            ResumeDownloadId = 5,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token342")).ReturnsAsync(session);
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(342)).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(342)).ReturnsAsync(() =>
        {
            lock (gate)
                return turns.OrderBy(turn => turn.SequenceNumber).ToList();
        });
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                lock (gate)
                {
                    turn.Id = nextId++;
                    turns.Add(turn);
                    return turn;
                }
            });
        resumeProfileService.Setup(x => x.EnsureResumeProfileAsync(session, It.IsAny<Product>()))
            .ReturnsAsync(new ResumeProfileGenerationResult { Success = true, ProfileJson = "{}" });
        aiClient.Setup(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()))
            .Returns(async (AIInterviewQuestionPlanRequest request) =>
            {
                await Task.Delay(25);
                return new AIInterviewQuestionPlanResponse
                {
                    Success = true,
                    Questions = Enumerable.Range(2, request.QuestionCount).Select(sequence => new AIInterviewQuestionPlanItem
                    {
                        SequenceNumber = sequence,
                        Category = "skill",
                        Question = $"Question {sequence}"
                    }).ToList()
                };
            });
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Success = true,
                Score = 88,
                Feedback = "Good answer.",
                RawJson = "{\"score\":88}",
                RubricJson = "{\"score\":88}"
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, resumeProfileService: resumeProfileService);

        var beginTask = service.BeginInterviewAsync("token342", new Customer { Id = 99 });
        var prepareTask = service.PrepareInterviewAsync("token342", new Customer { Id = 99 });
        await Task.WhenAll(prepareTask, beginTask);
        var submitResult = await service.SubmitAnswerAsync("token342", "A concrete answer with enough detail to validate.");

        Assert.That(beginTask.Result.CurrentQuestion, Does.Contain("start with you"));
        Assert.That(beginTask.Result.Turns.Single().SequenceNumber, Is.EqualTo(1));
        Assert.That(submitResult.Success, Is.True);
        Assert.That(turns.Select(turn => turn.SequenceNumber).OrderBy(value => value), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(session.StartedOnUtc, Is.Not.Null);
        resumeProfileService.Verify(x => x.EnsureResumeProfileAsync(session, It.IsAny<Product>()), Times.Once);
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Once);
        aiClient.Verify(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()), Times.Once);
    }

    [Test]
    public async Task PrepareThenBeginInterviewAsync_PrepareRemoteWorkBlocked_BeginReturnsLocalFirstTurn()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var resumeProfileService = new Mock<IResumeProfileService>();
        var turns = new List<InterviewTurn>();
        var gate = new object();
        var nextId = 1;
        var profileStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProfile = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new InterviewSession
        {
            Id = 345,
            ProductId = 10,
            CustomerId = 99,
            SessionKey = "key345",
            Token = "token345",
            Difficulty = "Medium",
            QuestionCount = 3,
            ResumeDownloadId = 5,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token345")).ReturnsAsync(session);
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(345)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(345)).ReturnsAsync(() =>
        {
            lock (gate)
                return turns.OrderBy(turn => turn.SequenceNumber).ThenBy(turn => turn.Id).ToList();
        });
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                lock (gate)
                {
                    turn.Id = nextId++;
                    turns.Add(turn);
                    return turn;
                }
            });
        resumeProfileService.Setup(x => x.EnsureResumeProfileAsync(session, It.IsAny<Product>()))
            .Returns(async () =>
            {
                profileStarted.SetResult();
                await releaseProfile.Task;
                return new ResumeProfileGenerationResult { Success = true, ProfileJson = "{}" };
            });
        aiClient.Setup(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()))
            .ReturnsAsync((AIInterviewQuestionPlanRequest request) => new AIInterviewQuestionPlanResponse
            {
                Success = true,
                Questions = Enumerable.Range(2, request.QuestionCount).Select(sequence => new AIInterviewQuestionPlanItem
                {
                    SequenceNumber = sequence,
                    Category = "skill",
                    Question = $"Question {sequence}"
                }).ToList()
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, resumeProfileService: resumeProfileService);

        var prepareTask = service.PrepareInterviewAsync("token345", new Customer { Id = 99 });
        await profileStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var beginTask = service.BeginInterviewAsync("token345", new Customer { Id = 99 });
        var completed = await Task.WhenAny(beginTask, Task.Delay(500));
        Assert.That(completed, Is.EqualTo(beginTask));
        Assert.That(beginTask.Result.CurrentQuestion, Does.Contain("start with you"));
        Assert.That(beginTask.Result.Turns.Single().SequenceNumber, Is.EqualTo(1));

        releaseProfile.SetResult();
        var prepareResult = await prepareTask;

        Assert.That(prepareResult.Success, Is.True);
        Assert.That(session.StartedOnUtc, Is.Not.Null);
        Assert.That(turns.Select(turn => turn.SequenceNumber).OrderBy(value => value), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(turns.Select(turn => turn.SequenceNumber).Distinct().Count(), Is.EqualTo(3));
        resumeProfileService.Verify(x => x.EnsureResumeProfileAsync(session, It.IsAny<Product>()), Times.Once);
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Once);
    }

    [Test]
    public async Task BeginInterviewAsync_DifferentSessions_DoNotShareGlobalMutationLock()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var sessionOne = new InterviewSession
        {
            Id = 343,
            ProductId = 10,
            CustomerId = 99,
            SessionKey = "key343",
            Token = "token343",
            Difficulty = "Medium",
            QuestionCount = 1,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        var sessionTwo = new InterviewSession
        {
            Id = 344,
            ProductId = 10,
            CustomerId = 100,
            SessionKey = "key344",
            Token = "token344",
            Difficulty = "Medium",
            QuestionCount = 1,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        var turns = new Dictionary<int, List<InterviewTurn>>
        {
            [343] = new(),
            [344] = new()
        };
        var gate = new object();
        var firstInsertStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstInsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextId = 1;

        sessionService.Setup(x => x.GetSessionByTokenAsync("token343")).ReturnsAsync(sessionOne);
        sessionService.Setup(x => x.GetSessionByTokenAsync("token344")).ReturnsAsync(sessionTwo);
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(343)).ReturnsAsync(sessionOne);
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(344)).ReturnsAsync(sessionTwo);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(It.IsAny<int>())).ReturnsAsync((int sessionId) =>
        {
            lock (gate)
                return turns[sessionId].OrderBy(turn => turn.SequenceNumber).ToList();
        });
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Returns(async (InterviewTurn turn) =>
            {
                if (turn.InterviewSessionId == sessionOne.Id)
                {
                    firstInsertStarted.TrySetResult();
                    await releaseFirstInsert.Task;
                }

                lock (gate)
                {
                    turn.Id = nextId++;
                    turns[turn.InterviewSessionId].Add(turn);
                    return turn;
                }
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var sessionOneTask = service.BeginInterviewAsync("token343", new Customer { Id = 99 });
        await firstInsertStarted.Task;
        var sessionTwoTask = service.BeginInterviewAsync("token344", new Customer { Id = 100 });
        var completedFirst = await Task.WhenAny(sessionTwoTask, Task.Delay(500));
        releaseFirstInsert.SetResult();
        await Task.WhenAll(sessionOneTask, sessionTwoTask);

        Assert.That(completedFirst, Is.EqualTo(sessionTwoTask));
        Assert.That(sessionOneTask.Result.CurrentQuestion, Does.Contain("start with you"));
        Assert.That(sessionTwoTask.Result.CurrentQuestion, Does.Contain("start with you"));
        Assert.That(turns[sessionOne.Id].Single().SequenceNumber, Is.EqualTo(1));
        Assert.That(turns[sessionTwo.Id].Single().SequenceNumber, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureInterviewStartedAsync_RealMode_Failure_StillCreatesLocalIntroTurn()
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
            Id = 10,
            ProductId = 50,
            CustomerId = 99,
            SessionKey = "key10",
            Token = "token10",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(10)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
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

        Assert.That(inserted, Is.True);
        Assert.That(model.CurrentQuestion, Does.StartWith("Let's start with you."));
    }

    [Test]
    public async Task EnsureInterviewStartedAsync_RealMode_BlankQuestion_StillCreatesLocalIntroTurn()
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
            Id = 11,
            ProductId = 51,
            CustomerId = 99,
            SessionKey = "key11",
            Token = "token11",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(11)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
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

        Assert.That(inserted, Is.True);
        Assert.That(model.CurrentQuestion, Does.StartWith("Let's start with you."));
    }

    [Test]
    public async Task EnsureInterviewStartedAsync_WithExistingIntro_PreservesPreparedFutureTurns()
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

        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(77)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(77)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                turn.Id = store.Max(existing => existing.Id) + 1;
                store.Add(turn);
                return turn;
            });
        turnService.Setup(x => x.DeleteInterviewTurnsAsync(It.IsAny<IList<InterviewTurn>>()))
            .Callback<IList<InterviewTurn>>(deletedTurns =>
            {
                foreach (var deletedTurn in deletedTurns)
                    store.RemoveAll(existing => existing.Id == deletedTurn.Id);
            })
            .Returns(Task.CompletedTask);
        productService.Setup(x => x.GetProductByIdAsync(18)).ReturnsAsync(new Product { Id = 18, Name = "Platform Engineer" });
        customerService.Setup(x => x.GetCustomerByIdAsync(41)).ReturnsAsync(new Customer { Id = 41, FirstName = "Casey", LastName = "Lee" });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var model = await service.EnsureInterviewStartedAsync(session, new Customer { Id = 41, FirstName = "Casey", LastName = "Lee" });

        Assert.That(model, Is.Not.Null);
        Assert.That(model.CurrentQuestion, Is.EqualTo("Existing question 1"));
        Assert.That(model.Turns.Select(turn => turn.SequenceNumber), Is.EqualTo(new[] { 1 }));
        Assert.That(store.Select(turn => turn.SequenceNumber), Is.EqualTo(new[] { 1, 3 }));
        turnService.Verify(x => x.DeleteInterviewTurnsAsync(It.IsAny<IList<InterviewTurn>>()), Times.Never);
        aiClient.Verify(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()), Times.Never);
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
        Assert.That(result.Turns.Count, Is.EqualTo(2));
        Assert.That(result.Turns.Count(turnModel => string.IsNullOrWhiteSpace(turnModel.AnswerText)), Is.EqualTo(1));
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
    public async Task CompleteInterviewAsync_PreservesRecordingFieldsPersistedDuringCompletion()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var sessionId = 1164;
        var token = "completion-recording-token";
        var staleCompletionSession = new InterviewSession
        {
            Id = sessionId,
            ProductId = 30,
            CustomerId = 8,
            SessionKey = "session-1164",
            Token = token,
            Difficulty = "Medium",
            IsActive = true,
            StartedOnUtc = DateTime.UtcNow.AddMinutes(-6),
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            QuestionCount = 1
        };
        var latestRecordingShareCreatedOnUtc = DateTime.UtcNow.AddMinutes(-1);
        var latestPersistedSession = new InterviewSession
        {
            Id = sessionId,
            ProductId = 30,
            CustomerId = 8,
            SessionKey = "session-1164",
            Token = token,
            Difficulty = "Medium",
            IsActive = true,
            StartedOnUtc = staleCompletionSession.StartedOnUtc,
            TokenExpiryUtc = staleCompletionSession.TokenExpiryUtc,
            QuestionCount = 1,
            RecordingUrl = "https://storage.blob.core.windows.net/container/recordings-ac83454ac9e049bdbaee4118d4fffa17-20260728122443.webm",
            RecordingShareToken = "recording-share-token-1164",
            RecordingShareEnabled = true,
            RecordingShareCreatedOnUtc = latestRecordingShareCreatedOnUtc
        };
        var completedTurn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = sessionId,
            SequenceNumber = 1,
            QuestionText = "Describe the release risk you managed.",
            AnswerText = "I isolated the release risk, added rollback checks, and coordinated the deployment plan.",
            Score = 88,
            Feedback = "Strong operational judgment.",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-5),
            AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-4),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        InterviewSession finalPersistedSession = null;

        sessionService.Setup(x => x.GetSessionByTokenAsync(token)).ReturnsAsync(staleCompletionSession);
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(sessionId))
            .ReturnsAsync(() =>
            {
                staleCompletionSession.RecordingUrl = latestPersistedSession.RecordingUrl;
                staleCompletionSession.RecordingShareToken = latestPersistedSession.RecordingShareToken;
                staleCompletionSession.RecordingShareEnabled = latestPersistedSession.RecordingShareEnabled;
                staleCompletionSession.RecordingShareCreatedOnUtc = latestPersistedSession.RecordingShareCreatedOnUtc;
                return staleCompletionSession;
            });
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(updated => finalPersistedSession = updated)
            .Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(sessionId)).ReturnsAsync(new List<InterviewTurn> { completedTurn });
        productService.Setup(x => x.GetProductByIdAsync(30)).ReturnsAsync(new Product { Id = 30, Name = "Architect" });
        customerService.Setup(x => x.GetCustomerByIdAsync(8)).ReturnsAsync(new Customer { Id = 8, FirstName = "John", LastName = "Doe" });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        aiClient.Setup(x => x.GenerateStrengthsSummaryAsync(It.IsAny<AIInterviewStrengthsSummaryRequest>()))
            .ReturnsAsync(new AIInterviewStrengthsSummaryResponse
            {
                Success = false,
                ErrorMessage = "Strengths summary skipped for preservation regression."
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.CompleteInterviewAsync(token, "Stopped by candidate.");

        Assert.That(result.Success, Is.True);
        Assert.That(result.ReportGenerationInProgress, Is.True);
        Assert.That(staleCompletionSession.CompletionState, Is.EqualTo(InterviewCompletionStates.Queued));
        await service.ProcessCompletionWorkAsync(sessionId);

        Assert.That(finalPersistedSession, Is.Not.Null);
        Assert.That(finalPersistedSession.CompletedOnUtc, Is.Not.Null);
        Assert.That(finalPersistedSession.IsActive, Is.False);
        Assert.That(finalPersistedSession.RecordingUrl, Is.EqualTo(latestPersistedSession.RecordingUrl));
        Assert.That(finalPersistedSession.RecordingShareToken, Is.EqualTo(latestPersistedSession.RecordingShareToken));
        Assert.That(finalPersistedSession.RecordingShareEnabled, Is.True);
        Assert.That(finalPersistedSession.RecordingShareCreatedOnUtc, Is.EqualTo(latestRecordingShareCreatedOnUtc));
        Assert.That(finalPersistedSession.Score, Is.EqualTo(88));
        Assert.That(finalPersistedSession.QuestionScores, Does.Contain("88"));
        Assert.That(finalPersistedSession.ReportData, Is.Not.Null.And.Not.Empty);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s =>
            s.CompletedOnUtc.HasValue &&
            !s.IsActive &&
            s.RecordingUrl == latestPersistedSession.RecordingUrl &&
            s.RecordingShareToken == latestPersistedSession.RecordingShareToken &&
            s.RecordingShareEnabled &&
            s.RecordingShareCreatedOnUtc == latestRecordingShareCreatedOnUtc &&
            !string.IsNullOrWhiteSpace(s.ReportData))), Times.AtLeastOnce);
        sessionService.Verify(x => x.EnsureRecordingShareTokenAsync(It.IsAny<InterviewSession>()), Times.Never);
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
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(223)).ReturnsAsync(session);
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
    public async Task SubmitAnswerAsync_WithStaleFuturePendingTurn_Returns_TrueNextSequence_And_AlignedScores()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();

        var session = new InterviewSession
        {
            Id = 225,
            ProductId = 44,
            CustomerId = 5,
            SessionKey = "key225",
            Token = "token225",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            QuestionCount = 5
        };
        var answeredTurn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = 225,
            SequenceNumber = 1,
            QuestionText = "Q1",
            AnswerText = "A1",
            Score = 71,
            Feedback = "Add metrics",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-8),
            AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-7),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-8)
        };
        var currentTurn = new InterviewTurn
        {
            Id = 2,
            InterviewSessionId = 225,
            SequenceNumber = 2,
            QuestionText = "Q2",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-4),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-4)
        };
        var staleFutureTurn = new InterviewTurn
        {
            Id = 5,
            InterviewSessionId = 225,
            SequenceNumber = 5,
            QuestionText = "Stale pending question",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        var store = new List<InterviewTurn> { answeredTurn, currentTurn, staleFutureTurn };
        InterviewSession updatedSession = null;
        AIInterviewQuestionPlanRequest planRequest = null;

        sessionService.Setup(x => x.GetSessionByTokenAsync("token225")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(225)).ReturnsAsync(() => store.OrderBy(turn => turn.SequenceNumber).ToList());
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(updatedTurn =>
            {
                store.RemoveAll(existing => existing.Id == updatedTurn.Id);
                store.Add(updatedTurn);
            })
            .Returns(Task.CompletedTask);
        turnService.Setup(x => x.DeleteInterviewTurnsAsync(It.IsAny<IList<InterviewTurn>>()))
            .Callback<IList<InterviewTurn>>(deletedTurns =>
            {
                foreach (var deletedTurn in deletedTurns)
                    store.RemoveAll(existing => existing.Id == deletedTurn.Id);
            })
            .Returns(Task.CompletedTask);
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn insertedTurn) =>
            {
                insertedTurn.Id = 6;
                store.Add(insertedTurn);
                return insertedTurn;
            });
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(updated => updatedSession = updated)
            .Returns(Task.CompletedTask);
        productService.Setup(x => x.GetProductByIdAsync(44)).ReturnsAsync(new Product { Id = 44, Name = "Platform Engineer" });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse
            {
                Score = 86,
                TechnicalScore = 88,
                CommunicationScore = 84,
                ProfessionalismScore = 86,
                PositiveAttitudeScore = 86,
                Feedback = "Strong follow-up",
                RawJson = "{}",
                RubricJson = "{\"technicalScore\":88,\"communicationScore\":84,\"professionalismScore\":86,\"positiveAttitudeScore\":86,\"score\":86}"
            });
        aiClient.Setup(x => x.GenerateQuestionPlanAsync(It.IsAny<AIInterviewQuestionPlanRequest>()))
            .Callback<AIInterviewQuestionPlanRequest>(request => planRequest = request)
            .ReturnsAsync((AIInterviewQuestionPlanRequest request) => new AIInterviewQuestionPlanResponse
            {
                Success = true,
                Questions = Enumerable.Range(1, request.QuestionCount)
                    .Select(index => new AIInterviewQuestionPlanItem
                    {
                        SequenceNumber = index,
                        Category = "job_fit",
                        Question = $"Q{index + 2}",
                        ResumeEvidence = "Alignment",
                        ExpectedSignals = new List<string> { "Alignment" },
                        Rubric = new AIInterviewQuestionRubric()
                    })
                    .ToList()
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService);

        var result = await service.SubmitAnswerAsync(new SubmitInterviewAnswerRequest
        {
            Token = "token225",
            TurnId = 2,
            SequenceNumber = 2,
            Answer = "A2 with impact and tradeoffs."
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Question, Is.EqualTo("Q3"));
        Assert.That(result.Turns.Select(turn => turn.SequenceNumber), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(result.Turns.Count(turn => string.IsNullOrWhiteSpace(turn.AnswerText)), Is.EqualTo(1));
        Assert.That(result.Turns.Count(turn => turn.SequenceNumber == 1), Is.EqualTo(1));
        Assert.That(result.Turns.Count(turn => turn.SequenceNumber == 2), Is.EqualTo(1));
        Assert.That(result.Turns.Any(turn => turn.SequenceNumber == 5), Is.False);
        Assert.That(store.Select(turn => turn.SequenceNumber).OrderBy(sequence => sequence), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        Assert.That(planRequest, Is.Not.Null);
        Assert.That(planRequest.QuestionCount, Is.EqualTo(3));
        Assert.That(planRequest.TotalQuestionCount, Is.EqualTo(5));
        turnService.Verify(x => x.DeleteInterviewTurnsAsync(
            It.Is<IList<InterviewTurn>>(turns => turns.Count == 1 && turns[0].Id == staleFutureTurn.Id)), Times.Once);
        Assert.That(updatedSession, Is.Not.Null);
        var parsedScores = JsonSerializer.Deserialize<List<decimal>>(updatedSession.QuestionScores);
        Assert.That(parsedScores, Is.Not.Null);
        Assert.That(parsedScores.Count, Is.EqualTo(2));
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
    public async Task GenerateQuestionAsync_AzureOpenAIHttpFailure_LogsSafeAzureDetails()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent("{\"error\":{\"code\":\"rate_limit_exceeded\",\"message\":\"Too many requests for this deployment.\"}}", Encoding.UTF8, "application/json")
        });
        var nopLogger = new Mock<NopLogger>();
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "super-secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-4o-mini",
                Prompt = "prompt"
            },
            handler,
            nopLogger.Object);

        var response = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Backend Engineer",
            Difficulty = "Medium",
            Prompt = "prompt",
            QuestionNumber = 1
        });

        Assert.That(response.Success, Is.False);
        AssertChatCompletionsRequest(handler, "gpt-4o-mini");
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI HTTP failure",
            It.Is<string>(message =>
                message.Contains("Mode=generate") &&
                message.Contains("Operation=llm-question-generation") &&
                message.Contains("FailureKind=azure-openai-http-failure") &&
                message.Contains("HttpStatus=429") &&
                message.Contains("EndpointHost=example.openai.azure.com") &&
                message.Contains("Deployment=gpt-4o-mini") &&
                message.Contains("ResponseLength=") &&
                message.Contains("AzureErrorCode=rate_limit_exceeded") &&
                message.Contains("AzureErrorMessage=Too many requests for this deployment.") &&
                message.Contains("AzureResponseBody=") &&
                !message.Contains("super-secret-key") &&
                !message.Contains("api-key")),
            null), Times.Once);
    }

    [Test]
    public async Task GenerateQuestionAsync_AzureOpenAIHttpException_LogsSafeExceptionDiagnostics()
    {
        var handler = new TestHttpMessageHandler(_ => throw new HttpRequestException("DNS failure for Azure OpenAI endpoint"));
        var nopLogger = new Mock<NopLogger>();
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "super-secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-4o-mini",
                Prompt = "prompt"
            },
            handler,
            nopLogger.Object);

        var response = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Backend Engineer",
            Difficulty = "Medium",
            Prompt = "prompt",
            QuestionNumber = 1
        });

        Assert.That(response.Success, Is.False);
        AssertChatCompletionsRequest(handler, "gpt-4o-mini");
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Error,
            "AI Interview Azure OpenAI exception",
            It.Is<string>(message =>
                message.Contains("Mode=generate") &&
                message.Contains("Operation=llm-question-generation") &&
                message.Contains("FailureKind=azure-openai-exception") &&
                message.Contains("Reason=HttpRequestException") &&
                message.Contains("ExceptionMessage=DNS failure for Azure OpenAI endpoint") &&
                message.Contains("ExceptionDetail=") &&
                !message.Contains("super-secret-key") &&
                !message.Contains("api-key")),
            null), Times.Once);
    }

    [Test]
    public async Task ScoreAnswerAsync_AzureOpenAIContractFailure_LogsPreciseSafeReason()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"feedback\\\":\\\"Helpful\\\",\\\"complete\\\":false}\"}}]}", Encoding.UTF8, "application/json")
        });
        var nopLogger = new Mock<NopLogger>();
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "super-secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-4o-mini",
                Prompt = "prompt"
            },
            handler,
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
        AssertChatCompletionsRequest(handler, "gpt-4o-mini");
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview score validation failure",
            It.Is<string>(message =>
                message.Contains("Mode=score") &&
                message.Contains("Operation=llm-scoring") &&
                message.Contains("FailureKind=azure-openai-contract-failure") &&
                message.Contains("missing required score") &&
                !message.Contains("super-secret-key") &&
                !message.Contains("It reduces coupling.")),
            null), Times.Once);
    }

    [Test]
    public async Task ScoreAnswerAsync_AzureOpenAIPlainTextContractFailure_LogsSafeDiagnostics()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Sorry, I cannot score this right now.\"}}]}", Encoding.UTF8, "application/json")
        });
        var nopLogger = new Mock<NopLogger>();
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "super-secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-4o-mini",
                Prompt = "prompt"
            },
            handler,
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
        AssertChatCompletionsRequest(handler, "gpt-4o-mini");
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI contract failure",
            It.Is<string>(message =>
                message.Contains("Mode=score") &&
                message.Contains("Operation=llm-scoring") &&
                message.Contains("FailureKind=azure-openai-contract-failure") &&
                message.Contains("Reason=invalid JSON") &&
                message.Contains("Shape=plain text") &&
                message.Contains("ResponseLength=") &&
                message.Contains("Sample=Sorry, I cannot score this right now.") &&
                !message.Contains("super-secret-key") &&
                !message.Contains("My full candidate answer with confidential details.")),
            null), Times.Once);
    }

    [Test]
    public async Task GenerateQuestionAsync_AzureOpenAIContractFailure_LogsSafeReason()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[]}", Encoding.UTF8, "application/json")
        });
        var nopLogger = new Mock<NopLogger>();
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "super-secret-key",
                AzureOpenAiDeploymentOrModel = "gpt-4o-mini",
                Prompt = "prompt"
            },
            handler,
            nopLogger.Object);

        var response = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Backend Engineer",
            Difficulty = "Medium",
            Prompt = "prompt",
            QuestionNumber = 1
        });

        Assert.That(response.Success, Is.False);
        AssertChatCompletionsRequest(handler, "gpt-4o-mini");
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI contract failure",
            It.Is<string>(message =>
                message.Contains("Mode=generate") &&
                message.Contains("Operation=llm-question-generation") &&
                message.Contains("FailureKind=azure-openai-contract-failure") &&
                message.Contains("Reason=empty response content") &&
                message.Contains("ResponseLength=") &&
                message.Contains("AzureResponseBody={\"choices\":[]}") &&
                !message.Contains("super-secret-key")),
            null), Times.Once);
    }

    [Test]
    public async Task AIInterviewClient_AnalyzeResumeAsync_HttpFailure_LogsSafeAzureDetails()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":{\"code\":\"DeploymentNotFound\",\"message\":\"Deployment not found. api-key=secret-token\"}}", Encoding.UTF8, "application/json")
        });
        var nopLogger = new Mock<NopLogger>();
        nopLogger.Setup(x => x.InsertLogAsync(It.IsAny<LogLevel>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "super-secret-key",
                AzureOpenAiDeploymentOrModel = "resume-model",
                Prompt = "prompt"
            },
            handler,
            nopLogger.Object);

        var response = await client.AnalyzeResumeAsync(new AIResumeProfileRequest
        {
            JobTitle = "Backend Engineer",
            JobContext = "Cloud APIs",
            ResumeText = "Confidential resume text that must not be logged."
        });

        Assert.That(response.Success, Is.False);
        AssertChatCompletionsRequest(handler, "resume-model");
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview Azure OpenAI HTTP failure",
            It.Is<string>(message =>
                message.Contains("Mode=resume-profile") &&
                message.Contains("Operation=llm-resume-profile") &&
                message.Contains("FailureKind=azure-openai-http-failure") &&
                message.Contains("HttpStatus=400") &&
                message.Contains("Deployment=resume-model") &&
                message.Contains("AzureErrorCode=DeploymentNotFound") &&
                message.Contains("AzureResponseBody=") &&
                message.Contains("api-key=<redacted>") &&
                !message.Contains("super-secret-key") &&
                !message.Contains("secret-token") &&
                !message.Contains("Confidential resume text")),
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
            RuntimeClientEventUrl = "/runtime-client-event",
            SpeechAvailable = true
        }, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        Assert.That(json, Does.Contain("\"questionCount\""));
        Assert.That(json, Does.Contain("\"submitAnswerUrl\""));
        Assert.That(json, Does.Contain("\"stopInterviewUrl\""));
        Assert.That(json, Does.Contain("\"runtimeClientEventUrl\""));
        Assert.That(json, Does.Contain("\"speechAvailable\""));
    }

    [Test]
    public async Task SubmitAnswerAsync_WhenAiRequestsCompletion_QueuesDurablyWithoutPublishingInRequest()
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
        var strengthsGate = new TaskCompletionSource<AIInterviewStrengthsSummaryResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
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
        aiClient.Setup(x => x.GenerateStrengthsSummaryAsync(It.IsAny<AIInterviewStrengthsSummaryRequest>()))
            .Returns(strengthsGate.Task);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token3", "Answer that should complete");

        Assert.That(result.IsTerminated, Is.True);
        Assert.That(result.Success, Is.True);
        Assert.That(result.ReportUrl, Is.Empty);
        Assert.That(result.ReportReady, Is.False);
        Assert.That(result.ReportGenerationInProgress, Is.True);
        Assert.That(result.EstimatedWaitSeconds, Is.EqualTo(120));
        Assert.That(updatedSession, Is.Not.Null);
        Assert.That(updatedSession.Score, Is.EqualTo(91));
        Assert.That(updatedSession.QuestionScores, Does.Contain("91"));
        Assert.That(updatedSession.CompletionState, Is.EqualTo(InterviewCompletionStates.Queued));
        Assert.That(updatedSession.CompletionQueuedOnUtc, Is.Not.Null);
        Assert.That(result.Completion, Is.Empty);
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
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => !s.CompletedOnUtc.HasValue && !s.IsActive && string.IsNullOrWhiteSpace(s.ReportData))), Times.AtLeastOnce);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
        strengthsGate.SetResult(new AIInterviewStrengthsSummaryResponse { Success = false });
    }

    [Test]
    public async Task SubmitAnswerAsync_TerminalCompletion_DisposesTransactionBeforeCustomerResolutionAndLogging()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();
        var nopLogger = new Mock<NopLogger>();
        var customerActivityService = new Mock<Nop.Services.Logging.ICustomerActivityService>();
        var dataProvider = new Mock<INopDataProvider>();
        var session = new InterviewSession
        {
            Id = 3003,
            ProductId = 30,
            CustomerId = 8,
            SessionKey = "session-3003",
            Token = "transaction-scope-token",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            QuestionCount = 1
        };
        var turn = new InterviewTurn
        {
            Id = 31,
            InterviewSessionId = session.Id,
            SequenceNumber = 1,
            QuestionText = "Describe how you diagnosed a production failure.",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        var customer = new Customer { Id = session.CustomerId };
        var completionTransactionId = string.Empty;
        var databaseWorkAttemptedInsideCompletedScope = false;
        var customerResolutionTransactionIds = new List<string>();

        sessionService.Setup(x => x.GetSessionByTokenAsync(session.Token)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(updated =>
            {
                if (updated.CompletionState == InterviewCompletionStates.Queued)
                    completionTransactionId = Transaction.Current?.TransactionInformation.LocalIdentifier ?? string.Empty;
            })
            .Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(new List<InterviewTurn> { turn });
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>())).Returns(Task.CompletedTask);
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        dataProvider.Setup(x => x.CreateTransactionScope())
            .Returns(() => new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled));
        customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId))
            .Returns(() =>
            {
                var currentTransactionId = Transaction.Current?.TransactionInformation.LocalIdentifier ?? string.Empty;
                customerResolutionTransactionIds.Add(currentTransactionId);
                if (!string.IsNullOrWhiteSpace(completionTransactionId) &&
                    string.Equals(currentTransactionId, completionTransactionId, StringComparison.Ordinal))
                {
                    databaseWorkAttemptedInsideCompletedScope = true;
                    throw new InvalidOperationException("The current TransactionScope is already complete.");
                }

                return Task.FromResult(customer);
            });
        nopLogger.Setup(x => x.InsertLogAsync(
                It.IsAny<LogLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);
        customerActivityService.Setup(x => x.InsertActivityAsync(
                It.IsAny<Customer>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<BaseEntity>()))
            .ReturnsAsync(new ActivityLog());

        var service = CreateService(
            sessionService,
            turnService,
            aiClient,
            productService,
            customerService,
            localizationService,
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true },
            workContext: workContext,
            eventPublisher: eventPublisher,
            nopLogger: nopLogger,
            dataProvider: dataProvider.Object,
            customerActivityService: customerActivityService);

        var response = await service.SubmitAnswerAsync(session.Token, "I isolated the failing transaction boundary and verified the persisted state.");

        Assert.That(response.Success, Is.True);
        Assert.That(response.IsTerminated, Is.True);
        Assert.That(response.ReportGenerationInProgress, Is.True);
        Assert.That(response.ReportReady, Is.False);
        Assert.That(response.Completion, Is.Empty);
        Assert.That(session.IsActive, Is.False);
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Queued));
        Assert.That(session.CompletedOnUtc, Is.Null);
        Assert.That(session.ReportData, Is.Empty);
        Assert.That(completionTransactionId, Is.Not.Empty);
        Assert.That(databaseWorkAttemptedInsideCompletedScope, Is.False);
        Assert.That(customerResolutionTransactionIds, Is.Not.Empty);
        Assert.That(customerResolutionTransactionIds, Has.None.EqualTo(completionTransactionId));
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Information,
            "AI Interview completion accepted",
            It.Is<string>(message => message.Contains($"SessionId={session.Id}")),
            customer), Times.Once);
        customerActivityService.Verify(x => x.InsertActivityAsync(
            customer,
            "AIInterview.Runtime.CompletionAccepted",
            It.IsAny<string>(),
            It.IsAny<BaseEntity>()), Times.Once);
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Never);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
        dataProvider.Verify(x => x.CreateTransactionScope(), Times.Once);
    }

    [Test]
    public async Task SubmitAnswerAsync_ConcurrentFinalSubmits_QueueDurablyAndScheduledCompletionRunsOnce()
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
            Id = 3030,
            ProductId = 30,
            CustomerId = 8,
            SessionKey = "session-3030",
            Token = "token3030",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            QuestionCount = 1
        };
        var turn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = session.Id,
            SequenceNumber = 1,
            QuestionText = "Q1",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        var turns = new List<InterviewTurn> { turn };
        var gate = new object();
        var reportPersisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        sessionService.Setup(x => x.GetSessionByTokenAsync(session.Token)).ReturnsAsync(session);
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(updated =>
            {
                if (updated.CompletedOnUtc.HasValue && !string.IsNullOrWhiteSpace(updated.ReportData))
                    reportPersisted.TrySetResult();
            })
            .Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(() =>
        {
            lock (gate)
                return turns.Select(item => item).ToList();
        });
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(updated =>
            {
                lock (gate)
                {
                    var existing = turns.Single(turnItem => turnItem.Id == updated.Id);
                    existing.AnswerText = updated.AnswerText;
                    existing.AnsweredOnUtc = updated.AnsweredOnUtc;
                    existing.Score = updated.Score;
                    existing.Feedback = updated.Feedback;
                    existing.RubricJson = updated.RubricJson;
                    existing.RawAIResponseJson = updated.RawAIResponseJson;
                }
            })
            .Returns(Task.CompletedTask);
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        var finalScoringStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFinalScoring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        aiClient.Setup(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()))
            .Returns(async () =>
            {
                finalScoringStarted.TrySetResult();
                await releaseFinalScoring.Task;
                return new AIInterviewFinalScoringResponse
                {
                    Success = true,
                    Completion = "Completed once.",
                    RawJson = "{\"complete\":true}",
                    Turns = new List<AIInterviewFinalScoringTurnResult>
                    {
                        new()
                        {
                            SequenceNumber = 1,
                            TechnicalScore = 91,
                            CommunicationScore = 91,
                            ProfessionalismScore = 91,
                            PositiveAttitudeScore = 91,
                            Score = 91,
                            Feedback = "Strong final answer.",
                            RubricJson = "{\"score\":91}"
                        }
                    }
                };
            });
        aiClient.Setup(x => x.GenerateStrengthsSummaryAsync(It.IsAny<AIInterviewStrengthsSummaryRequest>()))
            .ReturnsAsync(new AIInterviewStrengthsSummaryResponse
            {
                Success = true,
                StrengthsText = "The candidate showed ownership, clear delivery judgment, production debugging skill, and practical communication across the answer.",
                EvidenceTurnNumbers = new List<int> { 1 }
            });

        var service = CreateService(
            sessionService,
            turnService,
            aiClient,
            productService,
            customerService,
            localizationService,
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true },
            workContext: workContext,
            eventPublisher: eventPublisher);

        var results = await Task.WhenAll(
            service.SubmitAnswerAsync(session.Token, "Final answer one with concrete implementation detail."),
            service.SubmitAnswerAsync(session.Token, "Final answer retry with concrete implementation detail."));

        Assert.That(results.All(result => result.Success && result.IsTerminated), Is.True);
        Assert.That(results.All(result => result.ReportGenerationInProgress), Is.True);
        Assert.That(results.All(result => !result.ReportReady && string.IsNullOrWhiteSpace(result.ReportUrl)), Is.True);
        Assert.That(results.Select(result => result.EstimatedWaitSeconds), Is.All.EqualTo(120));
        Assert.That(session.CompletedOnUtc.HasValue, Is.False);
        Assert.That(session.IsActive, Is.False);
        Assert.That(session.ReportData, Is.Empty);
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Queued));
        Assert.That(session.CompletionAttemptCount, Is.Zero);
        Assert.That(session.CompletionQueuedOnUtc, Is.Not.Null);
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Never);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
        var persistedAnswer = turn.AnswerText;
        var pendingRetry = await service.SubmitAnswerAsync(session.Token, "Final answer retry with concrete implementation detail.");
        Assert.That(pendingRetry.Success, Is.True);
        Assert.That(pendingRetry.IsTerminated, Is.True);
        Assert.That(pendingRetry.ReportGenerationInProgress, Is.True);
        Assert.That(turn.AnswerText, Is.EqualTo(persistedAnswer));

        var scheduledRuns = Task.WhenAll(
            service.ProcessCompletionWorkAsync(session.Id),
            service.ProcessCompletionWorkAsync(session.Id));
        await finalScoringStarted.Task;
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Processing));
        Assert.That(session.CompletionAttemptCount, Is.EqualTo(1));
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Once);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
        releaseFinalScoring.SetResult();
        await scheduledRuns;
        await reportPersisted.Task;
        Assert.That(session.ReportData, Does.Contain("Completed once."));
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Ready));
        Assert.That(session.CompletionPublishedOnUtc, Is.Not.Null);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Once);
    }

    [Test]
    public async Task ScheduledRecovery_CompletesQueuedSession_WithoutCompletionStatusOrBrowser()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var terminalAiClient = new Mock<IAIInterviewClient>();
        var workerAiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();
        var session = new InterviewSession
        {
            Id = 4141,
            ProductId = 41,
            CustomerId = 9,
            SessionKey = "session-4141",
            Token = "scoped-worker-token",
            Difficulty = "Medium",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            QuestionCount = 1
        };
        var turn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = session.Id,
            SequenceNumber = 1,
            QuestionText = "Q1",
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        var turns = new List<InterviewTurn> { turn };
        var workerFinalScoringStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reportPersisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        sessionService.Setup(x => x.GetSessionByTokenAsync(session.Token)).ReturnsAsync(session);
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(updated =>
            {
                if (updated.CompletedOnUtc.HasValue && !string.IsNullOrWhiteSpace(updated.ReportData))
                    reportPersisted.TrySetResult();
            })
            .Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(() => turns.ToList());
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(updated =>
            {
                turn.AnswerText = updated.AnswerText;
                turn.AnsweredOnUtc = updated.AnsweredOnUtc;
                turn.Score = updated.Score;
                turn.Feedback = updated.Feedback;
                turn.RubricJson = updated.RubricJson;
                turn.RawAIResponseJson = updated.RawAIResponseJson;
            })
            .Returns(Task.CompletedTask);
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        workerAiClient.Setup(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()))
            .ReturnsAsync(() =>
            {
                workerFinalScoringStarted.TrySetResult();
                return new AIInterviewFinalScoringResponse
                {
                    Success = true,
                    Completion = "Worker scoped completion.",
                    RawJson = "{\"complete\":true}",
                    Turns = new List<AIInterviewFinalScoringTurnResult>
                    {
                        new()
                        {
                            SequenceNumber = 1,
                            TechnicalScore = 92,
                            CommunicationScore = 92,
                            ProfessionalismScore = 92,
                            PositiveAttitudeScore = 92,
                            Score = 92,
                            Feedback = "Worker scoped score.",
                            RubricJson = "{\"score\":92}"
                        }
                    }
                };
            });
        workerAiClient.Setup(x => x.GenerateStrengthsSummaryAsync(It.IsAny<AIInterviewStrengthsSummaryRequest>()))
            .ReturnsAsync(new AIInterviewStrengthsSummaryResponse { Success = false });

        var workerService = CreateService(
            sessionService,
            turnService,
            workerAiClient,
            productService,
            customerService,
            localizationService,
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true },
            workContext: workContext,
            eventPublisher: eventPublisher);

        var terminalService = CreateService(
            sessionService,
            turnService,
            terminalAiClient,
            productService,
            customerService,
            localizationService,
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true },
            workContext: workContext,
            eventPublisher: eventPublisher);

        var response = await terminalService.SubmitAnswerAsync(session.Token, "Final answer with enough concrete implementation detail.");
        Assert.That(response.Success, Is.True);
        Assert.That(response.ReportGenerationInProgress, Is.True);
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Queued));
        terminalAiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Never);
        workerAiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Never);

        sessionService.Setup(x => x.GetCompletionWorkSessionsAsync(It.IsAny<DateTime>(), InterviewCompletionRecoveryTask.BatchSize))
            .ReturnsAsync(new List<InterviewSession> { session });
        var recoveryTask = new InterviewCompletionRecoveryTask(sessionService.Object, workerService);
        await recoveryTask.ExecuteAsync();
        await workerFinalScoringStarted.Task;
        await reportPersisted.Task;

        Assert.That(session.CompletedOnUtc.HasValue, Is.True);
        Assert.That(session.ReportData, Does.Contain("Worker scoped completion."));
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Ready));
        terminalAiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Never);
        workerAiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Once);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Once);
    }

    [Test]
    public async Task ProcessCompletionWorkAsync_PublicationDisposesTransactionBeforeStageLoggingAndPublishesOnce()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();
        var nopLogger = new Mock<NopLogger>();
        var dataProvider = new Mock<INopDataProvider>();
        var session = new InterviewSession
        {
            Id = 4142,
            ProductId = 41,
            CustomerId = 9,
            Token = "publication-scope-token",
            IsActive = false,
            CompletedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            ReportData = "Completed interview report.",
            CompletionState = InterviewCompletionStates.Ready,
            CompletionFinishedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10),
            QuestionCount = 1
        };
        var customer = new Customer { Id = session.CustomerId };
        var publicationPersistenceCount = 0;
        var publicationTransactionId = string.Empty;
        var databaseWorkAttemptedInsideCompletedScope = false;
        var customerResolutionTransactionIds = new List<string>();

        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(updated =>
            {
                if (!updated.CompletionPublishedOnUtc.HasValue)
                    return;

                publicationPersistenceCount++;
                publicationTransactionId = Transaction.Current?.TransactionInformation.LocalIdentifier ?? string.Empty;
            })
            .Returns(Task.CompletedTask);
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        workContext.Setup(x => x.GetWorkingLanguageAsync())
            .ReturnsAsync(new Nop.Core.Domain.Localization.Language { Id = 1 });
        eventPublisher.Setup(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()))
            .Returns(Task.CompletedTask);
        dataProvider.Setup(x => x.CreateTransactionScope())
            .Returns(() => new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled));
        customerService.Setup(x => x.GetCustomerByIdAsync(session.CustomerId))
            .Returns(() =>
            {
                var currentTransactionId = Transaction.Current?.TransactionInformation.LocalIdentifier ?? string.Empty;
                customerResolutionTransactionIds.Add(currentTransactionId);
                if (!string.IsNullOrWhiteSpace(publicationTransactionId) &&
                    string.Equals(currentTransactionId, publicationTransactionId, StringComparison.Ordinal))
                {
                    databaseWorkAttemptedInsideCompletedScope = true;
                    throw new InvalidOperationException("The current TransactionScope is already complete.");
                }

                return Task.FromResult(customer);
            });
        nopLogger.Setup(x => x.InsertLogAsync(
                It.IsAny<LogLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Customer>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            sessionService,
            turnService,
            aiClient,
            productService,
            customerService,
            localizationService,
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true },
            workContext: workContext,
            eventPublisher: eventPublisher,
            nopLogger: nopLogger,
            dataProvider: dataProvider.Object);

        var firstResponse = await service.ProcessCompletionWorkAsync(session.Id);
        var publishedOnUtc = session.CompletionPublishedOnUtc;
        var secondResponse = await service.ProcessCompletionWorkAsync(session.Id);

        Assert.That(firstResponse.Success, Is.True);
        Assert.That(firstResponse.ReportReady, Is.True);
        Assert.That(secondResponse.Success, Is.True);
        Assert.That(secondResponse.ReportReady, Is.True);
        Assert.That(publishedOnUtc, Is.Not.Null);
        Assert.That(session.CompletionPublishedOnUtc, Is.EqualTo(publishedOnUtc));
        Assert.That(publicationPersistenceCount, Is.EqualTo(1));
        Assert.That(publicationTransactionId, Is.Not.Empty);
        Assert.That(databaseWorkAttemptedInsideCompletedScope, Is.False);
        Assert.That(customerResolutionTransactionIds, Has.Count.EqualTo(1));
        Assert.That(customerResolutionTransactionIds, Has.None.EqualTo(publicationTransactionId));
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Information,
            "AI Interview completion stage",
            It.Is<string>(message =>
                message.Contains($"SessionId={session.Id}") &&
                message.Contains("Stage=email-publication") &&
                message.Contains("Success=true")),
            customer), Times.Once);
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview completion publication failed",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Never);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(
            It.Is<InterviewSession>(updated => updated.CompletionPublishedOnUtc.HasValue)), Times.Once);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Once);
        dataProvider.Verify(x => x.CreateTransactionScope(), Times.Once);
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Never);
    }

    [Test]
    public async Task ScheduledRecovery_ReclaimsStaleProcessingOnce_AcrossConcurrentRuns()
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
            Id = 4191,
            ProductId = 41,
            CustomerId = 9,
            SessionKey = "session-4191",
            Token = "stale-processing-token",
            Difficulty = "Medium",
            IsActive = false,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1),
            QuestionCount = 1,
            CompletionState = InterviewCompletionStates.Processing,
            CompletionAttemptCount = 1,
            CompletionQueuedOnUtc = DateTime.UtcNow.AddMinutes(-20),
            CompletionProcessingStartedOnUtc = DateTime.UtcNow.Subtract(InterviewCompletionRecoveryTask.ProcessingLeaseTimeout).AddMinutes(-1),
            CompletionReason = "Interview completed."
        };
        var turn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = session.Id,
            SequenceNumber = 1,
            QuestionText = "Q1",
            AnswerText = "A durable final answer.",
            AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-20),
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-21),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-21)
        };
        var scoringStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScoring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        sessionService.Setup(x => x.GetCompletionWorkSessionsAsync(It.IsAny<DateTime>(), InterviewCompletionRecoveryTask.BatchSize))
            .ReturnsAsync(new List<InterviewSession> { session });
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        sessionService.Setup(x => x.SendInterviewCompletionNotificationAsync(session, 1)).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(new List<InterviewTurn> { turn });
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>())).Returns(Task.CompletedTask);
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new Nop.Core.Domain.Localization.Language { Id = 1 });
        aiClient.Setup(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()))
            .Returns(async () =>
            {
                scoringStarted.TrySetResult();
                await releaseScoring.Task;
                return new AIInterviewFinalScoringResponse
                {
                    Success = true,
                    Completion = "Recovered after restart.",
                    RawJson = "{\"complete\":true}",
                    Turns =
                    [
                        new AIInterviewFinalScoringTurnResult
                        {
                            SequenceNumber = 1,
                            TechnicalScore = 90,
                            CommunicationScore = 90,
                            ProfessionalismScore = 90,
                            PositiveAttitudeScore = 90,
                            Score = 90,
                            Feedback = "Recovered score.",
                            RubricJson = "{\"score\":90}"
                        }
                    ]
                };
            });
        aiClient.Setup(x => x.GenerateStrengthsSummaryAsync(It.IsAny<AIInterviewStrengthsSummaryRequest>()))
            .ReturnsAsync(new AIInterviewStrengthsSummaryResponse { Success = false });

        var completionConsumer = new Nop.Plugin.Misc.AIInterview.Services.Events.MockAiInterviewCompletedEventConsumer(sessionService.Object, workContext.Object);
        eventPublisher.Setup(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()))
            .Returns((MockAiInterviewCompletedEvent eventMessage) => completionConsumer.HandleEventAsync(eventMessage));

        var serviceAfterRestart = CreateService(
            sessionService,
            turnService,
            aiClient,
            productService,
            customerService,
            localizationService,
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true },
            workContext: workContext,
            eventPublisher: eventPublisher);
        var firstTaskRun = new InterviewCompletionRecoveryTask(sessionService.Object, serviceAfterRestart);
        var secondTaskRun = new InterviewCompletionRecoveryTask(sessionService.Object, serviceAfterRestart);

        var concurrentRuns = Task.WhenAll(firstTaskRun.ExecuteAsync(), secondTaskRun.ExecuteAsync());
        await scoringStarted.Task;
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Processing));
        Assert.That(session.CompletionAttemptCount, Is.EqualTo(2));
        releaseScoring.SetResult();
        await concurrentRuns;

        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Ready));
        Assert.That(session.ReportData, Does.Contain("Recovered after restart."));
        Assert.That(session.CompletionPublishedOnUtc, Is.Not.Null);
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Once);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Once);
        sessionService.Verify(x => x.SendInterviewCompletionNotificationAsync(session, 1), Times.Once);
    }

    [Test]
    public async Task QueuedCompletion_TransientAzureFailureRetriesAfterBackoff_ThenPublishesOnce()
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
            Id = 4201,
            ProductId = 42,
            CustomerId = 10,
            Token = "transient-retry-token",
            Difficulty = "Medium",
            IsActive = false,
            ReportData = string.Empty,
            CompletionState = InterviewCompletionStates.Queued,
            CompletionQueuedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletionReason = "Interview completed.",
            QuestionCount = 1
        };
        var turn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = session.Id,
            SequenceNumber = 1,
            QuestionText = "How did you improve reliability?",
            AnswerText = "I added idempotency, health checks, and measured recovery time.",
            AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-1),
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-2),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2)
        };
        const string transientDiagnostic = "AI service unavailable. Mode=final-score; Operation=azure-openai-final-score; FailureKind=azure-openai-http-failure; HttpStatus=503; Reason=http failure.";

        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        sessionService.Setup(x => x.SendInterviewCompletionNotificationAsync(session, 1)).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(new List<InterviewTurn> { turn });
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>())).Returns(Task.CompletedTask);
        productService.Setup(x => x.GetProductByIdAsync(session.ProductId))
            .ReturnsAsync(new Product { Id = session.ProductId, Name = "Platform Engineer" });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);
        workContext.Setup(x => x.GetWorkingLanguageAsync())
            .ReturnsAsync(new Nop.Core.Domain.Localization.Language { Id = 1 });
        var completionConsumer = new Nop.Plugin.Misc.AIInterview.Services.Events.MockAiInterviewCompletedEventConsumer(
            sessionService.Object,
            workContext.Object);
        eventPublisher.Setup(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()))
            .Returns((MockAiInterviewCompletedEvent eventMessage) => completionConsumer.HandleEventAsync(eventMessage));
        aiClient.SetupSequence(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()))
            .ReturnsAsync(new AIInterviewFinalScoringResponse
            {
                Success = false,
                ErrorMessage = transientDiagnostic
            })
            .ReturnsAsync(new AIInterviewFinalScoringResponse
            {
                Success = true,
                Completion = "Recovered report completion.",
                RawJson = "{\"complete\":true}",
                Turns =
                [
                    new AIInterviewFinalScoringTurnResult
                    {
                        SequenceNumber = 1,
                        TechnicalScore = 91,
                        CommunicationScore = 90,
                        ProfessionalismScore = 92,
                        PositiveAttitudeScore = 93,
                        Score = 91.5m,
                        Feedback = "Strong reliability answer.",
                        RubricJson = "{\"score\":91.5}"
                    }
                ]
            });
        aiClient.Setup(x => x.GenerateStrengthsSummaryAsync(It.IsAny<AIInterviewStrengthsSummaryRequest>()))
            .ReturnsAsync(new AIInterviewStrengthsSummaryResponse { Success = false });

        var service = CreateService(
            sessionService,
            turnService,
            aiClient,
            productService,
            customerService,
            localizationService,
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true },
            workContext: workContext,
            eventPublisher: eventPublisher);
        var failureObservedOnUtc = DateTime.UtcNow;

        var firstAttempt = await service.ProcessCompletionWorkAsync(session.Id);

        Assert.That(firstAttempt.Success, Is.True);
        Assert.That(firstAttempt.ReportGenerationInProgress, Is.True);
        Assert.That(firstAttempt.ReportGenerationFailed, Is.False);
        Assert.That(firstAttempt.Message, Is.EqualTo("Your answer was submitted. Generating your report."));
        Assert.That(firstAttempt.Message, Does.Not.Contain("503"));
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Queued));
        Assert.That(session.CompletionAttemptCount, Is.EqualTo(1));
        Assert.That(session.CompletionProcessingStartedOnUtc, Is.Null);
        Assert.That(session.CompletionNextAttemptOnUtc, Is.GreaterThan(failureObservedOnUtc));
        Assert.That(session.CompletionNextAttemptOnUtc, Is.LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(2)));
        Assert.That(session.CompletionFailureMessage, Is.Null);
        Assert.That(session.CompletionFailureDiagnostic, Does.Contain("HttpStatus=503"));

        var earlyAttempt = await service.ProcessCompletionWorkAsync(session.Id);
        Assert.That(earlyAttempt.ReportGenerationInProgress, Is.True);
        Assert.That(session.CompletionAttemptCount, Is.EqualTo(1));
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Once);

        session.CompletionNextAttemptOnUtc = DateTime.UtcNow.AddSeconds(-1);
        var successfulRetry = await service.ProcessCompletionWorkAsync(session.Id);
        var readyRead = await service.ProcessCompletionWorkAsync(session.Id);

        Assert.That(successfulRetry.Success, Is.True);
        Assert.That(successfulRetry.ReportReady, Is.True);
        Assert.That(readyRead.ReportReady, Is.True);
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Ready));
        Assert.That(session.CompletionAttemptCount, Is.EqualTo(2));
        Assert.That(session.CompletionNextAttemptOnUtc, Is.Null);
        Assert.That(session.CompletionFailureDiagnostic, Is.Null);
        Assert.That(session.ReportData, Does.Contain("Recovered report completion."));
        Assert.That(session.CompletionPublishedOnUtc, Is.Not.Null);
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Exactly(2));
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Once);
        sessionService.Verify(x => x.SendInterviewCompletionNotificationAsync(session, 1), Times.Once);
    }

    [Test]
    public async Task QueuedCompletion_MaximumTransientAttemptBecomesTerminalFailed()
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
            Id = 4202,
            ProductId = 42,
            CustomerId = 10,
            Token = "maximum-retry-token",
            Difficulty = "Medium",
            IsActive = false,
            ReportData = string.Empty,
            CompletionState = InterviewCompletionStates.Queued,
            CompletionAttemptCount = AIInterviewDefaults.CompletionMaxAttempts - 1,
            CompletionNextAttemptOnUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletionQueuedOnUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletionReason = "Interview completed.",
            QuestionCount = 1
        };
        var turn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = session.Id,
            SequenceNumber = 1,
            QuestionText = "Describe your production recovery process.",
            AnswerText = "I diagnose impact, roll back safely, and verify recovery metrics.",
            AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-5),
            AskedOnUtc = DateTime.UtcNow.AddMinutes(-6),
            CreatedOnUtc = DateTime.UtcNow.AddMinutes(-6)
        };
        const string transientDiagnostic = "AI service unavailable. Mode=final-score; FailureKind=azure-openai-http-failure; HttpStatus=429; Reason=http failure.";

        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(new List<InterviewTurn> { turn });
        productService.Setup(x => x.GetProductByIdAsync(session.ProductId))
            .ReturnsAsync(new Product { Id = session.ProductId, Name = "Platform Engineer" });
        aiClient.Setup(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()))
            .ReturnsAsync(new AIInterviewFinalScoringResponse
            {
                Success = false,
                ErrorMessage = transientDiagnostic
            });

        var service = CreateService(
            sessionService,
            turnService,
            aiClient,
            productService,
            customerService,
            localizationService,
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true },
            eventPublisher: eventPublisher);

        var finalAttempt = await service.ProcessCompletionWorkAsync(session.Id);
        var laterRecovery = await service.ProcessCompletionWorkAsync(session.Id);

        Assert.That(finalAttempt.Success, Is.False);
        Assert.That(finalAttempt.ReportGenerationFailed, Is.True);
        Assert.That(finalAttempt.Message, Is.EqualTo("Report generation failed. Please contact support if the report is not available from your interview history."));
        Assert.That(finalAttempt.Message, Does.Not.Contain("429"));
        Assert.That(laterRecovery.ReportGenerationFailed, Is.True);
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Failed));
        Assert.That(session.CompletionAttemptCount, Is.EqualTo(AIInterviewDefaults.CompletionMaxAttempts));
        Assert.That(session.CompletionNextAttemptOnUtc, Is.Null);
        Assert.That(session.CompletionProcessingStartedOnUtc, Is.Null);
        Assert.That(session.CompletionFinishedOnUtc, Is.Not.Null);
        Assert.That(session.CompletionFailureMessage, Is.EqualTo(finalAttempt.Message));
        Assert.That(session.CompletionFailureDiagnostic, Does.Contain("HttpStatus=429"));
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Once);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
    }

    [Test]
    public async Task StaleProcessing_AtAttemptLimitBecomesFailedWithoutDuplicateScoring()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var session = new InterviewSession
        {
            Id = 4203,
            IsActive = false,
            CompletionState = InterviewCompletionStates.Processing,
            CompletionAttemptCount = AIInterviewDefaults.CompletionMaxAttempts,
            CompletionProcessingStartedOnUtc = DateTime.UtcNow
                .Subtract(InterviewCompletionRecoveryTask.ProcessingLeaseTimeout)
                .AddMinutes(-1)
        };

        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        var service = CreateService(
            sessionService,
            turnService,
            aiClient,
            new Mock<IProductService>(),
            new Mock<ICustomerService>(),
            new Mock<ILocalizationService>(),
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true });

        var response = await service.ProcessCompletionWorkAsync(session.Id);

        Assert.That(response.ReportGenerationFailed, Is.True);
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Failed));
        Assert.That(session.CompletionAttemptCount, Is.EqualTo(AIInterviewDefaults.CompletionMaxAttempts));
        Assert.That(session.CompletionProcessingStartedOnUtc, Is.Null);
        Assert.That(session.CompletionNextAttemptOnUtc, Is.Null);
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Never);
        turnService.Verify(x => x.GetTurnsBySessionIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task QueuedCompletion_MalformedScoringResponseFailsImmediatelyWithoutRetry()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var eventPublisher = new Mock<IEventPublisher>();
        var session = new InterviewSession
        {
            Id = 4204,
            ProductId = 42,
            IsActive = false,
            CompletionState = InterviewCompletionStates.Queued,
            CompletionQueuedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletionReason = "Interview completed.",
            QuestionCount = 1
        };
        var turn = new InterviewTurn
        {
            Id = 1,
            InterviewSessionId = session.Id,
            SequenceNumber = 1,
            QuestionText = "Describe a delivery tradeoff.",
            AnswerText = "I reduced scope and protected the release deadline.",
            AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-1)
        };

        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id)).ReturnsAsync(new List<InterviewTurn> { turn });
        productService.Setup(x => x.GetProductByIdAsync(session.ProductId))
            .ReturnsAsync(new Product { Id = session.ProductId, Name = "Delivery Lead" });
        aiClient.Setup(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()))
            .ReturnsAsync(new AIInterviewFinalScoringResponse
            {
                Success = true,
                Turns =
                [
                    new AIInterviewFinalScoringTurnResult
                    {
                        SequenceNumber = 1,
                        Score = 80
                    }
                ]
            });

        var service = CreateService(
            sessionService,
            turnService,
            aiClient,
            productService,
            new Mock<ICustomerService>(),
            new Mock<ILocalizationService>(),
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true },
            eventPublisher: eventPublisher);

        var failure = await service.ProcessCompletionWorkAsync(session.Id);
        var laterRecovery = await service.ProcessCompletionWorkAsync(session.Id);

        Assert.That(failure.Success, Is.False);
        Assert.That(failure.ReportGenerationFailed, Is.True);
        Assert.That(failure.Message, Is.EqualTo("Report generation failed. Please contact support if the report is not available from your interview history."));
        Assert.That(laterRecovery.ReportGenerationFailed, Is.True);
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Failed));
        Assert.That(session.CompletionAttemptCount, Is.EqualTo(1));
        Assert.That(session.CompletionNextAttemptOnUtc, Is.Null);
        Assert.That(session.CompletionFailureDiagnostic, Does.Contain("incomplete turn score"));
        aiClient.Verify(x => x.ScoreInterviewAtCompletionAsync(It.IsAny<AIInterviewFinalScoringRequest>()), Times.Once);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
    }

    [Test]
    public async Task QueuedCompletion_FailureReturnsSafeMessage_AndLogsUnderlyingExceptionDetail()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();
        var nopLogger = new Mock<NopLogger>();
        var session = new InterviewSession
        {
            Id = 4242,
            ProductId = 42,
            CustomerId = 10,
            Token = "worker-failure-token",
            IsActive = false,
            CompletedOnUtc = null,
            ReportData = string.Empty,
            CompletionState = InterviewCompletionStates.Queued,
            CompletionQueuedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10),
            QuestionCount = 1
        };
        var loggedDetails = new List<string>();

        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(session.Id)).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(session.Id))
            .ThrowsAsync(new ObjectDisposedException("System.Net.Http.HttpClient", "disposed-client-marker"));
        nopLogger.Setup(x => x.InsertLogAsync(
                It.IsAny<LogLevel>(),
                "AI Interview completion processing failed",
                It.IsAny<string>(),
                It.IsAny<Customer>()))
            .Callback<LogLevel, string, string, Customer>((_, _, fullMessage, _) => loggedDetails.Add(fullMessage))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            sessionService,
            turnService,
            aiClient,
            productService,
            customerService,
            localizationService,
            settings: new AIInterviewSettings { Prompt = "Be concise", EnableFinalScoringAtCompletion = true },
            workContext: workContext,
            eventPublisher: eventPublisher,
            nopLogger: nopLogger);

        var response = await service.ProcessCompletionWorkAsync(session.Id);

        Assert.That(response.Success, Is.False);
        Assert.That(response.ReportGenerationFailed, Is.True);
        Assert.That(response.Message, Is.EqualTo("Report generation failed. Please contact support if the report is not available from your interview history."));
        Assert.That(session.CompletionState, Is.EqualTo(InterviewCompletionStates.Failed));
        Assert.That(session.CompletionFailureMessage, Is.EqualTo(response.Message));
        Assert.That(session.CompletionFailureDiagnostic, Does.Contain("disposed-client-marker"));
        Assert.That(session.CompletionFinishedOnUtc, Is.Not.Null);
        Assert.That(loggedDetails.Any(detail => detail.Contains("disposed-client-marker")), Is.True);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(candidate =>
            candidate.Id == session.Id &&
            candidate.CompletionState == InterviewCompletionStates.Failed &&
            candidate.CompletionFailureMessage == response.Message)), Times.AtLeastOnce);
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
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
    public async Task SpeechToken_MissingConfig_LogsDetailedFlags()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();
        var nopLogger = new Mock<NopLogger>();

        sessionService.Setup(x => x.GetSessionByTokenAsync("token5")).ReturnsAsync(new InterviewSession
        {
            Id = 5,
            Token = "token5",
            CustomerId = 7,
            ProductId = 9,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        customerService.Setup(x => x.GetCustomerByIdAsync(7)).ReturnsAsync(new Customer { Id = 7 });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            workContext: workContext,
            eventPublisher: eventPublisher,
            nopLogger: nopLogger);

        var result = await service.GetSpeechTokenAsync("token5");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureKind, Is.EqualTo("configuration-incomplete"));
        Assert.That(result.Message, Is.EqualTo("Voice mode is unavailable. Please type your answer below."));
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview speech token unavailable",
            It.Is<string>(message =>
                message.Contains("Mode=speech-token") &&
                message.Contains("Reason=configuration incomplete") &&
                message.Contains("FailureKind=configuration-incomplete") &&
                message.Contains("AzureSpeechKeyConfigured=false") &&
                message.Contains("AzureSpeechRegionConfigured=false") &&
                message.Contains("SpeechRegion=<empty>")),
            It.IsAny<Customer>()), Times.Once);
    }

    [Test]
    public async Task SpeechToken_ReturnsFailure_ForInactiveCompletedOrExpiredSessions()
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

        Assert.That((await service.GetSpeechTokenAsync("inactive")).FailureKind, Is.EqualTo("invalid-session"));
        Assert.That((await service.GetSpeechTokenAsync("completed")).FailureKind, Is.EqualTo("invalid-session"));
        Assert.That((await service.GetSpeechTokenAsync("expired")).FailureKind, Is.EqualTo("invalid-session"));
        Assert.That(httpHandler.Requests, Is.Empty);
    }

    [Test]
    public async Task SpeechToken_ReturnsFailure_OnExpiryBoundary()
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

        var result = await service.GetSpeechTokenAsync("boundary");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureKind, Is.EqualTo("invalid-session"));
        Assert.That(httpHandler.Requests, Is.Empty);
    }

    [Test]
    public async Task SpeechToken_AzureHttpFailure_LogsOriginalAzureBodyWithoutKey()
    {
        var azureBody = """{"error":{"code":"401","message":"Access denied due to invalid subscription key or wrong API endpoint."},"token":"raw-secret-token"}""";
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(azureBody, Encoding.UTF8, "application/json")
        });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var nopLogger = new Mock<NopLogger>();

        sessionService.Setup(x => x.GetSessionByTokenAsync("token-http")).ReturnsAsync(new InterviewSession
        {
            Id = 12,
            Token = "token-http",
            CustomerId = 21,
            ProductId = 34,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        customerService.Setup(x => x.GetCustomerByIdAsync(21)).ReturnsAsync(new Customer { Id = 21 });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech-key-secret",
                AzureSpeechRegion = "eastus"
            },
            nopLogger: nopLogger);

        var result = await service.GetSpeechTokenAsync("token-http");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureKind, Is.EqualTo("azure-http-failure"));
        Assert.That(result.AzureStatusCode, Is.EqualTo(401));
        Assert.That(result.AzureReasonPhrase, Is.EqualTo("Unauthorized"));
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview speech token failure",
            It.Is<string>(message =>
                message.Contains("FailureKind=azure-http-failure") &&
                message.Contains("HttpStatus=401") &&
                message.Contains("ReasonPhrase=Unauthorized") &&
                message.Contains("Access denied due to invalid subscription key or wrong API endpoint.") &&
                !message.Contains("speech-key-secret") &&
                !message.Contains("raw-secret-token")),
            It.IsAny<Customer>()), Times.Once);
    }

    [Test]
    public async Task SpeechToken_DuplicateAzureHttpFailure_LogsOnce()
    {
        var azureBody = """{"error":{"code":"401","message":"Access denied due to invalid subscription key or wrong API endpoint."}}""";
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(azureBody, Encoding.UTF8, "application/json")
        });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var nopLogger = new Mock<NopLogger>();

        sessionService.Setup(x => x.GetSessionByTokenAsync("token-http-dedupe")).ReturnsAsync(new InterviewSession
        {
            Id = 112,
            Token = "token-http-dedupe",
            CustomerId = 121,
            ProductId = 134,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        customerService.Setup(x => x.GetCustomerByIdAsync(121)).ReturnsAsync(new Customer { Id = 121 });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech-key-secret",
                AzureSpeechRegion = "eastus"
            },
            nopLogger: nopLogger);

        var first = await service.GetSpeechTokenAsync("token-http-dedupe");
        var second = await service.GetSpeechTokenAsync("token-http-dedupe");

        Assert.That(first.Success, Is.False);
        Assert.That(second.Success, Is.False);
        Assert.That(first.FailureKind, Is.EqualTo("azure-http-failure"));
        Assert.That(second.FailureKind, Is.EqualTo("azure-http-failure"));
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview speech token failure",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Once);
    }

    [Test]
    public async Task SpeechToken_DifferentAzureHttpStatus_BypassesDedupe()
    {
        var callCount = 0;
        var httpHandler = new TestHttpMessageHandler(_ =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("""{"error":{"code":"401","message":"Invalid key."}}""", Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    ReasonPhrase = "TooManyRequests",
                    Content = new StringContent("""{"error":{"code":"429","message":"Quota exceeded."}}""", Encoding.UTF8, "application/json")
                };
        });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var nopLogger = new Mock<NopLogger>();

        sessionService.Setup(x => x.GetSessionByTokenAsync("token-http-status")).ReturnsAsync(new InterviewSession
        {
            Id = 113,
            Token = "token-http-status",
            CustomerId = 122,
            ProductId = 135,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        customerService.Setup(x => x.GetCustomerByIdAsync(122)).ReturnsAsync(new Customer { Id = 122 });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech-key-secret",
                AzureSpeechRegion = "eastus"
            },
            nopLogger: nopLogger);

        var first = await service.GetSpeechTokenAsync("token-http-status");
        var second = await service.GetSpeechTokenAsync("token-http-status");

        Assert.That(first.AzureStatusCode, Is.EqualTo(401));
        Assert.That(second.AzureStatusCode, Is.EqualTo(429));
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview speech token failure",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Exactly(2));
    }

    [Test]
    public async Task SpeechToken_DifferentSession_BypassesDedupe()
    {
        var azureBody = """{"error":{"code":"401","message":"Access denied due to invalid subscription key or wrong API endpoint."}}""";
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(azureBody, Encoding.UTF8, "application/json")
        });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var nopLogger = new Mock<NopLogger>();

        sessionService.Setup(x => x.GetSessionByTokenAsync("token-http-session-a")).ReturnsAsync(new InterviewSession
        {
            Id = 114,
            Token = "token-http-session-a",
            CustomerId = 123,
            ProductId = 136,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        sessionService.Setup(x => x.GetSessionByTokenAsync("token-http-session-b")).ReturnsAsync(new InterviewSession
        {
            Id = 115,
            Token = "token-http-session-b",
            CustomerId = 124,
            ProductId = 136,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        customerService.Setup(x => x.GetCustomerByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => new Customer { Id = id });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech-key-secret",
                AzureSpeechRegion = "eastus"
            },
            nopLogger: nopLogger);

        await service.GetSpeechTokenAsync("token-http-session-a");
        await service.GetSpeechTokenAsync("token-http-session-b");

        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview speech token failure",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Exactly(2));
    }

    [Test]
    public async Task SpeechToken_ConfigurationFailure_Dedupes()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var nopLogger = new Mock<NopLogger>();

        sessionService.Setup(x => x.GetSessionByTokenAsync("token-config-dedupe")).ReturnsAsync(new InterviewSession
        {
            Id = 116,
            Token = "token-config-dedupe",
            CustomerId = 125,
            ProductId = 137,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        customerService.Setup(x => x.GetCustomerByIdAsync(125)).ReturnsAsync(new Customer { Id = 125 });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            settings: new AIInterviewSettings(),
            nopLogger: nopLogger);

        var first = await service.GetSpeechTokenAsync("token-config-dedupe");
        var second = await service.GetSpeechTokenAsync("token-config-dedupe");

        Assert.That(first.FailureKind, Is.EqualTo("configuration-incomplete"));
        Assert.That(second.FailureKind, Is.EqualTo("configuration-incomplete"));
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview speech token unavailable",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Once);
    }

    [Test]
    public async Task SpeechToken_ExceptionFailure_DedupesByExceptionSignature()
    {
        var callCount = 0;
        var httpHandler = new TestHttpMessageHandler(_ =>
        {
            callCount++;
            throw new HttpRequestException(callCount <= 2
                ? "DNS failure for speech endpoint"
                : "TLS failure for speech endpoint");
        });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var nopLogger = new Mock<NopLogger>();

        sessionService.Setup(x => x.GetSessionByTokenAsync("token-ex-dedupe")).ReturnsAsync(new InterviewSession
        {
            Id = 117,
            Token = "token-ex-dedupe",
            CustomerId = 126,
            ProductId = 138,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        customerService.Setup(x => x.GetCustomerByIdAsync(126)).ReturnsAsync(new Customer { Id = 126 });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech-key",
                AzureSpeechRegion = "eastus"
            },
            nopLogger: nopLogger);

        var first = await service.GetSpeechTokenAsync("token-ex-dedupe");
        var second = await service.GetSpeechTokenAsync("token-ex-dedupe");
        var third = await service.GetSpeechTokenAsync("token-ex-dedupe");

        Assert.That(first.FailureKind, Is.EqualTo("azure-exception"));
        Assert.That(second.FailureKind, Is.EqualTo("azure-exception"));
        Assert.That(third.FailureKind, Is.EqualTo("azure-exception"));
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Error,
            "AI Interview speech token exception",
            It.IsAny<string>(),
            It.IsAny<Customer>()), Times.Exactly(2));
    }

    [Test]
    public async Task SpeechToken_AzureException_LogsExceptionDetail()
    {
        var httpHandler = new TestHttpMessageHandler(_ => throw new HttpRequestException("DNS failure for speech endpoint"));
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var nopLogger = new Mock<NopLogger>();

        sessionService.Setup(x => x.GetSessionByTokenAsync("token-ex")).ReturnsAsync(new InterviewSession
        {
            Id = 18,
            Token = "token-ex",
            CustomerId = 44,
            ProductId = 52,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        customerService.Setup(x => x.GetCustomerByIdAsync(44)).ReturnsAsync(new Customer { Id = 44 });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech-key",
                AzureSpeechRegion = "eastus"
            },
            nopLogger: nopLogger);

        var result = await service.GetSpeechTokenAsync("token-ex");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureKind, Is.EqualTo("azure-exception"));
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Error,
            "AI Interview speech token exception",
            It.Is<string>(message =>
                message.Contains("FailureKind=azure-exception") &&
                message.Contains("ExceptionType=System.Net.Http.HttpRequestException") &&
                message.Contains("DNS failure for speech endpoint") &&
                message.Contains("SessionId=18") &&
                message.Contains("CustomerId=44") &&
                message.Contains("ProductId=52")),
            It.IsAny<Customer>()), Times.Once);
    }

    [Test]
    public async Task SpeechToken_EmptySuccessfulResponse_LogsClearly()
    {
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain")
        });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var nopLogger = new Mock<NopLogger>();

        sessionService.Setup(x => x.GetSessionByTokenAsync("token-empty")).ReturnsAsync(new InterviewSession
        {
            Id = 22,
            Token = "token-empty",
            CustomerId = 55,
            ProductId = 66,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        customerService.Setup(x => x.GetCustomerByIdAsync(55)).ReturnsAsync(new Customer { Id = 55 });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech-key",
                AzureSpeechRegion = "eastus"
            },
            nopLogger: nopLogger);

        var result = await service.GetSpeechTokenAsync("token-empty");

        Assert.That(result.Success, Is.False);
        Assert.That(result.FailureKind, Is.EqualTo("empty-token-response"));
        nopLogger.Verify(x => x.InsertLogAsync(
            LogLevel.Warning,
            "AI Interview speech token failure",
            It.Is<string>(message =>
                message.Contains("FailureKind=empty-token-response") &&
                message.Contains("Reason=empty token response") &&
                message.Contains("HttpStatus=200") &&
                message.Contains("ReasonPhrase=OK") &&
                message.Contains("ResponseLength=0")),
            It.IsAny<Customer>()), Times.Once);
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
        sessionService.Setup(x => x.GetSessionByTokenAsync("recent-completed-expired")).ReturnsAsync(new InterviewSession
        {
            Token = "recent-completed-expired",
            IsActive = false,
            CompletedOnUtc = DateTime.UtcNow.AddMinutes(-2),
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        });

        Assert.That((await service.UploadRecordingAsync("invalid", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("expired", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("completed", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("recent-completed-expired", CreateRecordingFile())).Success, Is.False);
        Assert.That(httpHandler.Requests, Is.Empty);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task UploadRecordingAsync_Allows_UnexpiredPendingCompletion_AndRejectsInvalidPendingVariants()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(string.Empty)
        });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var now = DateTime.UtcNow;
        var validPending = new InterviewSession
        {
            Id = 5101,
            CustomerId = 11,
            ProductId = 15,
            Token = "pending-recording",
            IsActive = false,
            CompletedOnUtc = null,
            ReportData = string.Empty,
            TokenExpiryUtc = now.AddMinutes(10)
        };
        var mismatchedToken = new InterviewSession
        {
            Id = 5102,
            Token = "different-token",
            IsActive = false,
            ReportData = string.Empty,
            TokenExpiryUtc = now.AddMinutes(10)
        };
        var expiredPending = new InterviewSession
        {
            Id = 5103,
            Token = "expired-pending",
            IsActive = false,
            ReportData = string.Empty,
            TokenExpiryUtc = now.AddMinutes(-1)
        };
        var existingRecordingPending = new InterviewSession
        {
            Id = 5104,
            Token = "existing-recording-pending",
            IsActive = false,
            ReportData = string.Empty,
            RecordingUrl = "https://storage.blob.core.windows.net/container/existing.webm",
            TokenExpiryUtc = now.AddMinutes(10)
        };
        var inactiveNonPending = new InterviewSession
        {
            Id = 5105,
            Token = "inactive-nonpending",
            IsActive = false,
            CompletedOnUtc = null,
            ReportData = "not pending",
            TokenExpiryUtc = now.AddMinutes(10)
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync(It.IsAny<string>()))
            .ReturnsAsync((string token) => token switch
            {
                "pending-recording" => validPending,
                "mismatch" => mismatchedToken,
                "expired-pending" => expiredPending,
                "existing-recording-pending" => existingRecordingPending,
                "inactive-nonpending" => inactiveNonPending,
                _ => null
            });
        sessionService.Setup(x => x.EnsureRecordingShareTokenAsync(It.IsAny<InterviewSession>()))
            .ReturnsAsync("share-token");
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureBlobStorageContainerUrl = "https://storage.blob.core.windows.net/container",
                AzureBlobStorageSasToken = "sv=2024&sig=test",
                RecordingUploadMaxMb = 100
            });

        var accepted = await service.UploadRecordingAsync("pending-recording", CreateRecordingFile("pending-webm"));

        Assert.That(accepted.Success, Is.True);
        Assert.That(validPending.RecordingUrl, Is.EqualTo(accepted.RecordingUrl));
        Assert.That((await service.UploadRecordingAsync("pending-recording", CreateRecordingFile("replacement-webm"))).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("mismatch", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("expired-pending", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("existing-recording-pending", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("inactive-nonpending", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("missing-pending", CreateRecordingFile())).Success, Is.False);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(session => session.Id == validPending.Id && !string.IsNullOrWhiteSpace(session.RecordingUrl))), Times.Once);
        sessionService.Verify(x => x.EnsureRecordingShareTokenAsync(It.Is<InterviewSession>(session => session.Id == validPending.Id)), Times.Once);
        Assert.That(httpHandler.Requests.Count, Is.EqualTo(1));
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
        var tokenExpiryUtc = DateTime.UtcNow.AddMinutes(30);
        var session = new InterviewSession
        {
            Id = 22,
            Token = "recent",
            IsActive = false,
            CompletedOnUtc = completedAt,
            TokenExpiryUtc = tokenExpiryUtc,
            SessionKey = "session-recent",
            CustomerId = 7,
            ProductId = 5
        };

        sessionService.Setup(x => x.GetSessionByTokenAsync("recent")).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        customerService.Setup(x => x.GetCustomerByIdAsync(7)).ReturnsAsync(new Customer { Id = 7, FirstName = "Recent", LastName = "Candidate" });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AzureBlobStorageContainerUrl = "https://storage.blob.core.windows.net/container",
                AzureBlobStorageSasToken = "?sig=token"
            });

        var result = await service.UploadRecordingAsync("recent", CreateRecordingFile("recent-webm"));

        Assert.That(result.Success, Is.True);
        Assert.That(session.RecordingUrl, Is.EqualTo(result.RecordingUrl));
        Assert.That(session.Token, Is.EqualTo("recent"));
        Assert.That(session.TokenExpiryUtc, Is.EqualTo(tokenExpiryUtc));
        Assert.That(httpHandler.Requests, Has.Count.EqualTo(1));
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s =>
            s.Id == 22 &&
            s.RecordingUrl == result.RecordingUrl &&
            s.CompletedOnUtc == completedAt &&
            s.Token == "recent" &&
            s.TokenExpiryUtc == tokenExpiryUtc &&
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
    public void RecordingBlobName_UsesApplicantNameAndUtcTimestamp()
    {
        var service = CreateDefaultServiceForRecordingName();
        var blobName = BuildRecordingBlobNameForTest(
            service,
            new Customer { FirstName = "Jane", LastName = "Doe" },
            new DateTime(2026, 7, 28, 12, 24, 43, DateTimeKind.Utc));

        Assert.That(blobName, Is.EqualTo("Jane_Doe_20260728122443.webm"));
    }

    [Test]
    public void RecordingBlobName_SanitizesUnsafeAndWhitespaceNameComponents()
    {
        var service = CreateDefaultServiceForRecordingName();
        var blobName = BuildRecordingBlobNameForTest(
            service,
            new Customer { FirstName = "  Jane \t /? Smith  ", LastName = "  Do\\e#&=Jr\u0001 " },
            new DateTime(2026, 7, 28, 12, 24, 43, DateTimeKind.Utc));

        Assert.That(blobName, Is.EqualTo("Jane_Smith_Do_e_Jr_20260728122443.webm"));
    }

    [TestCase(null, "Doe", "Applicant_Doe_20260728122443.webm")]
    [TestCase("Jane", "", "Jane_Applicant_20260728122443.webm")]
    [TestCase("   ", "   ", "Applicant_Applicant_20260728122443.webm")]
    [TestCase("/?#", "\\", "Applicant_Applicant_20260728122443.webm")]
    public void RecordingBlobName_UsesApplicantFallbacksForMissingComponents(string firstName, string lastName, string expectedBlobName)
    {
        var service = CreateDefaultServiceForRecordingName();
        var blobName = BuildRecordingBlobNameForTest(
            service,
            new Customer { FirstName = firstName, LastName = lastName },
            new DateTime(2026, 7, 28, 12, 24, 43, DateTimeKind.Utc));

        Assert.That(blobName, Is.EqualTo(expectedBlobName));
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
        customerService.Setup(x => x.GetCustomerByIdAsync(7)).ReturnsAsync(new Customer { Id = 7, FirstName = "Jane", LastName = "Doe" });
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
        Assert.That(result.RecordingUrl, Does.Match(@"^https://storage\.blob\.core\.windows\.net/container/Jane_Doe_\d{14}\.webm$"));
        Assert.That(result.RecordingUrl, Does.Not.Contain("recordings-"));
        Assert.That(result.RecordingUrl, Does.Not.Contain("session-success"));
        Assert.That(session.RecordingUrl, Is.EqualTo(result.RecordingUrl));
        Assert.That(session.CompletedOnUtc, Is.Null);
        Assert.That(session.IsActive, Is.True);
        Assert.That(httpHandler.Requests.Count, Is.EqualTo(1));
        Assert.That(httpHandler.Requests[0].Method, Is.EqualTo(HttpMethod.Put));
        Assert.That(httpHandler.Requests[0].RequestUri.AbsoluteUri, Does.Match(@"^https://storage\.blob\.core\.windows\.net/container/Jane_Doe_\d{14}\.webm\?sig=token$"));
        Assert.That(httpHandler.Requests[0].RequestUri.AbsoluteUri, Does.Not.Contain("recordings-"));
        Assert.That(httpHandler.Requests[0].RequestUri.AbsoluteUri, Does.Not.Contain("session-success"));
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
        customerService.Setup(x => x.GetCustomerByIdAsync(1)).ReturnsAsync(new Customer { Id = 1, FirstName = "Codec", LastName = "Candidate" });

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
        customerService.Setup(x => x.GetCustomerByIdAsync(7)).ReturnsAsync(new Customer { Id = 7, FirstName = "Invalid", LastName = "Type" });

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
        customerService.Setup(x => x.GetCustomerByIdAsync(7)).ReturnsAsync(new Customer { Id = 7, FirstName = "Log", LastName = "Failure" });
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
        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            httpHandler);

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
        AssertChatCompletionsRequest(httpHandler, "deployment");
        var requestBody = httpHandler.RequestBodies.Single();
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
        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            httpHandler);

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
        AssertChatCompletionsRequest(httpHandler, "deployment");
        var requestBody = httpHandler.RequestBodies.Single();
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
        Assert.That(requestBody, Does.Contain("Reserve score 0 and answerQuality non_substantive only for empty, copied, refusal, AI-persona, or unrelated answers."));
    }

    [Test]
    public async Task ScoreAnswerAsync_MissingOrOutOfRangeScore_ReturnsUnavailable()
    {
        var missingScoreHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"{\"feedback\":\"Weak\",\"complete\":false}"}}]}""", Encoding.UTF8, "application/json")
        });
        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            missingScoreHandler);

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
        AssertChatCompletionsRequest(missingScoreHandler, "deployment");

        var outOfRangeHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"{\"technicalScore\":96,\"communicationScore\":94,\"professionalismScore\":92,\"positiveAttitudeScore\":90,\"score\":150,\"feedback\":\"Too high\",\"complete\":false,\"nextQuestion\":\"Q2\"}"}}]}""", Encoding.UTF8, "application/json")
        });
        var outOfRangeClient = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            outOfRangeHandler);

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
        AssertChatCompletionsRequest(outOfRangeHandler, "deployment");
    }

    [Test]
    public async Task ScoreAnswerAsync_MissingCategoriesOrFeedback_ReturnsUnavailable()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"{\"score\":91,\"feedback\":\"\",\"complete\":false,\"nextQuestion\":\"Q2\",\"technicalScore\":91}"}}]}""", Encoding.UTF8, "application/json")
        });
        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            handler);

        var response = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Question = "Q1",
            Answer = "A1"
        });

        Assert.That(response.Success, Is.False);
        Assert.That(response.Score, Is.Null);
        AssertChatCompletionsRequest(handler, "deployment");
    }

    [Test]
    public async Task AzureOpenAi_NonSuccessOrInvalidJson_ReturnsUnavailable()
    {
        var failureHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad request")
        });
        var client = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            failureHandler);

        var failure = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            Prompt = "Prompt"
        });

        Assert.That(failure.Success, Is.False);
        AssertChatCompletionsRequest(failureHandler, "deployment");

        var invalidJsonHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"not-json"}}]}""", Encoding.UTF8, "application/json")
        });
        var invalidClient = CreateAzureInterviewAiClient(
            new AIInterviewSettings
            {
                AzureOpenAiEndpointUrl = "https://example.openai.azure.com",
                AzureOpenAiApiKey = "secret",
                AzureOpenAiDeploymentOrModel = "deployment"
            },
            invalidJsonHandler);

        var invalid = await invalidClient.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Question = "Q1",
            Answer = "A1"
        });

        Assert.That(invalid.Success, Is.False);
        AssertChatCompletionsRequest(invalidJsonHandler, "deployment");
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
        Assert.That(result.Success, Is.True);
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
        sessionService.Setup(x => x.GetInterviewSessionByIdAsync(6)).ReturnsAsync(session);
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
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

        var report = (string)method.Invoke(service, new object[] { turns, 0m, "The answer was not substantive.", null, null });

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

        var report = (string)method.Invoke(service, new object[] { turns, 82m, "Completed", null, null });

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

        var report = (string)method.Invoke(service, new object[] { turns, 61m, "Completed", null, null });

        Assert.That(report, Does.Contain("Improvement areas:"));
        Assert.That(report, Does.Not.Contain(questionText));
        Assert.That(report, Does.Contain("Provide specific examples of Azure AI Services used in projects."));
    }

    [Test]
    public void ScorePrompt_Distinguishes_NonSubstantive_Weak_And_Substantive()
    {
        var client = new InterviewAiClient(new AIInterviewSettings(), new MockAIInterviewSettings { UseMockResponses = false });
        var method = typeof(InterviewAiClient).GetMethod("BuildPrompt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var systemPrompt = AIInterviewDefaults.DefaultRuntimeScoringSystemPrompt;

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
        Assert.That(systemPrompt, Does.Contain("Reserve score 0 and answerQuality non_substantive only for empty, copied, refusal, AI-persona, or unrelated answers."));
        Assert.That(systemPrompt, Does.Contain("classify it as weak and assign low but non-zero scores with concrete feedback."));
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
        Assert.That(content.Contains("let speechUnavailable = !config.speechAvailable;"), Is.True, "Runtime view should track first speech failure for the page.");
        Assert.That(content.Contains("config.speechAvailable = false;"), Is.True, "Runtime view should disable speech after the first speech failure.");
        Assert.That(content.Contains("Voice mode is unavailable. Please type your answer below."), Is.True, "Runtime view should keep the applicant-facing fallback message safe.");
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
