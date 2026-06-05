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

        var result = await _runtimeController.StartPost(1, "Hard");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.Difficulty == "Hard" && s.ProductId == 1)), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_Idempotency_Works()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var activeSession = new InterviewSession { SessionKey = "existing", Token = "t1", IsActive = true, CustomerId = 1, ProductId = 1 };
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession> { activeSession });

        var result = await _runtimeController.StartPost(1);
        var json = (JsonResult)result;

        var sessionKey = json.Value.GetType().GetProperty("sessionKey").GetValue(json.Value, null);
        Assert.That(sessionKey, Is.EqualTo("existing"));
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_SponsorFallback_Works()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());

        var sponsorInvite = new SponsorInvite { Id = 10, SponsorId = 2, Email = "test@example.com", InviteCode = "SPONSOR123", ExpiryDateUtc = DateTime.UtcNow.AddDays(1) };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("SPONSOR123")).ReturnsAsync(sponsorInvite);

        // Sponsor has no credits
        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 0 });

        // Customer has credits
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(1, "Medium", "SPONSOR123");
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
    public async Task WidgetView_Rendering_Works()
    {
        var component = new Nop.Plugin.Misc.AIInterview.Components.AIInterviewProductDetailsViewComponent(_creditService.Object, _workContext.Object);
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

        var sponsorInvite = new SponsorInvite { Id = 10, SponsorId = 2, Email = "test@example.com", InviteCode = "abc", ExpiryDateUtc = DateTime.UtcNow.AddDays(1) };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("abc")).ReturnsAsync(sponsorInvite);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 5 });

        // Act
        // Mock a Nop base model dynamic
        var productDetailsModel = new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 99 };
        var result = await component.InvokeAsync("productdetails_before_collateral", productDetailsModel);

        // Assert
        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        var viewResult = (Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult)result;
        Assert.That(viewResult.ViewName, Is.EqualTo("~/Plugins/Misc.AIInterview/Views/Shared/Components/AIInterviewProductDetails/Default.cshtml"));

        // Assert viewbags
        Assert.That(component.ViewBag.HasSponsorCredits, Is.True);
        Assert.That(component.ViewBag.ProductId, Is.EqualTo(99));
        Assert.That(component.ViewBag.SponsorToken, Is.EqualTo("abc"));
    }
}
