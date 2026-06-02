using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
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
    private AIInterviewRuntimeController _runtimeController;

    private Mock<ICreditService> _creditService;
    private Mock<ISponsorInviteService> _inviteService;
    private Mock<INotificationService> _notificationService;
    private AIInterviewAdminController _adminController;

    private Mock<IProductService> _productService;
    private Mock<ICustomerService> _customerService;
    private SponsorInviteService _inviteServiceImplementation;

    [SetUp]
    public void SetUp()
    {
        _sessionService = new Mock<IInterviewSessionService>();
        _localizationService = new Mock<ILocalizationService>();
        _workContext = new Mock<IWorkContext>();
        _runtimeController = new AIInterviewRuntimeController(_sessionService.Object, _localizationService.Object, _workContext.Object);

        _creditService = new Mock<ICreditService>();
        _inviteService = new Mock<ISponsorInviteService>();
        _notificationService = new Mock<INotificationService>();
        _adminController = new AIInterviewAdminController(_creditService.Object, _inviteService.Object, _localizationService.Object, _notificationService.Object, _workContext.Object);

        _productService = new Mock<IProductService>();
        _customerService = new Mock<ICustomerService>();
        _inviteServiceImplementation = new SponsorInviteService(null, _productService.Object, _customerService.Object, _localizationService.Object);

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key == "Plugins.Misc.AIInterview.Missing" ? "" : key);
    }

    [Test]
    public async Task Runtime_Start_Unauthorized_ReturnsError()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync((Customer)null);
        var result = await _runtimeController.Start();
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
        var controller = new TestRuntimeController(_sessionService.Object, _localizationService.Object, _workContext.Object);
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
    public async Task Admin_Invite_Validation_InvalidOwnership()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(1)).ReturnsAsync(new Customer { Id = 1, VendorId = 1 });
        _productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Product { Id = 10, VendorId = 2 }); // Owned by vendor 2

        var ex = Assert.ThrowsAsync<NopException>(async () => await _inviteServiceImplementation.CreateInviteAsync(1, "test@test.com", 10, 1, null));
        Assert.That(ex.Message, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Invite.InvalidOwnership"));
    }

    private class TestRuntimeController : AIInterviewRuntimeController
    {
        public TestRuntimeController(IInterviewSessionService sessionService, ILocalizationService localizationService, IWorkContext workContext)
            : base(sessionService, localizationService, workContext) { }

        public async Task<IActionResult> TestFallback()
        {
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Missing", "Fallback text");
        }
    }
}
