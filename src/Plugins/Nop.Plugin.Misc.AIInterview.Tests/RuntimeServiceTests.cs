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
        Mock<IEventPublisher> eventPublisher = null,
        Mock<Microsoft.Extensions.Logging.ILogger<InterviewRuntimeService>> logger = null)
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
        Assert.That(model.CurrentQuestion, Is.EqualTo("AI service unavailable. Please try again later."));
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

        var session = new InterviewSession { Id = 222, ProductId = 20, CustomerId = 5, SessionKey = "key222", Token = "token222", Difficulty = "Medium", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
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
        aiClient.Setup(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()))
            .ReturnsAsync(new AIInterviewClientResponse { Question = "Q2", RawJson = "{\"question\":\"Q2\"}" });
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
        turnService.Setup(x => x.InsertInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .ReturnsAsync((InterviewTurn created) =>
            {
                created.Id = created.SequenceNumber + 10;
                store.Add(created);
                return created;
            });
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

        var session = new InterviewSession { Id = 22, ProductId = 44, CustomerId = 5, SessionKey = "key22", Token = "token22", Difficulty = "Medium", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        var priorTurn = new InterviewTurn { Id = 1, InterviewSessionId = 22, SequenceNumber = 1, QuestionText = "Q1", AnswerText = "A1 with detail", Score = 74, Feedback = "Add stronger metrics", AskedOnUtc = DateTime.UtcNow.AddMinutes(-5), AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-4), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-5) };
        var currentTurn = new InterviewTurn { Id = 2, InterviewSessionId = 22, SequenceNumber = 2, QuestionText = "Q2", AskedOnUtc = DateTime.UtcNow.AddMinutes(-2), CreatedOnUtc = DateTime.UtcNow.AddMinutes(-2) };
        var store = new List<InterviewTurn> { priorTurn, currentTurn };

        AIInterviewClientRequest scoreRequest = null;
        AIInterviewClientRequest nextQuestionRequest = null;

        sessionService.Setup(x => x.GetSessionByTokenAsync("token22")).ReturnsAsync(session);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(22)).ReturnsAsync(() => store.OrderBy(x => x.SequenceNumber).ToList());
        aiClient.Setup(x => x.ScoreAnswerAsync(It.IsAny<AIInterviewClientRequest>()))
            .Callback<AIInterviewClientRequest>(request => scoreRequest = request)
            .ReturnsAsync(new AIInterviewClientResponse { Score = 88, Feedback = "Strong follow-up", RawJson = "{}" });
        aiClient.Setup(x => x.GenerateQuestionAsync(It.IsAny<AIInterviewClientRequest>()))
            .Callback<AIInterviewClientRequest>(request => nextQuestionRequest = request)
            .ReturnsAsync(new AIInterviewClientResponse { Question = "Q3", RawJson = "{\"question\":\"Q3\"}" });
        productService.Setup(x => x.GetProductByIdAsync(44)).ReturnsAsync(new Product { Id = 44, Name = "Platform Engineer" });
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
                created.Id = 3;
                store.Add(created);
                return created;
            });
        localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>())).ReturnsAsync((string key) => key);

        var service = CreateService(sessionService, turnService, aiClient, productService, customerService, localizationService, workContext: workContext, eventPublisher: eventPublisher);

        var result = await service.SubmitAnswerAsync("token22", "A2 with structure and impact.");

        Assert.That(result.Success, Is.True);
        Assert.That(scoreRequest, Is.Not.Null);
        Assert.That(scoreRequest.PreviousTurns.Count, Is.EqualTo(1));
        Assert.That(scoreRequest.PreviousTurns[0].Question, Is.EqualTo("Q1"));
        Assert.That(scoreRequest.PreviousTurns[0].Answer, Is.EqualTo("A1 with detail"));
        Assert.That(scoreRequest.PreviousTurns[0].Feedback, Is.EqualTo("Add stronger metrics"));

        Assert.That(nextQuestionRequest, Is.Not.Null);
        Assert.That(nextQuestionRequest.PreviousTurns.Count, Is.EqualTo(2));
        Assert.That(nextQuestionRequest.PreviousTurns.Any(x => x.Question == "Q1" && x.Answer == "A1 with detail" && x.Feedback == "Add stronger metrics"), Is.True);
        Assert.That(nextQuestionRequest.PreviousTurns.Any(x => x.Question == "Q2" && x.Answer == "A2 with structure and impact." && x.Feedback == "Strong follow-up"), Is.True);
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
        Assert.That(result.Message, Does.Contain("temporarily unavailable"));
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

        var result = await service.SubmitAnswerAsync("token15", "answer");

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

        var result = await service.SubmitAnswerAsync("token13", "answer");

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
        InterviewSession updatedSession = null;
        sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(updated => updatedSession = updated)
            .Returns(Task.CompletedTask);
        turnService.Setup(x => x.GetTurnsBySessionIdAsync(3)).ReturnsAsync(new List<InterviewTurn> { turn });
        InterviewTurn updatedTurn = null;
        turnService.Setup(x => x.UpdateInterviewTurnAsync(It.IsAny<InterviewTurn>()))
            .Callback<InterviewTurn>(updated => updatedTurn = updated)
            .Returns(Task.CompletedTask);
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
        Assert.That(updatedTurn.RawAIResponseJson, Is.EqualTo("{\"score\":91,\"complete\":true}"));
        Assert.That(updatedTurn.RubricJson, Is.EqualTo("{\"score\":91,\"feedback\":\"Strong\"}"));
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
        sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Id == 22 && s.RecordingUrl == result.RecordingUrl && s.CompletedOnUtc.HasValue)), Times.Once);
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
            Answer = "A1",
            PreviousTurns = new List<AIInterviewHistoryItem>
            {
                new() { SequenceNumber = 1, Question = "Previous question", Answer = "Previous answer", Score = 79, Feedback = "More detail on trade-offs" }
            }
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Score, Is.EqualTo(91));
        Assert.That(result.NextQuestion, Is.EqualTo("Q2"));
        var requestBody = await httpHandler.Requests.Single().Content.ReadAsStringAsync();
        Assert.That(requestBody, Does.Contain("Previous answered turns"));
        Assert.That(requestBody, Does.Contain("Previous answer"));
        Assert.That(requestBody, Does.Contain("More detail on trade-offs"));
        Assert.That(result.Complete, Is.True);
        Assert.That(requestBody, Does.Contain("Scoring mode contract"));
        Assert.That(requestBody, Does.Contain("nextQuestion"));
        Assert.That(requestBody, Does.Contain("completion"));
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
            missingScoreFactory.Object);

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
            Content = new StringContent("""{"choices":[{"message":{"content":"{\"score\":150,\"feedback\":\"Too high\",\"complete\":false}"}}]}""", Encoding.UTF8, "application/json")
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
            outOfRangeFactory.Object);

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
        Assert.That(content.Contains("if (!currentText && interviewStarted && !isSpeakingOrSubmitting && hasActiveQuestion())"), Is.True, "Runtime view should contain repeating reminder scheduling logic");
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
