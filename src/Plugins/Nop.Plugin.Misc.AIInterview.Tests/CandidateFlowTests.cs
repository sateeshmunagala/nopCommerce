using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core;
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
    private AIInterviewController _controller;
    private MockAiInterviewController _runtimeController;

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

        _controller = new AIInterviewController(
            _applicationService.Object,
            _sessionService.Object,
            _settings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object);

        _runtimeController = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object);

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key);
    }

    [Test]
    public async Task Apply_ResumeRequired_Validation_Fails()
    {
        _settings.ResumeRequired = true;
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
        _settings.ResumeRequired = true;
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
    public async Task Runtime_Start_DifficultySelection_Works()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost("Hard");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.Difficulty == "Hard")), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_Idempotency_Works()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var activeSession = new InterviewSession { SessionKey = "existing", Token = "t1", IsActive = true, CustomerId = 1 };
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession> { activeSession });

        var result = await _runtimeController.StartPost();
        var json = (JsonResult)result;

        var sessionKey = json.Value.GetType().GetProperty("sessionKey").GetValue(json.Value, null);
        Assert.That(sessionKey, Is.EqualTo("existing"));
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
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
    public async Task Runtime_RefreshToken_Success()
    {
        var session = new InterviewSession { Token = "old", IsActive = true };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("old")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("old");
        var json = (JsonResult)result;

        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null);
        Assert.That(newToken, Is.Not.Null);
        Assert.That(newToken, Is.Not.EqualTo("old"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Token == newToken.ToString())), Times.Once);
    }

    [Test]
    public async Task Runtime_RefreshToken_ServiceFailure_ReturnsError()
    {
        var session = new InterviewSession { Token = "fail-me", IsActive = true };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("fail-me")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("fail-me");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Plugins.Misc.AIInterview.Runtime.Error.TokenServiceFailure"));
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
}
