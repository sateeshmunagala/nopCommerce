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
using Nop.Web.Framework.Mvc.Routing;
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
    public async Task Runtime_Start_ExpiredActiveSession_IsHealed_And_Reused()
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

        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession> { staleSession });
        _sessionService.Setup(x => x.GetSessionByTokenAsync(staleSession.Token))
            .ReturnsAsync(staleSession);
        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns("/mockaiinterview/runtime?token=generated");
        _runtimeController.Url = urlHelperMock.Object;

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1);
        var json = (JsonResult)result;
        var runtimeUrl = json.Value.GetType().GetProperty("runtimeUrl").GetValue(json.Value, null) as string;
        var token = json.Value.GetType().GetProperty("token").GetValue(json.Value, null) as string;

        Assert.That(staleSession.IsActive, Is.True);
        Assert.That(staleSession.CompletedOnUtc, Is.Null);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s =>
            s.Id == staleSession.Id &&
            s.IsActive &&
            !s.CompletedOnUtc.HasValue &&
            s.Token != "expired-token" &&
            s.TokenExpiryUtc > DateTime.UtcNow)), Times.Once);

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        Assert.That(token, Is.Not.EqualTo("expired-token"));
        Assert.That(runtimeUrl, Is.EqualTo("/mockaiinterview/runtime?token=generated"));
    }

    [Test]
    public async Task Apply_With_ProductId_Redirects_To_Generic_Product_Url()
    {
        var product = new Product { Id = 9, Name = "Net Developer" };
        var nopUrlHelper = new Mock<INopUrlHelper>();
        nopUrlHelper.Setup(x => x.RouteGenericUrlAsync(product, null, null, null))
            .ReturnsAsync("/jobs/net-developer");
        _productService.Setup(x => x.GetProductByIdAsync(9)).ReturnsAsync(product);

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
            null,
            nopUrlHelper.Object);

        var result = await controller.Apply("Net Developer", 9);

        Assert.That(result, Is.TypeOf<RedirectResult>());
        Assert.That(((RedirectResult)result).Url, Is.EqualTo("/jobs/net-developer?jobTitle=Net%20Developer"));
        nopUrlHelper.Verify(x => x.RouteGenericUrlAsync(product, null, null, null), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_Get_Redirects_To_Generic_Product_Url_And_Preserves_SponsorToken()
    {
        var product = new Product { Id = 5, Name = "AI Developer" };
        var nopUrlHelper = new Mock<INopUrlHelper>();
        nopUrlHelper.Setup(x => x.RouteGenericUrlAsync(product, null, null, null))
            .ReturnsAsync("/jobs/ai-developer");
        _productService.Setup(x => x.GetProductByIdAsync(5)).ReturnsAsync(product);

        var controller = new MockAiInterviewController(
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
            _jobInterviewExperienceService.Object,
            null,
            null,
            null,
            _jobRequirementService.Object,
            null,
            null,
            nopUrlHelper.Object);

        var result = await controller.Start(5, "invite-token");

        Assert.That(result, Is.TypeOf<RedirectResult>());
        Assert.That(((RedirectResult)result).Url, Is.EqualTo("/jobs/ai-developer?sponsorToken=invite-token"));
        nopUrlHelper.Verify(x => x.RouteGenericUrlAsync(product, null, null, null), Times.Once);
    }

    [Test]
    public void ProductDetails_StartInterview_Button_Wires_Post_And_Redirect()
    {
        var viewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "Components", "AIInterviewProductDetails", "Default.cshtml"));

        Assert.That(viewText, Does.Contain("data-start-interview-button=\"true\""));
        Assert.That(viewText, Does.Contain("postJson('@Url.RouteUrl(AIInterviewDefaults.MockStartRouteName)'"));
        Assert.That(viewText, Does.Contain("window.location.href = result.runtimeUrl"));
        Assert.That(viewText, Does.Contain("document.addEventListener('click'"));
        Assert.That(viewText, Does.Not.Contain("aiinterview-server-fallback-shell"));
    }

    [Test]
    public void ProductDetails_And_StartViews_Handle_Fetch_Errors_Safely()
    {
        var productViewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "Components", "AIInterviewProductDetails", "Default.cshtml"));
        var startViewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Start.cshtml"));

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
        Assert.That(File.Exists(TestFilePathHelper.GetPluginFilePath("Views", "Interview.cshtml")), Is.False);
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
    public async Task Runtime_Start_ProductMismatchSponsoredInvite_FallsBack_To_CandidateCharge()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());

        var sponsorInvite = new SponsorInvite
        {
            Id = 13,
            SponsorId = 2,
            ProductId = 99,
            Email = "test@example.com",
            InviteCode = "MISMATCH123",
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
            IsActive = true,
            MaxAttempts = 2
        };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("MISMATCH123")).ReturnsAsync(sponsorInvite);

        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1, "Medium", "MISMATCH123");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>()), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_InactiveSponsorInvite_FallsBack_To_CandidateCharge()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());

        var sponsorInvite = new SponsorInvite
        {
            Id = 11,
            SponsorId = 2,
            ProductId = 1,
            Email = "test@example.com",
            InviteCode = "INACTIVE123",
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
            IsActive = false
        };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("INACTIVE123")).ReturnsAsync(sponsorInvite);

        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1, "Medium", "INACTIVE123");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>()), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_ExhaustedSponsorInvite_FallsBack_To_CandidateCharge()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var sponsorInvite = new SponsorInvite
        {
            Id = 12,
            SponsorId = 2,
            ProductId = 1,
            Email = "test@example.com",
            InviteCode = "EXHAUSTED123",
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
            IsActive = true,
            MaxAttempts = 2
        };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("EXHAUSTED123")).ReturnsAsync(sponsorInvite);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(12)).ReturnsAsync(2);

        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1, "Medium", "EXHAUSTED123");
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
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var message = json.Value.GetType().GetProperty("message").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Invalid or expired session token."));
        Assert.That(error, Is.EqualTo("Invalid or expired session token."));
    }

    [Test]
    public async Task Runtime_SubmitAnswer_InvalidAnswer_ReturnsError()
    {
        var session = new InterviewSession { Token = "valid", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("valid")).ReturnsAsync(session);

        var result = await _runtimeController.SubmitAnswer("valid", "");
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var message = json.Value.GetType().GetProperty("message").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Answer cannot be empty."));
        Assert.That(error, Is.EqualTo("Answer cannot be empty."));
    }

    [Test]
    public async Task Runtime_SubmitAnswer_ExpiredSession_ReturnsError()
    {
        var session = new InterviewSession { Token = "expired", IsActive = true, TokenExpiryUtc = DateTime.UtcNow };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired")).ReturnsAsync(session);

        var result = await _runtimeController.SubmitAnswer("expired", "answer");
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var message = json.Value.GetType().GetProperty("message").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Invalid or expired session token."));
        Assert.That(error, Is.EqualTo("Invalid or expired session token."));
    }

    [Test]
    public async Task Runtime_RefreshToken_Success()
    {
        var session = new InterviewSession { Token = "old", IsActive = true, CompletedOnUtc = null, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("old")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("old");
        var json = (JsonResult)result;

        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null);
        Assert.That(success, Is.EqualTo(true));
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
                RubricJson = "{\"technicalScore\":91,\"communicationScore\":86,\"professionalismScore\":84,\"positiveAttitudeScore\":92,\"score\":88}",
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
        Assert.That(model.Turns[0].TechnicalScore, Is.EqualTo(91));
        Assert.That(model.Turns[0].CommunicationScore, Is.EqualTo(86));
        Assert.That(model.Turns[0].ProfessionalismScore, Is.EqualTo(84));
        Assert.That(model.Turns[0].PositiveAttitudeScore, Is.EqualTo(92));
        Assert.That(model.ParsedQuestionScores, Is.EquivalentTo(new[] { 88m, 92m }));
        Assert.That(model.RecordingUrl, Is.EqualTo("/aiinterview/recording/2"));
    }

    [Test]
    public async Task Report_OldTurnWithoutRubric_LeavesCategoryScoresNull()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 12)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(12)).ReturnsAsync(new InterviewSession
        {
            Id = 12,
            CustomerId = 1,
            ProductId = 11,
            ReportData = "overall score: 75",
            QuestionScores = "[75]",
            Score = 75,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-1),
            CompletedOnUtc = DateTime.UtcNow
        });
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(12)).ReturnsAsync(new List<InterviewTurn>
        {
            new InterviewTurn
            {
                Id = 101,
                InterviewSessionId = 12,
                SequenceNumber = 1,
                QuestionText = "Legacy Q1",
                AnswerText = "Legacy A1",
                Score = 75,
                Feedback = "Legacy feedback"
            }
        });

        var result = await _controller.Report(12);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.Turns, Has.Count.EqualTo(1));
        Assert.That(model.Turns[0].TechnicalScore, Is.Null);
        Assert.That(model.Turns[0].CommunicationScore, Is.Null);
        Assert.That(model.Turns[0].ProfessionalismScore, Is.Null);
        Assert.That(model.Turns[0].PositiveAttitudeScore, Is.Null);
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
        _sessionService.Setup(x => x.EnsureRecordingShareTokenAsync(It.IsAny<InterviewSession>())).ReturnsAsync("share-token-99");

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
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlActionContext ctx) => ctx.Action switch
            {
                "Recording" => "/aiinterview/recording/99",
                "ReportPanel" => "/aiinterview/report-panel/99",
                _ => "/aiinterview/report/99"
            });
        urlHelperMock.Setup(u => u.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns("/aiinterview/recording/share/share-token-99");
        _controller.Url = urlHelperMock.Object;

        var result = await _controller.MyApplications("LatestApplied");

        var viewResult = (ViewResult)result;
        var model = (ApplicationListModel)viewResult.Model;
        Assert.That(model.Applications.Single().RecordingUrl, Is.Not.Null.And.Contain("/aiinterview/recording/99"));
        Assert.That(model.Applications.Single().RecordingShareUrl, Is.EqualTo("/aiinterview/recording/share/share-token-99"));
        Assert.That(model.Applications.Single().RecordingShareUrl, Does.Not.EndWith("/99"));
        Assert.That(model.Applications.Single().RecordingShareUrl, Does.Not.Contain("storage.example.com"));
        Assert.That(model.Applications.Single().RecordingShareUrl, Does.Not.Contain("sig="));
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
        var reportText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_InterviewReportContent.cshtml"));
        var drawerText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_CandidateReportDrawer.cshtml"));
        var historyText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "History.cshtml"));
        var myApplicationsText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MyApplications.cshtml"));

        Assert.That(reportText, Does.Contain("Plugins.Misc.AIInterview.Report.Recording"));
        Assert.That(historyText, Does.Contain("Plugins.Misc.AIInterview.Report.OpenRecording"));
        Assert.That(historyText, Does.Contain("Plugins.Misc.AIInterview.Report.ViewReport"));
        Assert.That(historyText, Does.Contain("ai-view-report-link"));
        Assert.That(historyText, Does.Contain("fa fa-eye"));
        Assert.That(historyText, Does.Contain("ai-copy-share-link"));
        Assert.That(myApplicationsText, Does.Contain("js-open-report-drawer"));
        Assert.That(drawerText, Does.Contain("ai-report-drawer"));
        Assert.That(drawerText, Does.Contain("data-report-drawer-close"));
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
        _sessionService.Setup(x => x.EnsureRecordingShareTokenAsync(It.IsAny<InterviewSession>())).ReturnsAsync((string)null);

        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlActionContext ctx) => ctx.Action == "ReportPanel" ? "panel-url" : "dummy-url");
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
        Assert.That(firstApp.InterviewReportPanelUrl, Is.EqualTo("panel-url"));
    }

    [Test]
    public async Task RecordingShareRoute_InvalidToken_ReturnsNotFound()
    {
        _sessionService.Setup(x => x.GetSessionByRecordingShareTokenAsync("missing-token")).ReturnsAsync((InterviewSession)null);

        var result = await _controller.RecordingShare("missing-token");

        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task MyApplications_Uses_Actual_Turn_Data_For_Assessment_Preview()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var applications = new List<JobApplication>
        {
            new JobApplication { Id = 10, ProductId = 5, JobTitle = "Test Job" }
        };
        var sessions = new List<InterviewSession>
        {
            new InterviewSession { Id = 99, ProductId = 5, CompletedOnUtc = DateTime.UtcNow, Score = 85, QuestionScores = "[85]" }
        };
        var turns = new List<InterviewTurn>
        {
            new InterviewTurn
            {
                Id = 1,
                InterviewSessionId = 99,
                SequenceNumber = 1,
                QuestionText = "What is dependency injection?",
                AnswerText = "It removes hard coupling.",
                Score = 85,
                Feedback = "Strong answer",
                AskedOnUtc = DateTime.UtcNow.AddMinutes(-2),
                AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-1)
            }
        };

        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(applications);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(sessions);
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(99)).ReturnsAsync(turns);

        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>())).Returns("dummy-url");
        _controller.Url = urlHelperMock.Object;

        var result = await _controller.MyApplications("LatestApplied");

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (ApplicationListModel)viewResult.Model;
        var firstApp = model.Applications.First();

        Assert.That(firstApp.Turns, Has.Count.EqualTo(1));
        Assert.That(firstApp.Turns[0].SequenceNumber, Is.EqualTo(1));
        Assert.That(firstApp.Turns[0].QuestionText, Is.EqualTo("What is dependency injection?"));
        Assert.That(firstApp.Turns[0].Score, Is.EqualTo(85));
        Assert.That(firstApp.Turns[0].Feedback, Is.EqualTo("Strong answer"));
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
        var runtimeText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));
        var mockReportText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Report.cshtml"));
        var reportText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Report.cshtml"));
        var reportContentText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_InterviewReportContent.cshtml"));
        var drawerText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_CandidateReportDrawer.cshtml"));

        Assert.That(runtimeText, Does.Contain("textContent"));
        Assert.That(runtimeText, Does.Not.Contain("card.innerHTML ="));
        Assert.That(runtimeText, Does.Contain("Unable to reach the interview service. Please check your network and try again."));
        Assert.That(runtimeText, Does.Not.Contain("console.log(\"Settings Config\")"));
        Assert.That(runtimeText, Does.Not.Contain("console.log(config)"));
        Assert.That(runtimeText, Does.Not.Contain("Body: ${text}"));
        Assert.That(runtimeText, Does.Contain("requestRecordingMediaForStart"));
        Assert.That(runtimeText, Does.Contain("await setCamera(true, true);"));
        Assert.That(runtimeText, Does.Contain("await setMic(true, true);"));
        Assert.That(runtimeText, Does.Contain("refreshAt <= 0"));
        Assert.That(runtimeText, Does.Contain("tokenRefreshPromise"));
        Assert.That(runtimeText, Does.Contain("if (tokenRefreshPromise)"));
        Assert.That(runtimeText, Does.Not.Contain("tokenRefreshInFlight"));
        Assert.That(runtimeText, Does.Contain("showUnavailableQuestionState"));
        Assert.That(runtimeText, Does.Contain("const beginResult = await postForm(config.beginInterviewUrl"));
        Assert.That(runtimeText, Does.Contain("let interviewUnavailable = false;"));
        Assert.That(runtimeText, Does.Contain("normalized === 'AI service unavailable. Please try again later.'"));
        Assert.That(runtimeText, Does.Contain("const hasActiveQuestion = () => !interviewUnavailable && !isPlaceholderSpeechText(currentQuestionText());"));
        Assert.That(runtimeText, Does.Contain("const disableSubmit = answerBox.disabled"));
        Assert.That(runtimeText, Does.Contain("|| !interviewStarted"));
        Assert.That(runtimeText, Does.Contain("|| runtimeStoppedOrCompleted"));
        Assert.That(runtimeText, Does.Contain("|| stopInProgress"));
        Assert.That(runtimeText, Does.Contain("|| !isCameraActive()"));
        Assert.That(runtimeText, Does.Contain("|| !isMicActive()"));
        Assert.That(runtimeText, Does.Contain("|| !hasActiveQuestion()"));
        Assert.That(runtimeText, Does.Contain("|| isScreenShareBlockingInterview()"));
        Assert.That(runtimeText, Does.Contain("interviewUnavailable = true;"));
        Assert.That(runtimeText, Does.Contain("const autoSubmitDelaySeconds = 10;"));
        Assert.That(runtimeText, Does.Contain("const clearAnswerTimers = () =>"));
        Assert.That(runtimeText, Does.Contain("const clearTokenRefreshTimer = () =>"));
        Assert.That(runtimeText, Does.Contain("const clearAllRuntimeTimers = () =>"));
        Assert.That(runtimeText, Does.Contain("id=\"screen-share-status\""));
        Assert.That(runtimeText, Does.Not.Contain("Plugins.Misc.AIInterview.Runtime.ScreenSharingOptional"));
        Assert.That(runtimeText, Does.Contain("Screen sharing active"));
        Assert.That(runtimeText, Does.Contain("Screen sharing ended. Resume screen sharing to continue."));
        Assert.That(runtimeText, Does.Contain("Screen sharing resumed"));
        Assert.That(runtimeText, Does.Contain("let screenShareRequired = true;"));
        Assert.That(runtimeText, Does.Contain("const ensureRequiredMediaReady = async () =>"));
        Assert.That(runtimeText, Does.Contain("const shouldWarnBeforeUnload = () => interviewStarted && !runtimeStoppedOrCompleted && !stopInProgress;"));
        Assert.That(runtimeText, Does.Contain("window.addEventListener('beforeunload', (event) => {"));
        Assert.That(runtimeText, Does.Contain("if (!shouldWarnBeforeUnload())"));
        Assert.That(runtimeText, Does.Contain("runtime-log-panel"));
        Assert.That(runtimeText, Does.Contain("if (!trimmedAnswer)"));
        Assert.That(runtimeText, Does.Contain("Auto submitting..."));
        Assert.That(runtimeText, Does.Contain("Submit Answer (${countdownValue})"));
        Assert.That(runtimeText, Does.Not.Contain("answerStageTimer = setTimeout(() => {"));
        Assert.That(runtimeText, Does.Not.Contain("}, autoSubmitDelaySeconds * 1000);"));
        Assert.That(runtimeText, Does.Contain("fa-solid fa-robot"));
        Assert.That(runtimeText, Does.Contain("fa-solid fa-user"));
        Assert.That(runtimeText, Does.Contain("runtime-chat-message"));
        Assert.That(runtimeText, Does.Contain("runtime-chat-avatar"));
        Assert.That(runtimeText, Does.Contain("id=\"conversation\""));
        Assert.That(runtimeText, Does.Contain("toggle-screen-share"));
        Assert.That(runtimeText, Does.Contain("Please speak or type something."));
        Assert.That(runtimeText, Does.Contain("answerNeedsEditAfterFailure = true;"));
        Assert.That(runtimeText, Does.Contain("if (!isSuccess(result)) {"));
        Assert.That(runtimeText, Does.Not.Contain("answerNeedsEditAfterFailure = true;\r\n                updateSubmitAvailability();\r\n                resetTimers();"));
        Assert.That(runtimeText, Does.Contain("Speaking question."));
        Assert.That(runtimeText, Does.Contain("Speaking reminder."));
        Assert.That(runtimeText, Does.Contain("Question speech completed."));
        Assert.That(runtimeText, Does.Contain("Reminder shown."));
        Assert.That(runtimeText, Does.Contain("Reminder spoken."));
        Assert.That(runtimeText, Does.Contain("Reminder speech failed."));
        Assert.That(runtimeText, Does.Contain("if (!interviewStarted || isSpeakingOrSubmitting || !hasActiveQuestion() || answerNeedsEditAfterFailure || isScreenShareBlockingInterview())"));
        Assert.That(runtimeText, Does.Contain("startRuntimeTimer();"));
        Assert.That(runtimeText, Does.Contain("const updateStartButtonState = () =>"));
        Assert.That(runtimeText, Does.Contain("setButtonLabel(primaryActionButton, 'Submit Answer');"));
        Assert.That(runtimeText, Does.Contain("Interview Started"));
        Assert.That(runtimeText, Does.Not.Contain("startButton.textContent = 'Next Question';"));
        Assert.That(runtimeText, Does.Contain("const normalizeTurn = (turn, index = 0) =>"));
        Assert.That(runtimeText, Does.Contain("getValue(turn, 'questionText', 'QuestionText')"));
        Assert.That(runtimeText, Does.Contain("messageBox.textContent = isTerminated ? 'Interview completed. Redirecting to report...' : 'Please answer the next question.';"));
        Assert.That(runtimeText, Does.Not.Contain("messageBox.textContent = getValue(result, 'feedback', 'Feedback') || getRuntimeMessage(result, '') || '';"));
        Assert.That(runtimeText, Does.Contain("if (mediaRecorder && recordingEnabled)\r\n                    await stopRecording(true);").Or.Contain("if (mediaRecorder && recordingEnabled)\n                    await stopRecording(true);"));
        Assert.That(runtimeText, Does.Not.Contain("setRecordingStatus('Recording ready.', false);"));
        Assert.That(runtimeText, Does.Not.Contain("setRecordingStatus('Recording waiting for screen share, camera, or microphone.', false);"));
        Assert.That(runtimeText, Does.Contain("Recording paused until screen sharing resumes."));
        Assert.That(runtimeText, Does.Contain("let preservedRecordingSegments = [];"));
        Assert.That(runtimeText, Does.Contain("const canStartRecording = () => {"));
        Assert.That(runtimeText, Does.Contain("if (screenShareRequired && (!screenShareActive || screenShareInterrupted))"));
        Assert.That(runtimeText, Does.Contain("const segments = [...preservedRecordingSegments];"));
        Assert.That(runtimeText, Does.Contain("await stopRecording(false, { preserveSegment: true, statusMessage: 'Recording restarting with resumed screen share.' });"));
        Assert.That(runtimeText, Does.Contain("acknowledgeGuidelinesUrl"));
        Assert.That(runtimeText, Does.Contain("sendGuidelinesAcknowledgementAudit"));
        Assert.That(runtimeText, Does.Contain("Mobile phones and tablets are not allowed by policy, but they are not blocked technically."));
        Assert.That(runtimeText, Does.Contain("Recording live."));
        Assert.That(runtimeText, Does.Not.Contain("Questions and answers appear here in order."));
        Assert.That(runtimeText, Does.Contain("~/Plugins/Misc.AIInterview/Content/css/aiinterview-public.css"));
        Assert.That(runtimeText, Does.Contain("Plugins.Misc.AIInterview.Runtime.MockMode.Warning"));
        var myApplicationsText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MyApplications.cshtml"));
        Assert.That(myApplicationsText, Does.Not.Contain("Q1 Relevancy"));
        Assert.That(myApplicationsText, Does.Not.Contain("Q1 Correctness"));
        Assert.That(myApplicationsText, Does.Not.Contain("Q1 Answer Score"));
        Assert.That(myApplicationsText, Does.Contain("js-open-report-drawer"));
        Assert.That(mockReportText, Does.Contain("_InterviewReportContent.cshtml"));
        Assert.That(reportText, Does.Contain("_InterviewReportContent.cshtml"));
        Assert.That(reportContentText, Does.Not.Contain("Html.Raw(Model.ReportData)"));
        Assert.That(reportContentText, Does.Not.Contain("Q@turn.SequenceNumber"));
        Assert.That(reportContentText, Does.Contain("Q@(turn.SequenceNumber)"));
        Assert.That(reportContentText, Does.Contain("Plugins.Misc.AIInterview.Report.TechnicalScore"));
        Assert.That(reportContentText, Does.Contain("Plugins.Misc.AIInterview.Report.Communication"));
        Assert.That(reportContentText, Does.Contain("Plugins.Misc.AIInterview.Report.Professionalism"));
        Assert.That(reportContentText, Does.Contain("Plugins.Misc.AIInterview.Report.PositiveAttitude"));
        Assert.That(reportContentText, Does.Contain("ai-copy-share-link"));
        Assert.That(drawerText, Does.Contain("navigator.share"));
        Assert.That(drawerText, Does.Contain("Escape"));
        Assert.That(reportContentText, Does.Not.Contain(">Technical Score<"));
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
            _sessionService.Object,
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
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());

        var sponsorInvite = new SponsorInvite { Id = 10, SponsorId = 2, ProductId = 99, Email = "test@example.com", InviteCode = "abc", ExpiryDateUtc = DateTime.UtcNow.AddDays(1), IsActive = true, MaxAttempts = 1 };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("abc")).ReturnsAsync(sponsorInvite);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 5 });
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(10)).ReturnsAsync(0);

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
    public async Task WidgetView_DoesNotShowSponsorCredits_WhenInviteInactive()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        _productService.Setup(x => x.GetProductByIdAsync(100))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 100, ProductTemplateId = 7 });
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
            _sessionService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/buy-credits" },
            _jobRequirementService.Object,
            _inviteService.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?sponsorToken=inactive-token");
        var viewEngineMock = new Mock<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>();
        var requestServicesMock = new Mock<IServiceProvider>();
        requestServicesMock.Setup(s => s.GetService(typeof(ISponsorInviteService))).Returns(_inviteService.Object);
        requestServicesMock.Setup(s => s.GetService(typeof(Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine))).Returns(viewEngineMock.Object);
        httpContext.RequestServices = requestServicesMock.Object;

        component.ViewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext
        {
            ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext { HttpContext = httpContext }
        };

        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(customer.Id)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("inactive-token"))
            .ReturnsAsync(new SponsorInvite
            {
                Id = 15,
                SponsorId = 2,
                Email = "test@example.com",
                InviteCode = "inactive-token",
                ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
                IsActive = false
            });

        var productDetailsModel = new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 100 };
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

        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        Assert.That(component.ViewBag.HasSponsorCredits, Is.False);
    }

    [Test]
    public async Task WidgetView_DoesNotShowSponsorCredits_WhenInviteAttemptsAreExhausted()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        _productService.Setup(x => x.GetProductByIdAsync(101))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 101, ProductTemplateId = 7 });
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
            _sessionService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/buy-credits" },
            _jobRequirementService.Object,
            _inviteService.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?sponsorToken=exhausted-token");
        var viewEngineMock = new Mock<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>();
        var requestServicesMock = new Mock<IServiceProvider>();
        requestServicesMock.Setup(s => s.GetService(typeof(ISponsorInviteService))).Returns(_inviteService.Object);
        requestServicesMock.Setup(s => s.GetService(typeof(Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine))).Returns(viewEngineMock.Object);
        httpContext.RequestServices = requestServicesMock.Object;

        component.ViewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext
        {
            ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext { HttpContext = httpContext }
        };

        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(customer.Id)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(16)).ReturnsAsync(2);
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("exhausted-token"))
            .ReturnsAsync(new SponsorInvite
            {
                Id = 16,
                SponsorId = 2,
                ProductId = 101,
                Email = "test@example.com",
                InviteCode = "exhausted-token",
                ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
                IsActive = true,
                MaxAttempts = 2
            });

        var productDetailsModel = new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 101 };
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

        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        Assert.That(component.ViewBag.HasSponsorCredits, Is.False);
    }

    [Test]
    public async Task WidgetView_DoesNotShowSponsorCredits_WhenInviteIsForDifferentProduct()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        _productService.Setup(x => x.GetProductByIdAsync(102))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 102, ProductTemplateId = 7 });
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
            _sessionService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/buy-credits" },
            _jobRequirementService.Object,
            _inviteService.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?sponsorToken=mismatch-token");
        var viewEngineMock = new Mock<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>();
        var requestServicesMock = new Mock<IServiceProvider>();
        requestServicesMock.Setup(s => s.GetService(typeof(ISponsorInviteService))).Returns(_inviteService.Object);
        requestServicesMock.Setup(s => s.GetService(typeof(Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine))).Returns(viewEngineMock.Object);
        httpContext.RequestServices = requestServicesMock.Object;

        component.ViewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext
        {
            ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext { HttpContext = httpContext }
        };

        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(customer.Id)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("mismatch-token"))
            .ReturnsAsync(new SponsorInvite
            {
                Id = 17,
                SponsorId = 2,
                ProductId = 999,
                Email = "test@example.com",
                InviteCode = "mismatch-token",
                ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
                IsActive = true,
                MaxAttempts = 2
            });
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(17)).ReturnsAsync(0);

        var productDetailsModel = new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 102 };
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

        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        Assert.That(component.ViewBag.HasSponsorCredits, Is.False);
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
            _sessionService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/pricing" },
            _jobRequirementService.Object,
            _inviteService.Object);

        var result = await component.InvokeAsync(
            "productdetails_before_collateral",
            new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 99 });

        Assert.That(result.GetType().Name, Is.EqualTo("ContentViewComponentResult"));
    }
}
