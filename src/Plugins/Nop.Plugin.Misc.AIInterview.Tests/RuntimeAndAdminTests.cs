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
        Assert.That(error, Is.EqualTo("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized"));
    }

    [Test]
    public async Task Runtime_InvalidToken_ReturnsLocalizedError()
    {
        var result = await _runtimeController.SubmitAnswer(null, "Answer");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken"));
    }

    [Test]
    public async Task Runtime_LocalizationFallback_Works()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync((Customer)null);
        // Using a trick here by mocking the controller to use a missing resource
        var controller = new TestRuntimeController(_sessionService.Object, _localizationService.Object, _workContext.Object, _inviteService.Object, _creditService.Object, _customerService.Object, _productService.Object, new Mock<global::Nop.Services.Vendors.IVendorService>().Object, new Mock<IApplicationService>().Object);
        var result = await controller.TestFallback();
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Fallback text"));
    }

    [Test]
    public async Task Admin_TopUp_InvalidAmount_ReturnsError()
    {
        var result = await _adminController.TopUpCredits(1, -10);
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(error, Is.EqualTo("Plugins.Misc.AIInterview.Admin.TopUp.InvalidAmount"));
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
        var model = new ConfigurationModel
        {
            Enabled = true,
            ApiKey = "new-key",
            Provider = "OpenAI",
            AiModel = "gpt-4",
            Prompt = "Be a helpful assistant",
            CreditPackAmount = 100,
            CreditPackPrice = 50
        };

        await _adminController.Configure(model);

        _settingService.Verify(x => x.SaveSettingAsync(It.Is<AIInterviewSettings>(s =>
            s.Enabled && s.ApiKey == "new-key" && s.Provider == "OpenAI" && s.Model == "gpt-4" && s.Prompt == "Be a helpful assistant" && s.CreditPackAmount == 100 && s.CreditPackPrice == 50)), Times.Once);
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
    public async Task Runtime_RefreshToken_Successful()
    {
        var session = new InterviewSession { Token = "old-token", IsActive = true };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("old-token")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("old-token");
        var json = (JsonResult)result;
        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null);

        Assert.That(newToken, Is.Not.EqualTo("old-token"));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.Is<InterviewSession>(s => s.Token == (string)newToken)), Times.Once);
    }

    [Test]
    public async Task Runtime_RefreshToken_Failure_ReturnsError()
    {
        var session = new InterviewSession { Token = "fail-me", IsActive = true };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("fail-me")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("fail-me");
        var json = (JsonResult)result;
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);

        Assert.That(error, Is.EqualTo("Plugins.Misc.AIInterview.Runtime.Error.TokenServiceFailure"));
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
                IsActive = true,
                TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-1)
            });

        var result = await _runtimeController.Runtime("expired");

        Assert.That(result, Is.TypeOf<RedirectResult>());
        Assert.That(((RedirectResult)result).Url, Is.EqualTo("/"));
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
}
