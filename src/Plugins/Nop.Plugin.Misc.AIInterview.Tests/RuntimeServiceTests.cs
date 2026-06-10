using Moq;
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
            workContext?.Object ?? new Mock<IWorkContext>().Object,
            eventPublisher?.Object ?? new Mock<IEventPublisher>().Object);
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
