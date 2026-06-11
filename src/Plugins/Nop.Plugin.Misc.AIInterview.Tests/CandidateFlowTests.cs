using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Primitives;
using Moq;
using System.Net;
using System.Net.Http;
using System.Threading;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class CandidateFlowTests
{
    private Mock<IApplicationService> _applicationService;
    private Mock<IInterviewSessionService> _sessionService;
    private AIInterviewSettings _settings;
    private Mock<IWorkContext> _workContext;
    private Mock<INotificationService> _notificationService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<IDownloadService> _downloadService;
    private Mock<ICustomerService> _customerService;
    private Mock<IProductService> _productService;
    private Mock<ISponsorInviteService> _inviteService;
    private Mock<ICreditService> _creditService;
    private Mock<IJobRequirementService> _jobRequirementService;
    private Mock<IJobInterviewExperienceService> _jobInterviewExperienceService;
    private Mock<IInterviewTurnService> _turnService;
    private AIInterviewController _controller;
    private MockAiInterviewController _runtimeController;

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }

    [SetUp]
    public void SetUp()
    {
        _applicationService = new Mock<IApplicationService>();
        _sessionService = new Mock<IInterviewSessionService>();
        _settings = new AIInterviewSettings { Enabled = true };
        _workContext = new Mock<IWorkContext>();
        _notificationService = new Mock<INotificationService>();
        _localizationService = new Mock<ILocalizationService>();
        _downloadService = new Mock<IDownloadService>();
        _customerService = new Mock<ICustomerService>();
        _productService = new Mock<IProductService>();
        _inviteService = new Mock<ISponsorInviteService>();
        _creditService = new Mock<ICreditService>();
        _jobRequirementService = new Mock<IJobRequirementService>();
        _jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        _turnService = new Mock<IInterviewTurnService>();
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<int>()))
            .ReturnsAsync(new JobRequirementsModel());
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>()))
            .ReturnsAsync(new JobRequirementsModel());

        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(It.IsAny<int>())).ReturnsAsync(new List<JobApplication>());
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(It.IsAny<int>())).ReturnsAsync(new List<InterviewSession>());

        _controller = new AIInterviewController(
            _applicationService.Object,
            _sessionService.Object,
            _settings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            _turnService.Object);

        _runtimeController = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object);

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key);
    }

    [Test]
    public async Task Apply_ResumeRequired_Validation_Fails()
    {
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<int>()))
            .ReturnsAsync(new JobRequirementsModel { ResumeRequired = true });
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new global::Nop.Core.Domain.Localization.Language { Id = 1 });
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(customer.Id, "Software Engineer"))
            .ReturnsAsync(new List<JobApplication>());
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<JobApplication>());

        var model = new ApplyModel { JobTitle = "Software Engineer" };
        _controller.ModelState.AddModelError("ResumeFile", "Required");
        var result = await _controller.Apply(model);

        var viewResult = (ViewResult)result;
        Assert.That(viewResult.ViewData.ModelState.ErrorCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task Apply_ResumeReuse_Path_Works()
    {
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<int>()))
            .ReturnsAsync(new JobRequirementsModel { ResumeRequired = true });
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new global::Nop.Core.Domain.Localization.Language { Id = 1 });

        var previousApps = new List<JobApplication>
        {
            new JobApplication { CustomerId = 1, JobTitle = "Old Job", ResumeDownloadId = 123, CreatedOnUtc = DateTime.UtcNow.AddDays(-1) }
        };
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(customer.Id, "Senior Dev"))
            .ReturnsAsync(new List<JobApplication>());
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(previousApps);

        var model = new ApplyModel { JobTitle = "Senior Dev" };
        _controller.ModelState.AddModelError("ResumeFile", "Required");

        var result = await _controller.Apply(model);

        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.Is<JobApplication>(a => a.ResumeDownloadId == 123)), Times.Once);
        Assert.That(result, Is.InstanceOf<RedirectToRouteResult>());
    }

    [Test]
    public async Task Runtime_Start_Uses_ResolvedDifficulty()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);
        _productService.Setup(x => x.GetProductByIdAsync(1)).ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 1, Name = "Backend Engineer" });
        _jobInterviewExperienceService.Setup(x => x.ResolveInterviewDifficultyAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>(), It.IsAny<IFormCollection>()))
            .ReturnsAsync("Hard");

        await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1, "Hard");

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.Difficulty == "Hard" && s.ProductId == 1)), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_Idempotency_Works()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var activeSession = new InterviewSession
        {
            SessionKey = "existing",
            Token = "t1",
            IsActive = true,
            CustomerId = 1,
            ProductId = 1,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession> { activeSession });

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1);
        var json = (JsonResult)result;

        var sessionKey = json.Value.GetType().GetProperty("sessionKey").GetValue(json.Value, null);
        Assert.That(sessionKey, Is.EqualTo("existing"));
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_ExpiredActiveSession_IsHealed_And_Replaced()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var staleSession = new InterviewSession
        {
            Id = 7,
            SessionKey = "stale-session",
            Token = "expired-token",
            IsActive = true,
            CustomerId = 1,
            ProductId = 1,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-2),
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-10)
        };

        InterviewSession insertedSession = null;
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession> { staleSession });
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);
        _sessionService.Setup(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(session => insertedSession = session)
            .Returns(Task.CompletedTask);
        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns("/mockaiinterview/runtime?token=generated");
        _runtimeController.Url = urlHelperMock.Object;

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1);
        var json = (JsonResult)result;
        var runtimeUrl = json.Value.GetType().GetProperty("runtimeUrl").GetValue(json.Value, null) as string;

        Assert.That(staleSession.IsActive, Is.False);
        Assert.That(staleSession.CompletedOnUtc, Is.Not.Null);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s =>
            s.Id == staleSession.Id &&
            s.IsActive == false &&
            s.CompletedOnUtc.HasValue)), Times.Once);

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>()), Times.Once);
        Assert.That(insertedSession, Is.Not.Null);
        Assert.That(insertedSession.Token, Is.Not.EqualTo(staleSession.Token));
        Assert.That(insertedSession.TokenExpiryUtc, Is.GreaterThan(DateTime.UtcNow));
        Assert.That(runtimeUrl, Is.EqualTo("/mockaiinterview/runtime?token=generated"));
    }

    [Test]
    public void ProductDetails_StartInterview_Button_Wires_Post_And_Redirect()
    {
        var viewPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "Shared", "Components", "AIInterviewProductDetails", "Default.cshtml"));
        var viewText = File.ReadAllText(viewPath);

        Assert.That(viewText, Does.Contain("data-start-interview-button=\"true\""));
        Assert.That(viewText, Does.Contain("postJson('@Url.RouteUrl(AIInterviewDefaults.MockStartRouteName)'"));
        Assert.That(viewText, Does.Contain("window.location.href = result.runtimeUrl"));
        Assert.That(viewText, Does.Contain("document.addEventListener('click'"));
    }

    [Test]
    public void ProductDetails_And_StartViews_Handle_Fetch_Errors_Safely()
    {
        var productViewPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "Shared", "Components", "AIInterviewProductDetails", "Default.cshtml"));
        var startViewPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "Start.cshtml"));

        var productViewText = File.ReadAllText(productViewPath);
        var startViewText = File.ReadAllText(startViewPath);

        Assert.That(productViewText, Does.Contain("Unable to reach the interview service. Please check your network and try again."));
        Assert.That(productViewText, Does.Contain("response.ok"));
        Assert.That(productViewText, Does.Contain("content-type"));
        Assert.That(productViewText, Does.Contain("interviewError"));
        Assert.That(productViewText, Does.Contain("Plugins.Misc.AIInterview.Runtime.Error.ExpiredLink"));
        Assert.That(startViewText, Does.Contain("Unable to reach the interview service. Please check your network and try again."));
        Assert.That(startViewText, Does.Contain("response.ok"));
        Assert.That(startViewText, Does.Contain("content-type"));
    }

    [Test]
    public void Legacy_Interview_View_Is_Removed()
    {
        var legacyViewPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "Interview.cshtml"));

        Assert.That(File.Exists(legacyViewPath), Is.False);
    }

    [Test]
    public async Task Runtime_Start_SponsorFallback_Works()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());

        var sponsorInvite = new SponsorInvite { Id = 10, SponsorId = 2, Email = "test@example.com", InviteCode = "SPONSOR123", ExpiryDateUtc = DateTime.UtcNow.AddDays(1), IsActive = true };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("SPONSOR123")).ReturnsAsync(sponsorInvite);

        // Sponsor has no credits
        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 0 });

        // Customer has credits
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1, "Medium", "SPONSOR123");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>()), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Runtime_SubmitAnswer_InvalidToken_ReturnsError()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("invalid")).ReturnsAsync((InterviewSession)null);

        var result = await _runtimeController.SubmitAnswer("invalid", "answer");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken"));
    }

    [Test]
    public async Task Runtime_SubmitAnswer_InvalidAnswer_ReturnsError()
    {
        var session = new InterviewSession { Token = "valid", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("valid")).ReturnsAsync(session);

        var result = await _runtimeController.SubmitAnswer("valid", "");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Plugins.Misc.AIInterview.Runtime.Error.InvalidAnswer"));
    }

    [Test]
    public async Task Runtime_SubmitAnswer_ExpiredSession_ReturnsError()
    {
        var session = new InterviewSession { Token = "expired", IsActive = true, TokenExpiryUtc = DateTime.UtcNow };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired")).ReturnsAsync(session);

        var result = await _runtimeController.SubmitAnswer("expired", "answer");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);

        Assert.That(error, Is.EqualTo("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken"));
    }

    [Test]
    public async Task Runtime_RefreshToken_Success()
    {
        var session = new InterviewSession { Token = "old", IsActive = true, CompletedOnUtc = null, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("old")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("old");
        var json = (JsonResult)result;

        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null);
        Assert.That(newToken, Is.Not.Null);
        Assert.That(newToken, Is.Not.EqualTo("old"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Token == newToken.ToString())), Times.Once);
    }

    [Test]
    public async Task History_Page_Loads()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession> { new InterviewSession { Id = 1, CustomerId = 1 } });

        var result = await _runtimeController.History();
        Assert.That(result, Is.InstanceOf<ViewResult>());
    }

    [Test]
    public async Task Report_UnauthorizedAccess_Redirects()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 99)).ReturnsAsync(false);

        var result = await _controller.Report(99);
        Assert.That(result, Is.InstanceOf<ChallengeResult>());
    }

    [Test]
    public async Task Report_MissingReport_HandlesGracefully()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 1)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(1)).ReturnsAsync(new InterviewSession { Id = 1, ReportData = "" });

        var result = await _controller.Report(1);
        Assert.That(result, Is.InstanceOf<RedirectToRouteResult>());
        _notificationService.Verify(x => x.ErrorNotification(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task Report_WithoutSavedRecording_LeavesRecordingUrlNull()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 3)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(3)).ReturnsAsync(new InterviewSession
        {
            Id = 3,
            CustomerId = 1,
            ProductId = 0,
            ReportData = "overall score: 70",
            QuestionScores = "[70]",
            Score = 70,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-1),
            CompletedOnUtc = DateTime.UtcNow
        });

        var result = await _controller.Report(3);

        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.RecordingUrl, Is.Null.Or.Empty);
    }

    [Test]
    public async Task Report_IncludesSavedTurns()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 2)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(2)).ReturnsAsync(new InterviewSession
        {
            Id = 2,
            CustomerId = 1,
            ProductId = 11,
            SessionKey = "session-2",
            Token = "token-2",
            ReportData = "overall score: 88",
            QuestionScores = "[88, 92]",
            Score = 90,
            RecordingUrl = "https://storage.example.com/recordings/session-2.webm",
            CreatedOnUtc = DateTime.UtcNow.AddHours(-1),
            CompletedOnUtc = DateTime.UtcNow
        });
        _productService.Setup(x => x.GetProductByIdAsync(11)).ReturnsAsync(new Product { Id = 11, Name = "Backend Engineer" });
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(2)).ReturnsAsync(new List<InterviewTurn>
        {
            new InterviewTurn
            {
                Id = 100,
                InterviewSessionId = 2,
                SequenceNumber = 1,
                QuestionText = "Q1",
                AnswerText = "A1",
                Score = 88,
                Feedback = "Good",
                AskedOnUtc = DateTime.UtcNow.AddMinutes(-30),
                AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-29)
            }
        });

        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlActionContext ctx) =>
                ctx.Action == "Recording" ? "/aiinterview/recording/2" : "/aiinterview/report/2");
        _controller.Url = urlHelperMock.Object;

        var result = await _controller.Report(2);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.Turns.Count, Is.EqualTo(1));
        Assert.That(model.Turns[0].QuestionText, Is.EqualTo("Q1"));
        Assert.That(model.Turns[0].AnswerText, Is.EqualTo("A1"));
        Assert.That(model.ParsedQuestionScores, Is.EquivalentTo(new[] { 88m, 92m }));
        Assert.That(model.RecordingUrl, Is.EqualTo("/aiinterview/recording/2"));
    }

    [Test]
    public async Task MockReport_WithoutSavedRecording_LeavesRecordingUrlNull()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 4)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(4)).ReturnsAsync(new InterviewSession
        {
            Id = 4,
            CustomerId = 1,
            ProductId = 0,
            ReportData = "overall score: 70",
            QuestionScores = "[70]",
            Score = 70,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-1),
            CompletedOnUtc = DateTime.UtcNow
        });

        var result = await _runtimeController.Report(4);

        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.RecordingUrl, Is.Null.Or.Empty);
    }

    [Test]
    public async Task Report_HandlesTurnLookupFailure_AndStillRendersSessionReport()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 18)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(18)).ReturnsAsync(new InterviewSession
        {
            Id = 18,
            CustomerId = 1,
            ProductId = 11,
            SessionKey = "session-18",
            Token = "token-18",
            ReportData = "overall score: 77",
            QuestionScores = "[77]",
            Score = 77,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-1),
            CompletedOnUtc = DateTime.UtcNow
        });
        _productService.Setup(x => x.GetProductByIdAsync(11)).ReturnsAsync(new Product { Id = 11, Name = "Backend Engineer" });
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(18)).ThrowsAsync(new InvalidOperationException("missing turn table"));

        var result = await _controller.Report(18);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.SessionId, Is.EqualTo(18));
        Assert.That(model.ReportData, Is.EqualTo("overall score: 77"));
        Assert.That(model.Turns, Is.Empty);
        Assert.That(model.ParsedQuestionScores, Is.EquivalentTo(new[] { 77m }));
    }

    [Test]
    public async Task MyApplications_IncludesRecordingAccess_ForLatestCompletedSession()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var applications = new List<JobApplication>
        {
            new JobApplication { Id = 10, ProductId = 5, JobTitle = "Test Job", CreatedOnUtc = DateTime.UtcNow.AddDays(-2) }
        };
        var sessions = new List<InterviewSession>
        {
            new InterviewSession { Id = 98, ProductId = 5, CompletedOnUtc = DateTime.UtcNow.AddDays(-1), RecordingUrl = "" },
            new InterviewSession { Id = 99, ProductId = 5, CompletedOnUtc = DateTime.UtcNow, RecordingUrl = "https://storage.example.com/recordings/session-99.webm" }
        };

        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(applications);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(sessions);

        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlActionContext ctx) =>
                ctx.Action == "Recording" ? "/aiinterview/recording/99" : "/aiinterview/report/99");
        _controller.Url = urlHelperMock.Object;

        var result = await _controller.MyApplications("LatestApplied");

        var viewResult = (ViewResult)result;
        var model = (ApplicationListModel)viewResult.Model;
        Assert.That(model.Applications.Single().RecordingUrl, Is.Not.Null.And.Contain("/aiinterview/recording/99"));
    }

    [Test]
    public async Task RecordingRoute_AllowsOwner_AndRejectsUnauthorizedUser()
    {
        _settings.AzureBlobStorageContainerUrl = "https://storage.example.com/recordings";
        _settings.AzureBlobStorageSasToken = "?sig=token";
        var allowedCustomer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(allowedCustomer);

        _sessionService.Setup(x => x.CanAccessReportAsync(allowedCustomer.Id, 55)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(55)).ReturnsAsync(new InterviewSession
        {
            Id = 55,
            CustomerId = 1,
            RecordingUrl = "https://storage.example.com/recordings/session-55.webm"
        });

        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("recording-bytes")))
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

        var controller = new AIInterviewController(
            _applicationService.Object,
            _sessionService.Object,
            _settings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            _turnService.Object,
            factory.Object);

        var result = await controller.Recording(55);
        Assert.That(result, Is.TypeOf<FileStreamResult>());

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 2 });
        _sessionService.Setup(x => x.CanAccessReportAsync(2, 55)).ReturnsAsync(false);
        var unauthorized = await controller.Recording(55);
        Assert.That(unauthorized, Is.TypeOf<ChallengeResult>());
    }

    [Test]
    public async Task RecordingRoute_RejectsUrls_OutsideConfiguredContainer()
    {
        _settings.AzureBlobStorageContainerUrl = "https://storage.example.com/recordings";
        _settings.AzureBlobStorageSasToken = "?sig=token";
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 56)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(56)).ReturnsAsync(new InterviewSession
        {
            Id = 56,
            CustomerId = 1,
            RecordingUrl = "https://evil.example.com/recordings/session-56.webm"
        });
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 57)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(57)).ReturnsAsync(new InterviewSession
        {
            Id = 57,
            CustomerId = 1,
            RecordingUrl = "https://storage.example.com/other/session-57.webm?sig=old"
        });

        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("recording-bytes")))
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

        var controller = new AIInterviewController(
            _applicationService.Object,
            _sessionService.Object,
            _settings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            _turnService.Object,
            factory.Object);

        var outsideContainer = await controller.Recording(56);
        Assert.That(outsideContainer, Is.TypeOf<NotFoundResult>());

        var queryBearing = await controller.Recording(57);
        Assert.That(queryBearing, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public void ReportAndHistoryViews_IncludeRecordingAccessMarkup()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "Report.cshtml"));
        var mockReportPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "Report.cshtml"));
        var historyPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "History.cshtml"));

        var reportText = File.ReadAllText(reportPath);
        var mockReportText = File.ReadAllText(mockReportPath);
        var historyText = File.ReadAllText(historyPath);

        Assert.That(reportText, Does.Contain("Recording"));
        Assert.That(mockReportText, Does.Contain("Recording"));
        Assert.That(historyText, Does.Contain("Open recording"));
        Assert.That(historyText, Does.Contain("Open report"));
    }

    [Test]
    public async Task MyApplications_ReportLink_IsCorrectlyGenerated()
    {
        // Arrange
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var applications = new List<JobApplication>
        {
            new JobApplication { Id = 10, ProductId = 5, JobTitle = "Test Job" }
        };
        var sessions = new List<InterviewSession>
        {
            new InterviewSession { Id = 99, ProductId = 5, CompletedOnUtc = DateTime.UtcNow, Score = 85 }
        };

        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(applications);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(sessions);

        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>())).Returns("dummy-url");
        _controller.Url = urlHelperMock.Object;

        // Act
        var result = await _controller.MyApplications("LatestApplied");

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (ApplicationListModel)viewResult.Model;
        var firstApp = model.Applications.First();

        Assert.That(firstApp.Id, Is.EqualTo(10)); // Id remains application ID
        Assert.That(firstApp.InterviewReportUrl, Is.EqualTo("dummy-url")); // Report URL is populated properly
    }

    [Test]
    public async Task Interview_LegacyAction_Redirects_To_Runtime()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionBySessionKeyAsync("legacy-key")).ReturnsAsync(new InterviewSession
        {
            Id = 20,
            CustomerId = 1,
            Token = "runtime-token",
            SessionKey = "legacy-key"
        });

        var result = await _controller.Interview("legacy-key");

        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        var redirect = (RedirectToRouteResult)result;
        Assert.That(redirect.RouteName, Is.EqualTo(AIInterviewDefaults.MockRuntimeRouteName));
        Assert.That(redirect.RouteValues["token"], Is.EqualTo("runtime-token"));
    }

    [Test]
    public void RuntimeAndReportViews_UseSafeTextRendering()
    {
        var runtimePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "Runtime.cshtml"));
        var mockReportPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "Report.cshtml"));
        var reportPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "..",
            "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "Report.cshtml"));

        var runtimeText = System.IO.File.ReadAllText(runtimePath);
        var mockReportText = System.IO.File.ReadAllText(mockReportPath);
        var reportText = System.IO.File.ReadAllText(reportPath);

        Assert.That(runtimeText, Does.Contain("textContent"));
        Assert.That(runtimeText, Does.Not.Contain("card.innerHTML ="));
        Assert.That(runtimeText, Does.Contain("Unable to reach the interview service. Please check your network and try again."));
        Assert.That(runtimeText, Does.Contain("requestRecordingMediaForStart"));
        Assert.That(runtimeText, Does.Contain("await setCamera(true, true);"));
        Assert.That(runtimeText, Does.Contain("await setMic(true, true);"));
        Assert.That(runtimeText, Does.Contain("Recording ready."));
        Assert.That(runtimeText, Does.Contain("Recording waiting for camera or mic."));
        Assert.That(runtimeText, Does.Contain("Recording live."));
        Assert.That(mockReportText, Does.Not.Contain("Html.Raw(Model.ReportData)"));
        Assert.That(reportText, Does.Not.Contain("Html.Raw(Model.ReportData)"));
    }

    [Test]
    public async Task WidgetView_Rendering_Works()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        _productService.Setup(x => x.GetProductByIdAsync(99))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 99, ProductTemplateId = 7 });
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(7))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 7,
                ViewPath = AIInterviewDefaults.JobProductTemplateViewPath
            });
        var component = new Nop.Plugin.Misc.AIInterview.Components.AIInterviewProductDetailsViewComponent(
            _creditService.Object,
            _workContext.Object,
            productAttributeService.Object,
            jobInterviewExperienceService.Object,
            _productService.Object,
            productTemplateService.Object,
            _applicationService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/buy-credits" },
            _jobRequirementService.Object,
            _inviteService.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?sponsorToken=abc");

        var viewEngineMock = new Mock<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>();
        var requestServicesMock = new Mock<IServiceProvider>();
        requestServicesMock.Setup(s => s.GetService(typeof(ISponsorInviteService))).Returns(_inviteService.Object);
        requestServicesMock.Setup(s => s.GetService(typeof(Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine))).Returns(viewEngineMock.Object);
        httpContext.RequestServices = requestServicesMock.Object;

        var actionContext = new ActionContext();
        actionContext.HttpContext = httpContext;

        var viewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext();
        viewContext.HttpContext = httpContext;

        var viewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext();
        viewComponentContext.ViewContext = viewContext;

        component.ViewComponentContext = viewComponentContext;

        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(customer.Id)).ReturnsAsync(new CreditWallet { Balance = 10 });

        var sponsorInvite = new SponsorInvite { Id = 10, SponsorId = 2, Email = "test@example.com", InviteCode = "abc", ExpiryDateUtc = DateTime.UtcNow.AddDays(1), IsActive = true };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("abc")).ReturnsAsync(sponsorInvite);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 5 });

        // Act
        // Mock a Nop base model dynamic
        var productDetailsModel = new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 99 };
        productDetailsModel.ProductAttributes.Add(new Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeModel
        {
            Id = 14,
            Name = AIInterviewDefaults.InterviewDifficultyAttributeName,
            TextPrompt = AIInterviewDefaults.InterviewDifficultyAttributeName,
            AttributeControlType = Nop.Core.Domain.Catalog.AttributeControlType.RadioList,
            Values = new List<Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeValueModel>
            {
                new() { Id = 101, Name = "Easy" },
                new() { Id = 102, Name = "Medium", IsPreSelected = true }
            }
        });
        var result = await component.InvokeAsync("productdetails_before_collateral", productDetailsModel);

        // Assert
        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        var viewResult = (Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult)result;
        Assert.That(viewResult.ViewName, Is.EqualTo("~/Plugins/Misc.AIInterview/Views/Shared/Components/AIInterviewProductDetails/Default.cshtml"));

        // Assert viewbags
        Assert.That(component.ViewBag.HasSponsorCredits, Is.True);
        Assert.That(component.ViewBag.ProductId, Is.EqualTo(99));
        Assert.That(component.ViewBag.SponsorToken, Is.EqualTo("abc"));
        Assert.That(component.ViewBag.CreditPurchasePageUrl, Is.EqualTo("/buy-credits"));
    }

    [Test]
    public async Task WidgetView_DoesNotRenderForOrdinaryProductTemplate()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        _productService.Setup(x => x.GetProductByIdAsync(99))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 99, ProductTemplateId = 1 });
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(1))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 1,
                ViewPath = "ProductTemplate.Simple"
            });
        var component = new Nop.Plugin.Misc.AIInterview.Components.AIInterviewProductDetailsViewComponent(
            _creditService.Object,
            _workContext.Object,
            productAttributeService.Object,
            jobInterviewExperienceService.Object,
            _productService.Object,
            productTemplateService.Object,
            _applicationService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/pricing" },
            _jobRequirementService.Object,
            _inviteService.Object);

        var result = await component.InvokeAsync(
            "productdetails_before_collateral",
            new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 99 });

        Assert.That(result.GetType().Name, Is.EqualTo("ContentViewComponentResult"));
    }
}
