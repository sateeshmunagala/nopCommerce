using Moq;
using Microsoft.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Events;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Events;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using NUnit.Framework;
using System.Net;
using System.Net.Http;
using System.Text;
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
        Mock<IHttpClientFactory> httpClientFactory = null,
        AIInterviewSettings settings = null,
        MockAIInterviewSettings mockSettings = null,
        Mock<IWorkContext> workContext = null,
        Mock<IEventPublisher> eventPublisher = null)
    {
        return new InterviewRuntimeService(
            sessionService.Object,
            turnService.Object,
            aiClient.Object,
            productService.Object,
            customerService.Object,
            localizationService.Object,
            settings ?? new AIInterviewSettings { Prompt = "Be concise" },
            mockSettings ?? new MockAIInterviewSettings { UseMockResponses = true },
            httpClientFactory?.Object ?? new Mock<IHttpClientFactory>().Object,
            workContext?.Object ?? new Mock<IWorkContext>().Object,
            eventPublisher?.Object ?? new Mock<IEventPublisher>().Object);
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
    public async Task EnsureInterviewStartedAsync_Creates_First_Turn()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var workContext = new Mock<IWorkContext>();
        var eventPublisher = new Mock<IEventPublisher>();

        var session = new InterviewSession { Id = 1, ProductId = 10, CustomerId = 99, SessionKey = "key", Token = "token", Difficulty = "Medium" };
        var store = new List<InterviewTurn>();
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(1)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        aiClient.Setup(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse { Question = "First question", RawJson = "{\"question\":\"First question\"}" });
        productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Product { Id = 10, Name = "Backend Engineer" });
        customerService.Setup(x => x.GetCustomerByIdAsync(99)).ReturnsAsync(new Customer { Id = 99, FirstName = "Jane", LastName = "Doe" });

        InterviewTurn insertedTurn = null;
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn turn) =>
            {
                insertedTurn = turn;
                store.Add(turn);
                return turn;
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var model = await service.EnsureInterviewStartedAsync(session, new Customer { Id = 99, FirstName = "Jane", LastName = "Doe" });

        Assert.That(model, Is.Not.Null);
        Assert.That(model.CurrentQuestion, Is.EqualTo("First question"));
        Assert.That(insertedTurn, Is.Not.Null);
        Assert.That(insertedTurn.InterviewSessionId, Is.EqualTo(1));
        Assert.That(insertedTurn.SequenceNumber, Is.EqualTo(1));
        aiClient.Verify(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()), Times.Once);
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
        aiClient.Setup(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse { Success = false, ErrorMessage = "AI service unavailable." });

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
        Assert.That(model.CurrentQuestion, Is.EqualTo("AI service unavailable."));
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
        aiClient.Setup(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse { Success = true, Question = "   ", RawJson = "{\"question\":\"   \"}" });

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
        Assert.That(model.CurrentQuestion, Is.EqualTo("AI service unavailable."));
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

        var session = new InterviewSession { Id = 2, ProductId = 20, CustomerId = 5, SessionKey = "key2", Token = "token2", Difficulty = "Medium", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        var turn = new InterviewTurn { Id = 1, InterviewSessionId = 2, SequenceNumber = 1, QuestionText = "Q1", AskedOnUtc = DateTime.UtcNow.AddMinutes(-1), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-1) };
        var store = new List<InterviewTurn> { turn };

        sessionService.Setup(x => x.GetSessionByTokenAsync("token2")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(2)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse { Score = 80, Feedback = "Good", RawJson = "{}" });
        aiClient.Setup(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse { Question = "Q2", RawJson = "{\"question\":\"Q2\"}" });
        productService.Setup(x => x.GetProductByIdAsync(20)).ReturnsAsync(new Product { Id = 20, Name = "QA Engineer" });
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

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token2", "This is a structured answer because it explains impact.");

        Assert.That(result.Success, Is.True);
        Assert.That(result.IsTerminated, Is.False);
        Assert.That(result.Question, Is.EqualTo("Q2"));
        Assert.That(store.Any(x => x.SequenceNumber == 1 && x.AnswerText != null), Is.True);
        Assert.That(store.Any(x => x.SequenceNumber == 2), Is.True);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Score > 0)), Times.Once);
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
        Assert.That(result.Message, Does.Contain("AI service unavailable"));
        Assert.That(inserted, Is.False);
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
        Assert.That(result.Message, Is.EqualTo("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken"));
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

        var result = await service.SubmitAnswerAsync("token13", "answer");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("AI service unavailable"));
        Assert.That(result.Feedback, Does.Contain("AI service unavailable"));
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
    public void RuntimeClientSettings_Serializes_UsingCamelCase()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new RuntimeClientSettingsModel
        {
            SubmitAnswerUrl = "/submit",
            StopInterviewUrl = "/stop",
            SpeechAvailable = true
        }, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

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
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
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
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(updated => session.ReportData = updated.ReportData)
            .Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(3)).ReturnsAsync(new List<InterviewTurn> { turn });
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>())).Returns(Task.CompletedTask);
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>())).ReturnsAsync((InterviewTurn created) => created);
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
                RawJson = "{\"score\":91,\"complete\":true}"
            });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token3", "Answer that should complete");

        Assert.That(result.IsTerminated, Is.True);
        Assert.That(result.Success, Is.True);
        Assert.That(result.ReportUrl, Does.Contain("/aiinterview/report/3"));
        Assert.That(session.ReportData, Does.Contain("AI completion: Interview completed"));
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
        Assert.That(result.Message, Is.EqualTo("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken"));
        eventPublisher.Verify(x => x.PublishAsync(It.IsAny<MockAiInterviewCompletedEvent>()), Times.Never);
    }

    [Test]
    public async Task SpeechAndAgoraTokens_ReturnNull_WhenConfigMissing()
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
        Assert.That(await service.GetAgoraTokenAsync("token5"), Is.Null);
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
    public async Task UploadRecordingAsync_Rejects_Invalid_Expired_Completed_Sessions()
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
            CompletedOnUtc = DateTime.UtcNow.AddMinutes(-1),
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });

        Assert.That((await service.UploadRecordingAsync("invalid", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("expired", CreateRecordingFile())).Success, Is.False);
        Assert.That((await service.UploadRecordingAsync("completed", CreateRecordingFile())).Success, Is.False);
        Assert.That(httpHandler.Requests, Is.Empty);
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
        Assert.That(httpHandler.Requests.Count, Is.EqualTo(1));
        Assert.That(httpHandler.Requests[0].Method, Is.EqualTo(HttpMethod.Put));
        Assert.That(httpHandler.Requests[0].Headers.Contains("x-ms-blob-type"), Is.True);
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.RecordingUrl == result.RecordingUrl)), Times.Once);
    }

    [Test]
    public async Task AgoraToken_ReturnsNull_ForInactiveCompletedOrExpiredSessions()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"token\":\"agora-token\"}") });
        var httpFactory = CreateHttpClientFactory(httpHandler);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AgoraAppId = "app-id",
                AgoraTokenServiceUrl = "https://tokens"
            });

        var inactive = new InterviewSession { Token = "inactive", SessionKey = "channel", CustomerId = 1, IsActive = false, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        var completed = new InterviewSession { Token = "completed", SessionKey = "channel", CustomerId = 1, IsActive = true, CompletedOnUtc = DateTime.UtcNow.AddMinutes(-1), TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        var expired = new InterviewSession { Token = "expired", SessionKey = "channel", CustomerId = 1, IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddSeconds(-1) };

        sessionService.Setup(x => x.GetSessionByTokenAsync("inactive")).ReturnsAsync(inactive);
        sessionService.Setup(x => x.GetSessionByTokenAsync("completed")).ReturnsAsync(completed);
        sessionService.Setup(x => x.GetSessionByTokenAsync("expired")).ReturnsAsync(expired);

        Assert.That(await service.GetAgoraTokenAsync("inactive"), Is.Null);
        Assert.That(await service.GetAgoraTokenAsync("completed"), Is.Null);
        Assert.That(await service.GetAgoraTokenAsync("expired"), Is.Null);
        Assert.That(httpHandler.Requests, Is.Empty);
    }

    [Test]
    public async Task AgoraToken_ReturnsNull_OnExpiryBoundary()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        var httpHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"token\":\"agora-token\"}") });
        var httpFactory = CreateHttpClientFactory(httpHandler);
        var now = DateTime.UtcNow;

        sessionService.Setup(x => x.GetSessionByTokenAsync("boundary")).ReturnsAsync(new InterviewSession
        {
            Token = "boundary",
            SessionKey = "channel",
            CustomerId = 1,
            IsActive = true,
            TokenExpiryUtc = now
        });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: httpFactory,
            settings: new AIInterviewSettings
            {
                AgoraAppId = "app-id",
                AgoraTokenServiceUrl = "https://tokens"
            });

        Assert.That(await service.GetAgoraTokenAsync("boundary"), Is.Null);
        Assert.That(httpHandler.Requests, Is.Empty);
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
            httpFactory.Object);

        var result = await client.GenerateQuestionAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            Prompt = "Use the admin guidance",
            QuestionNumber = 1,
            PreviousQuestions = new List<string>(),
            PreviousScores = new List<decimal>()
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Question, Is.EqualTo("Q1"));
        Assert.That(result.Complete, Is.False);
        var requestBody = await httpHandler.Requests.Single().Content.ReadAsStringAsync();
        Assert.That(requestBody, Does.Contain("Question mode contract"));
        Assert.That(requestBody, Does.Contain("complete:false"));
        Assert.That(requestBody, Does.Contain("optional rubricJson"));
        Assert.That(requestBody, Does.Contain("Use the admin guidance"));
    }

    [Test]
    public async Task ScoreAnswerAsync_MapsAzureSuccessResponse_AndPromptContract()
    {
        var responseJson = """
        {
          "choices": [
            {
              "message": {
                "content": "{\"score\":91,\"feedback\":\"Strong\",\"complete\":true,\"nextQuestion\":\"Q2\",\"completion\":\"done\"}"
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
            httpFactory.Object);

        var result = await client.ScoreAnswerAsync(new AIInterviewClientRequest
        {
            JobTitle = "Engineer",
            Difficulty = "Medium",
            Prompt = "Use the admin guidance",
            Question = "Q1",
            Answer = "A1"
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Score, Is.EqualTo(91));
        Assert.That(result.NextQuestion, Is.EqualTo("Q2"));
        Assert.That(result.Complete, Is.True);
        var requestBody = await httpHandler.Requests.Single().Content.ReadAsStringAsync();
        Assert.That(requestBody, Does.Contain("Scoring mode contract"));
        Assert.That(requestBody, Does.Contain("nextQuestion"));
        Assert.That(requestBody, Does.Contain("completion"));
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
            failureFactory.Object);

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
            invalidFactory.Object);

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
    public async Task AgoraToken_MapsJsonAndPlainResponses()
    {
        var jsonHandler = new TestHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"token\":\"agora-token\",\"channel\":\"channel-1\",\"appId\":\"app-id\",\"uid\":42,\"expiresInSeconds\":900}", Encoding.UTF8, "application/json")
        });
        var jsonFactory = CreateHttpClientFactory(jsonHandler);
        var sessionService = new Mock<IInterviewSessionService>();
        var turnService = new Mock<IInterviewTurnService>();
        var aiClient = new Mock<IAIInterviewClient>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        var localizationService = new Mock<ILocalizationService>();
        sessionService.Setup(x => x.GetSessionByTokenAsync("token")).ReturnsAsync(new InterviewSession
        {
            Token = "token",
            SessionKey = "channel-1",
            CustomerId = 42,
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: jsonFactory,
            settings: new AIInterviewSettings
            {
                AgoraAppId = "app-id",
                AgoraTokenServiceUrl = "https://tokens"
            });

        var jsonResult = await service.GetAgoraTokenAsync("token");
        Assert.That(jsonResult, Is.Not.Null);
        Assert.That(jsonResult.Token, Is.EqualTo("agora-token"));
        Assert.That(jsonResult.Channel, Is.EqualTo("channel-1"));
        Assert.That(jsonResult.AppId, Is.EqualTo("app-id"));
        Assert.That(jsonResult.Uid, Is.EqualTo(42));
        Assert.That(jsonResult.ExpiresInSeconds, Is.EqualTo(900));

        var plainHandler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("plain-token", Encoding.UTF8, "text/plain")
        });
        var plainFactory = CreateHttpClientFactory(plainHandler);
        var plainService = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, httpClientFactory: plainFactory,
            settings: new AIInterviewSettings
            {
                AgoraAppId = "app-id",
                AgoraTokenServiceUrl = "https://tokens"
            });

        var plainResult = await plainService.GetAgoraTokenAsync("token");
        Assert.That(plainResult, Is.Not.Null);
        Assert.That(plainResult.Token, Is.EqualTo("plain-token"));
        Assert.That(plainResult.Channel, Is.EqualTo("channel-1"));
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

        var session = new InterviewSession { Id = 6, ProductId = 60, CustomerId = 7, SessionKey = "session-6", Token = "token6", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(6)).ReturnsAsync(new List<InterviewTurn>());
        aiClient.Setup(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse { Question = "Q1", RawJson = "{\"question\":\"Q1\"}" });

        var missingFlagsService = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            settings: new AIInterviewSettings(),
            mockSettings: new MockAIInterviewSettings { UseMockResponses = true });
        var missingModel = await missingFlagsService.EnsureInterviewStartedAsync(session, new Customer { Id = 7 });
        Assert.That(missingModel.ClientSettings.SpeechAvailable, Is.False);
        Assert.That(missingModel.ClientSettings.AgoraAvailable, Is.False);

        var presentFlagsService = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService,
            settings: new AIInterviewSettings
            {
                AzureSpeechKey = "speech",
                AzureSpeechRegion = "eastus",
                AgoraAppId = "app",
                AgoraTokenServiceUrl = "https://tokens"
            },
            mockSettings: new MockAIInterviewSettings { UseMockResponses = true });
        var presentModel = await presentFlagsService.EnsureInterviewStartedAsync(session, new Customer { Id = 7 });
        Assert.That(presentModel.ClientSettings.SpeechAvailable, Is.True);
        Assert.That(presentModel.ClientSettings.AgoraAvailable, Is.True);
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
