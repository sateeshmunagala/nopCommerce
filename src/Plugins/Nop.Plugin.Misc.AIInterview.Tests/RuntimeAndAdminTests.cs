using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Plugin.Misc.AIInterview.Events;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class RuntimeAndAdminTests
{
    private Mock<IInterviewSessionService> _sessionService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<IWorkContext> _workContext;
    private Mock<ICustomerService> _customerService;
    private Mock<Nop.Core.Events.IEventPublisher> _eventPublisher;
    private MockAiInterviewController _runtimeController;

    private Mock<ICreditService> _creditService;
    private Mock<ISponsorInviteService> _inviteService;
    private Mock<INotificationService> _notificationService;
    private Mock<ISettingService> _settingService;
    private AIInterviewSettings _aiInterviewSettings;
    private MockAIInterviewSettings _mockAIInterviewSettings;
    private MockAiInterviewAdminController _adminController;
    private Mock<IInterviewRuntimeService> _interviewRuntimeService;

    private Mock<IProductService> _productService;
    private SponsorInviteService _inviteServiceImplementation;

    [SetUp]
    public void SetUp()
    {
        _sessionService = new Mock<IInterviewSessionService>();
        _localizationService = new Mock<ILocalizationService>();
        _workContext = new Mock<IWorkContext>();
        _customerService = new Mock<ICustomerService>();
        _eventPublisher = new Mock<Nop.Core.Events.IEventPublisher>();
        _creditService = new Mock<ICreditService>();
        _inviteService = new Mock<ISponsorInviteService>();
        _productService = new Mock<IProductService>();
        _runtimeController = new MockAiInterviewController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object, _eventPublisher.Object);

        _notificationService = new Mock<INotificationService>();
        _settingService = new Mock<ISettingService>();
        _aiInterviewSettings = new AIInterviewSettings();
        _mockAIInterviewSettings = new MockAIInterviewSettings();
        _interviewRuntimeService = new Mock<IInterviewRuntimeService>();
        _adminController = new MockAiInterviewAdminController(_creditService.Object, _inviteService.Object, _localizationService.Object, _notificationService.Object, _workContext.Object, _settingService.Object, _aiInterviewSettings, _mockAIInterviewSettings);

        _productService = new Mock<IProductService>();
        _inviteServiceImplementation = new SponsorInviteService(null, _productService.Object, _customerService.Object, _localizationService.Object);

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key == "Plugins.Misc.AIInterview.Missing" ? "" : key);
    }

    [Test]
    public async Task Runtime_Start_Unauthorized_ReturnsError()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync((Customer)null);
        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()));
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Unauthorized runtime request."));
    }

    [Test]
    public async Task Runtime_RefreshToken_ExpiredActiveSession_RenewsAndUpdatesToken()
    {
        var session = new InterviewSession
        {
            Id = 91,
            CustomerId = 1,
            Token = "expired-active",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-active")).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);

        var result = await _runtimeController.RefreshToken("expired-active");

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null)?.ToString();

        Assert.That(success, Is.EqualTo(true));
        Assert.That(newToken, Is.Not.Null);
        Assert.That(newToken, Is.Not.EqualTo("expired-active"));
        Assert.That(json.Value.GetType().GetProperty("tokenExpiryUtc").GetValue(json.Value, null), Is.Not.Null);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s =>
            s.Id == 91 &&
            s.Token == newToken &&
            s.IsActive &&
            s.CompletedOnUtc == null)), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_InactiveSponsorInvite_FallsBack_To_CandidateCharge()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("inactive-token")).ReturnsAsync(new SponsorInvite
        {
            Id = 44,
            SponsorId = 2,
            Email = "candidate@example.com",
            InviteCode = "inactive-token",
            IsActive = false,
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1)
        });

        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), 1, "Medium", "inactive-token");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session => session.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>()), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_ExhaustedSponsorInvite_FallsBack_To_CandidateCharge()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>
        {
            new InterviewSession { Id = 1, CustomerId = 1, ProductId = 1, SponsorInviteId = 55 },
            new InterviewSession { Id = 2, CustomerId = 1, ProductId = 1, SponsorInviteId = 55 }
        });
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("exhausted-token")).ReturnsAsync(new SponsorInvite
        {
            Id = 55,
            SponsorId = 2,
            Email = "candidate@example.com",
            InviteCode = "exhausted-token",
            IsActive = true,
            MaxAttempts = 2,
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1)
        });

        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()), 1, "Medium", "exhausted-token");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session => session.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>()), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Runtime_InvalidToken_ReturnsLocalizedError()
    {
        var result = await _runtimeController.SubmitAnswer(null, "Answer");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Invalid or expired session token."));
    }

    [Test]
    public async Task Runtime_LocalizationFallback_Works()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync((Customer)null);
        // Using a trick here by mocking the controller to use a missing resource
        var controller = new TestRuntimeController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object);
        var result = await controller.TestFallback();
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var message = json.Value.GetType().GetProperty("message").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Fallback text"));
        Assert.That(error, Is.EqualTo("Fallback text"));
    }

    [Test]
    public async Task Admin_TopUp_InvalidAmount_ReturnsError()
    {
        var result = await _adminController.TopUpCredits(1, -10);
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Invalid top-up amount."));
    }

    [Test]
    public async Task Admin_Invite_Validation_EmailRequired()
    {
        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "", 1, 1, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.EmailRequired"));
    }

    [Test]
    public async Task Admin_Invite_Validation_EmailInvalid()
    {
        var ex = Assert.ThrowsAsync<NopException>(async () =>
            await _inviteServiceImplementation.CreateInviteAsync(1, "not-an-email", 1, 1, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.EmailInvalid"));
    }

    [Test]
    public async Task Admin_Invite_Validation_InvalidOwnership()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(1)).ReturnsAsync(new Customer { Id = 1, VendorId = 1 });
        _productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Product { Id = 10, VendorId = 2 }); // Owned by vendor 2

        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "test@test.com", 10, 1, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.InvalidOwnership"));
    }

    [Test]
    public async Task Admin_Configure_SavesSettings()
    {
        _aiInterviewSettings.Enabled = true;
        _aiInterviewSettings.ApiKey = "keep";
        _aiInterviewSettings.MinimumScore = 42;
        _aiInterviewSettings.Provider = "keep";
        _aiInterviewSettings.Model = "keep";
        _aiInterviewSettings.Prompt = "keep";
        _aiInterviewSettings.ServiceSettings = "keep";

        var model = new ConfigurationModel
        {
            Enabled = false
        };

        await _adminController.Configure(model);

        _settingService.Verify(x => x.SaveSettingAsync(It.Is<AIInterviewSettings>(s =>
            s.Enabled == false &&
            s.ApiKey == "keep" &&
            s.MinimumScore == 42 &&
            s.Provider == "keep" &&
            s.Model == "keep" &&
            s.Prompt == "keep" &&
            s.ServiceSettings == "keep")), Times.Once);
    }

    [Test]
    public async Task Admin_TopUp_Successful()
    {
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Admin.TopUp.Remarks"))
            .ReturnsAsync("Admin top-up localized");

        var result = await _adminController.TopUpCredits(1, 100);
        var json = (JsonResult)result;
        var success = (bool)json.Value.GetType().GetProperty("success").GetValue(json.Value, null);

        _creditService.Verify(x => x.AddCreditAsync(1, 100, "Admin top-up localized"), Times.Once);
        Assert.That(success, Is.True);
    }

    [Test]
    public async Task Admin_Invite_Validation_ProductNotFound()
    {
        _productService.Setup(x => x.GetProductByIdAsync(It.IsAny<int>())).ReturnsAsync((Product)null);
        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "test@test.com", 999, 1, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.ProductNotFound"));
    }

    [Test]
    public async Task EmployerManage_Uses_Exhausted_Status_For_Fully_Used_Invite()
    {
        var customer = new Customer { Id = 1, VendorId = 2, Email = "vendor@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _inviteService.Setup(x => x.GetSponsorInvitesAsync(1)).ReturnsAsync(new List<SponsorInvite>
        {
            new SponsorInvite
            {
                Id = 22,
                SponsorId = 1,
                ProductId = 10,
                Email = "candidate@example.com",
                InviteCode = "INV-22",
                MaxAttempts = 2,
                IsActive = true,
                IsAccepted = true,
                ExpiryDateUtc = DateTime.UtcNow.AddDays(1)
            }
        });
        _creditService.Setup(x => x.GetOrCreateWalletAsync(1)).ReturnsAsync(new CreditWallet { Balance = 5 });
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(22)).ReturnsAsync(2);

        var result = await _runtimeController.EmployerManage();

        Assert.That(result, Is.TypeOf<ViewResult>());
        var statuses = _runtimeController.ViewBag.SponsorInviteStatuses as IDictionary<int, string>;
        Assert.That(statuses, Is.Not.Null);
        Assert.That(statuses[22], Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Exhausted"));
    }

    [Test]
    public async Task Runtime_RefreshToken_Successful()
    {
        var session = new InterviewSession { Token = "old-token", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("old-token")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("old-token");
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null);

        Assert.That(success, Is.EqualTo(true));
        Assert.That(newToken, Is.Not.EqualTo("old-token"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Token == (string)newToken)), Times.Once);
    }

    [Test]
    public async Task Runtime_RefreshToken_InactiveSession_ReturnsError()
    {
        var session = new InterviewSession { Token = "expired", IsActive = false, TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("expired");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);

        Assert.That(error, Is.EqualTo("Invalid or expired session token."));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_RefreshToken_CompletedSession_ReturnsError()
    {
        var session = new InterviewSession { Token = "completed", IsActive = true, CompletedOnUtc = DateTime.UtcNow.AddMinutes(-5), TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("completed")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("completed");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);

        Assert.That(error, Is.EqualTo("Invalid or expired session token."));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_RefreshToken_ExpiredActiveSession_Renews()
    {
        var session = new InterviewSession
        {
            Id = 77,
            CustomerId = 12,
            ProductId = 44,
            Token = "expired-active",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-active")).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);

        var result = await _runtimeController.RefreshToken("expired-active");
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null)?.ToString();

        Assert.That(success, Is.EqualTo(true));
        Assert.That(newToken, Is.Not.Null);
        Assert.That(newToken, Is.Not.EqualTo("expired-active"));
        Assert.That(json.Value.GetType().GetProperty("tokenExpiryUtc").GetValue(json.Value, null), Is.Not.Null);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Token == newToken && s.IsActive)), Times.Once);
    }

    [Test]
    public async Task Runtime_SubmitAnswer_ExpiredActiveSession_ReturnsRenewedToken()
    {
        var session = new InterviewSession
        {
            Id = 93,
            CustomerId = 1,
            Token = "expired-submit",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-submit")).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        _interviewRuntimeService.Setup(x => x.SubmitAnswerAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new SubmitInterviewAnswerResponse
            {
                Success = true,
                Question = "Next question"
            });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var result = await controller.SubmitAnswer("expired-submit", "Answer");

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var successProperty = json.Value.GetType().GetProperty("success");
        var duplicateSuccessProperty = json.Value.GetType().GetProperty("Success");
        var newTokenProperty = json.Value.GetType().GetProperty("newToken");
        var duplicateReportUrlProperty = json.Value.GetType().GetProperty("ReportUrl");
        Assert.That(successProperty, Is.Not.Null);
        Assert.That(duplicateSuccessProperty, Is.Null);
        Assert.That(newTokenProperty, Is.Not.Null, "Expected renewed submit-answer response to include a token update.");
        Assert.That(duplicateReportUrlProperty, Is.Null);
        var success = successProperty.GetValue(json.Value, null);
        Assert.That(success, Is.EqualTo(true));
        var newToken = newTokenProperty.GetValue(json.Value, null)?.ToString();
        Assert.That(newToken, Is.Not.Null);
        Assert.That(newToken, Is.Not.EqualTo("expired-submit"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Token == newToken)), Times.Once);
    }

    [Test]
    public async Task Runtime_Stop_ExpiredActiveSession_ReturnsRenewedToken_WithSuccess()
    {
        var session = new InterviewSession
        {
            Id = 94,
            CustomerId = 1,
            Token = "expired-stop",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-stop")).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);
        _interviewRuntimeService.Setup(x => x.CompleteInterviewAsync(It.IsAny<string>(), "Stopped by user"))
            .ReturnsAsync(new CompleteInterviewResponse { Success = true, IsTerminated = true, Completion = "done" });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var result = await controller.Stop("expired-stop");

        var json = (JsonResult)result;
        var successProperty = json.Value.GetType().GetProperty("success");
        var duplicateSuccessProperty = json.Value.GetType().GetProperty("Success");
        var duplicateReportUrlProperty = json.Value.GetType().GetProperty("ReportUrl");
        var newTokenProperty = json.Value.GetType().GetProperty("newToken");
        Assert.That(successProperty, Is.Not.Null);
        Assert.That(duplicateSuccessProperty, Is.Null);
        Assert.That(duplicateReportUrlProperty, Is.Null);
        Assert.That(newTokenProperty, Is.Not.Null);

        var success = successProperty.GetValue(json.Value, null);
        var newToken = newTokenProperty.GetValue(json.Value, null)?.ToString();

        Assert.That(success, Is.EqualTo(true));
        Assert.That(newToken, Is.Not.Null.And.Not.EqualTo("expired-stop"));
    }

    [Test]
    public async Task Runtime_Get_With_Expired_Active_Token_Renews_And_Redirects()
    {
        var session = new InterviewSession
        {
            Id = 88,
            CustomerId = 15,
            ProductId = 66,
            SessionKey = "session-key",
            Token = "expired-runtime",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-runtime")).ReturnsAsync(session);
        _sessionService.Setup(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>())).Returns(Task.CompletedTask);

        var result = await _runtimeController.Runtime("expired-runtime");

        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
        var redirect = (RedirectToActionResult)result;
        Assert.That(redirect.ActionName, Is.EqualTo(nameof(MockAiInterviewController.Runtime)));
        Assert.That(redirect.RouteValues["token"], Is.Not.EqualTo("expired-runtime"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Once);
    }

    [Test]
    public async Task Runtime_Preserves_Service_Level_MediaFlags()
    {
        var runtimeModel = new InterviewRuntimeModel
        {
            SessionId = 1,
            ProductId = 1,
            SessionKey = "session-key",
            Token = "token",
            CurrentQuestion = "Q1",
            ClientSettings = new RuntimeClientSettingsModel
            {
                SpeechAvailable = false,
                RecordingAvailable = false
            }
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("token")).ReturnsAsync(new InterviewSession
        {
            Id = 1,
            CustomerId = 1,
            IsActive = true,
            Token = "token",
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        _interviewRuntimeService.Setup(x => x.EnsureInterviewStartedAsync(It.IsAny<InterviewSession>(), It.IsAny<Customer>()))
            .ReturnsAsync(runtimeModel);

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext ctx) => ctx.RouteName switch
            {
                var name when name == AIInterviewDefaults.MockReportRouteName => "/mockaiinterview/report/1",
                var name when name == AIInterviewDefaults.MockSubmitAnswerRouteName => "/mockaiinterview/submit-answer",
                var name when name == AIInterviewDefaults.MockStopRouteName => "/mockaiinterview/stop",
                var name when name == AIInterviewDefaults.MockRefreshTokenRouteName => "/mockaiinterview/refresh-token",
                var name when name == AIInterviewDefaults.MockSpeechTokenRouteName => "/mockaiinterview/speech-token",
                var name when name == AIInterviewDefaults.MockRecordingUploadRouteName => "/mockaiinterview/upload-recording",
                _ => string.Empty
            });
        controller.Url = urlHelper.Object;

        var result = await controller.Runtime("token");
        var viewResult = (ViewResult)result;
        var model = (InterviewRuntimeModel)viewResult.Model;

        Assert.That(model.ClientSettings.SpeechAvailable, Is.False);
        Assert.That(model.ClientSettings.RecordingAvailable, Is.False);
        Assert.That(model.ReportUrl, Is.EqualTo("/mockaiinterview/report/1"));
        Assert.That(model.ClientSettings.ReportUrl, Is.EqualTo("/mockaiinterview/report/1"));
    }

    [Test]
    public async Task Runtime_SubmitAnswer_CompletedResponse_UsesMockReportRoute()
    {
        var session = new InterviewSession
        {
            Id = 71,
            CustomerId = 1,
            Token = "complete-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("complete-token")).ReturnsAsync(session);
        _interviewRuntimeService.Setup(x => x.SubmitAnswerAsync("complete-token", "Answer"))
            .ReturnsAsync(new SubmitInterviewAnswerResponse
            {
                Success = true,
                IsTerminated = true,
                ReportUrl = string.Empty,
                Completion = "done"
            });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext ctx) => ctx.RouteName == AIInterviewDefaults.MockReportRouteName ? "/mockaiinterview/report/71" : string.Empty);
        controller.Url = urlHelper.Object;

        var result = await controller.SubmitAnswer("complete-token", "Answer");
        var json = (JsonResult)result;
        var reportUrl = json.Value.GetType().GetProperty("ReportUrl")?.GetValue(json.Value, null)?.ToString();

        Assert.That(reportUrl, Is.EqualTo("/mockaiinterview/report/71"));
    }

    [Test]
    public async Task Runtime_Stop_CompletedResponse_UsesMockReportRoute()
    {
        var session = new InterviewSession
        {
            Id = 72,
            CustomerId = 1,
            Token = "stop-token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("stop-token")).ReturnsAsync(session);
        _interviewRuntimeService.Setup(x => x.CompleteInterviewAsync("stop-token", "Stopped by user"))
            .ReturnsAsync(new CompleteInterviewResponse
            {
                Success = true,
                IsTerminated = true,
                ReportUrl = string.Empty
            });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            _eventPublisher.Object,
            null,
            null,
            null,
            _interviewRuntimeService.Object);

        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext ctx) => ctx.RouteName == AIInterviewDefaults.MockReportRouteName ? "/mockaiinterview/report/72" : string.Empty);
        controller.Url = urlHelper.Object;

        var result = await controller.Stop("stop-token");
        var json = (JsonResult)result;
        var reportUrl = json.Value.GetType().GetProperty("ReportUrl")?.GetValue(json.Value, null)?.ToString();

        Assert.That(reportUrl, Is.EqualTo("/mockaiinterview/report/72"));
    }

    [Test]
    public void RuntimeView_Contains_Recording_And_Upload_Hooks()
    {
        var runtimeViewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));

        Assert.That(runtimeViewText, Does.Contain("recordingUploadUrl"));
        Assert.That(runtimeViewText, Does.Contain("MediaRecorder"));
        Assert.That(runtimeViewText, Does.Contain("toggle-recording"));
        Assert.That(runtimeViewText, Does.Contain("uploadRecording"));
        Assert.That(runtimeViewText, Does.Contain("getUserMedia"));
        Assert.That(runtimeViewText, Does.Contain("SpeechSDK"));
        Assert.That(runtimeViewText, Does.Contain("speechTokenUrl"));
        Assert.That(runtimeViewText, Does.Contain("submitAnswer"));
        Assert.That(runtimeViewText, Does.Contain("stopInterview"));
        Assert.That(runtimeViewText, Does.Not.Contain("console.log(config)"));
        Assert.That(runtimeViewText, Does.Not.Contain("Settings Config"));
        Assert.That(runtimeViewText, Does.Not.Contain("Body: ${text}"));
        Assert.That(runtimeViewText, Does.Contain("tokenRefreshPromise"));
        Assert.That(runtimeViewText, Does.Contain("if (tokenRefreshPromise)"));
        Assert.That(runtimeViewText, Does.Not.Contain("tokenRefreshInFlight"));
        Assert.That(runtimeViewText, Does.Contain("showUnavailableQuestionState"));
        Assert.That(runtimeViewText, Does.Contain("normalized === 'AI service unavailable. Please try again later.'"));
        Assert.That(runtimeViewText, Does.Contain("const hasActiveQuestion = () => !interviewUnavailable && !isPlaceholderSpeechText(currentQuestionText());"));
        Assert.That(runtimeViewText, Does.Contain("submitButton.disabled = interviewUnavailable || !hasActiveQuestion();"));
        Assert.That(runtimeViewText, Does.Contain("interviewUnavailable = true;"));
        Assert.That(runtimeViewText, Does.Contain("let runtimeStoppedOrCompleted = false;"));
        Assert.That(runtimeViewText, Does.Contain("let stopInProgress = false;"));
        Assert.That(runtimeViewText, Does.Contain("if (runtimeStoppedOrCompleted || stopInProgress)"));
        Assert.That(runtimeViewText, Does.Contain("const autoSubmitDelaySeconds = 10;"));
        Assert.That(runtimeViewText, Does.Contain("clearInteractionTimers();"));
        Assert.That(runtimeViewText, Does.Contain("Auto submitting in ${countdownValue}"));
        Assert.That(runtimeViewText, Does.Contain("Please speak or type something."));
        Assert.That(runtimeViewText, Does.Contain("stopInProgress = true;"));
        Assert.That(runtimeViewText, Does.Contain("runtimeStoppedOrCompleted = true;"));
        Assert.That(runtimeViewText, Does.Contain("clearTimeout(tokenRefreshTimer);"));
        Assert.That(runtimeViewText, Does.Contain("if (!config.recordingUploadUrl || !blob || recordingUploadInFlight)"));
        Assert.That(runtimeViewText, Does.Contain("Camera permission was denied. You can continue by typing your answers."));
        Assert.That(runtimeViewText, Does.Contain("Microphone permission was denied. You can continue by typing your answers."));
        Assert.That(runtimeViewText, Does.Contain("display:none; position: absolute;"));
        Assert.That(runtimeViewText, Does.Contain("runtimeLog.style.display = 'none';"));

        Assert.That(runtimeViewText, Does.Not.Contain("AgoraRTC"));
        Assert.That(runtimeViewText, Does.Not.Contain("download.agora.io"));
        Assert.That(runtimeViewText, Does.Not.Contain("ensureAgoraSession"));
        Assert.That(runtimeViewText, Does.Not.Contain("renewAgoraToken"));
        Assert.That(runtimeViewText, Does.Not.Contain("leaveAgoraSession"));
        Assert.That(runtimeViewText, Does.Not.Contain("agora-token"));
    }

    [Test]
    public async Task Runtime_SpeechToken_ExpiredOrInactive_ReturnsSafeJson()
    {
        _interviewRuntimeService.Setup(x => x.GetSpeechTokenAsync("expired")).ReturnsAsync((SpeechTokenResponseModel)null);
        _interviewRuntimeService.Setup(x => x.GetSpeechTokenAsync("inactive")).ReturnsAsync((SpeechTokenResponseModel)null);

        var expired = await _runtimeController.SpeechToken("expired");
        var inactive = await _runtimeController.SpeechToken("inactive");

        Assert.That(((JsonResult)expired).Value.GetType().GetProperty("error").GetValue(((JsonResult)expired).Value, null), Is.EqualTo("Speech token service is unavailable."));
        Assert.That(((JsonResult)inactive).Value.GetType().GetProperty("error").GetValue(((JsonResult)inactive).Value, null), Is.EqualTo("Speech token service is unavailable."));
    }

    [Test]
    public async Task Admin_Invite_Validation_InvalidAttempts()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(1)).ReturnsAsync(new Customer { Id = 1, VendorId = 1 });
        _productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Product { Id = 10, VendorId = 1 });

        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "test@test.com", 10, 0, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.InvalidAttempts"));
    }

    [Test]
    public async Task Admin_Invite_Validation_InvalidExpiry()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(1)).ReturnsAsync(new Customer { Id = 1, VendorId = 1 });
        _productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Product { Id = 10, VendorId = 1 });

        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "test@test.com", 10, 1, DateTime.UtcNow.AddMinutes(-1)));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.InvalidExpiry"));
    }

    [Test]
    public void AdminController_Has_ConfigureAction()
    {
        var method = typeof(MockAiInterviewAdminController).GetMethod("Configure", new[] { typeof(ConfigurationModel) });
        Assert.That(method, Is.Not.Null);
    }

    [Test]
    public void MockAiInterviewController_Has_EmployerActions()
    {
        var createMethod = typeof(MockAiInterviewController).GetMethod("CreateInvite");
        var deactivateMethod = typeof(MockAiInterviewController).GetMethod("DeactivateInvite");
        Assert.That(createMethod, Is.Not.Null);
        Assert.That(deactivateMethod, Is.Not.Null);
    }

    [Test]
    public async Task Runtime_InvalidSession_ReturnsVisibleErrorView()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired"))
            .ReturnsAsync(new InterviewSession
            {
                Token = "expired",
                IsActive = false,
                TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
            });

        var result = await _runtimeController.Runtime("expired");

        Assert.That(result, Is.TypeOf<RedirectResult>());
        Assert.That(((RedirectResult)result).Url, Is.EqualTo("/"));
    }

    [Test]
    public async Task Runtime_ExpiredProductSession_RedirectsToProductUrlWithExpiredFlag()
    {
        var urlRecordService = new Mock<global::Nop.Services.Seo.IUrlRecordService>();
        urlRecordService.Setup(x => x.GetSeNameAsync(It.IsAny<Product>())).ReturnsAsync("sample-job");
        _productService.Setup(x => x.GetProductByIdAsync(42)).ReturnsAsync(new Product { Id = 42, Name = "Sample Job" });
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired-product"))
            .ReturnsAsync(new InterviewSession
            {
                Token = "expired-product",
                ProductId = 42,
                IsActive = false,
                TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
            });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            new Mock<IApplicationService>().Object,
            null,
            null,
            urlRecordService.Object);

        var result = await controller.Runtime("expired-product");

        Assert.That(result, Is.TypeOf<RedirectResult>());
        Assert.That(((RedirectResult)result).Url, Is.EqualTo("/sample-job?interviewError=expired"));
    }

    [Test]
    public async Task Stop_PublishesCompletionOnlyOnce()
    {
        var session = new InterviewSession
        {
            Id = 11,
            Token = "valid",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(10)
        };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("valid")).ReturnsAsync(session);
        _workContext.Setup(x => x.GetWorkingLanguageAsync())
            .ReturnsAsync(new Nop.Core.Domain.Localization.Language { Id = 2 });

        var firstResult = await _runtimeController.Stop("valid");
        var secondResult = await _runtimeController.Stop("valid");

        Assert.That(firstResult, Is.TypeOf<JsonResult>());
        Assert.That(secondResult, Is.TypeOf<JsonResult>());
        _eventPublisher.Verify(x => x.PublishAsync(It.Is<MockAiInterviewCompletedEvent>(message =>
            message.Session.Id == 11 && message.LanguageId == 2)), Times.Once);
    }

    private class TestRuntimeController : MockAiInterviewController
    {
        public TestRuntimeController(IInterviewSessionService sessionService, ILocalizationService localizationService, IWorkContext workContext, ISponsorInviteService inviteService, ICreditService creditService, ICustomerService customerService, IProductService productService, global::Nop.Services.Vendors.IVendorService vendorService, IApplicationService applicationService)
            : base(sessionService, localizationService, workContext, inviteService, creditService, customerService, productService, vendorService, applicationService) { }

        public async Task<IActionResult> TestFallback()
        {
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Missing", "Fallback text");
        }
    }

    [Test]
    public async Task Runtime_SubmitAnswer_Empty_ReturnsLocalizedJsonError()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        sessionService.Setup(x => x.GetSessionByTokenAsync("token")).ReturnsAsync(new InterviewSession
        {
            Token = "token",
            IsActive = true,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        });
        var controller = new TestRuntimeController(sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object);
        var result = await controller.SubmitAnswer("token", "");
        var json = (JsonResult)result;

        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var message = json.Value.GetType().GetProperty("message").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);

        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Answer cannot be empty."));
        Assert.That(error, Is.EqualTo("Answer cannot be empty."));
    }

    [Test]
    public async Task LocalizedErrorAsync_SetsStatusCode_WhenHttpContextExists()
    {
        var sessionService = new Mock<IInterviewSessionService>();
        var controller = new TestRuntimeController(sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.SubmitAnswer(null, null); // invalid token & answer -> triggering LocalizedErrorAsync

        Assert.That(httpContext.Response.StatusCode, Is.EqualTo(400));
        var json = (JsonResult)result;
        Assert.That(json.Value.GetType().GetProperty("success").GetValue(json.Value, null), Is.False);
    }

    [Test]
    public void MockAiInterviewController_MaskToken_Works()
    {
        var controller = new TestRuntimeController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object);

        var maskMethod = controller.GetType().GetMethod("MaskToken", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var maskedShort = maskMethod.Invoke(controller, new object[] { "12345" });
        var maskedLong = maskMethod.Invoke(controller, new object[] { "1234567890" });

        Assert.That(maskedShort, Is.EqualTo("*****"));
        Assert.That(maskedLong, Is.EqualTo("123456..."));
    }

    [Test]
    public void Runtime_NoAgoraSdkUsage()
    {
        var path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "Runtime.cshtml");
        if (!System.IO.File.Exists(path))
            path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "src", "Plugins", "Nop.Plugin.Misc.AIInterview", "Views", "MockAiInterview", "Runtime.cshtml"); // CI/CD path fallback

        var content = System.IO.File.ReadAllText(path);
        Assert.That(content.Contains("AgoraRTC"), Is.False, "Runtime should not contain AgoraRTC usage.");
    }

    [Test]
    public void Plugin_DoesNotShip_AIReferenceFiles()
    {
        var projectText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Nop.Plugin.Misc.AIInterview.csproj"));

        Assert.That(projectText, Does.Contain("<Compile Remove=\"AI_ReferenceFiles\\**\\*\" />"));
        Assert.That(projectText, Does.Contain("<Content Remove=\"AI_ReferenceFiles\\**\\*\" />"));
        Assert.That(projectText, Does.Contain("<None Remove=\"AI_ReferenceFiles\\**\\*\" />"));
    }
}
