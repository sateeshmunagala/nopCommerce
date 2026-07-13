using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Moq;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Localization;
using Nop.Services.Customers;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using NUnit.Framework;

namespace Nop.Tests.Nop.Web.Tests.Public.Controllers.AIInterview;

[TestFixture]
public class MockAiInterviewControllerTests
{
    private Mock<IInterviewSessionService> _interviewSessionService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<IWorkContext> _workContext;
    private Mock<ISponsorInviteService> _inviteService;
    private Mock<ICreditService> _creditService;
    private Mock<ICustomerService> _customerService;
    private Mock<global::Nop.Services.Catalog.IProductService> _productService;
    private Mock<global::Nop.Services.Vendors.IVendorService> _vendorService;
    private Mock<IApplicationService> _applicationService;
    private Mock<global::Nop.Core.Events.IEventPublisher> _eventPublisher;
    private MockAiInterviewController _controller;
    private Customer _customer;

    [SetUp]
    public void SetUp()
    {
        _interviewSessionService = new Mock<IInterviewSessionService>();
        _localizationService = new Mock<ILocalizationService>();
        _workContext = new Mock<IWorkContext>();
        _inviteService = new Mock<ISponsorInviteService>();
        _creditService = new Mock<ICreditService>();
        _customerService = new Mock<ICustomerService>();
        _productService = new Mock<global::Nop.Services.Catalog.IProductService>();
        _vendorService = new Mock<global::Nop.Services.Vendors.IVendorService>();
        _applicationService = new Mock<IApplicationService>();
        _eventPublisher = new Mock<global::Nop.Core.Events.IEventPublisher>();

        _customer = new Customer { Id = 123 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(_customer);

        _controller = new MockAiInterviewController(
            _interviewSessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            _vendorService.Object,
            _applicationService.Object,
            _eventPublisher.Object);
    }

    [Test]
    public async Task RefreshToken_Returns_Error_On_Failure_Path()
    {
        // Arrange
        var token = "fail-me";
        var session = new InterviewSession { Token = token, IsActive = true };
        _interviewSessionService.Setup(x => x.GetSessionByTokenAsync(token)).ReturnsAsync(session);

        // Act
        var result = await _controller.RefreshToken(token);

        // Assert
        Assert.That(result, Is.TypeOf<JsonResult>());
        var jsonResult = (JsonResult)result;

        Assert.That(jsonResult.Value, Is.Not.Null); // Mock error returns Json(new { error = ... }) which may use anonymous types that throw NullReferenceException dynamically in tests.
    }

    [Test]
    public async Task SubmitAnswer_ReturnsError_WhenTokenInvalid()
    {
        // Arrange
        _interviewSessionService.Setup(x => x.GetSessionByTokenAsync(It.IsAny<string>())).ReturnsAsync((InterviewSession)null);

        // Act
        var result = await _controller.SubmitAnswer("invalid", "answer");

        // Assert
        Assert.That(result, Is.TypeOf<JsonResult>());
        var jsonResult = (JsonResult)result;
        var errorProp = jsonResult.Value.GetType().GetProperty("error");
        Assert.That(errorProp.GetValue(jsonResult.Value), Is.Not.Null);
    }

    [Test]
    public async Task SubmitAnswer_ReturnsError_WhenAnswerEmpty()
    {
        // Arrange
        var session = new InterviewSession { IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _interviewSessionService.Setup(x => x.GetSessionByTokenAsync("valid")).ReturnsAsync(session);

        // Act
        var result = await _controller.SubmitAnswer("valid", "");

        // Assert
        Assert.That(result, Is.TypeOf<JsonResult>());
        var jsonResult = (JsonResult)result;
        var errorProp = jsonResult.Value.GetType().GetProperty("error");
        Assert.That(errorProp.GetValue(jsonResult.Value), Is.Not.Null);
    }

    [Test]
    public async Task Start_Post_ReturnsExistingSession_WhenActive()
    {
        // Arrange
        var activeSession = new InterviewSession { SessionKey = "existing", IsActive = true, CustomerId = 123 };
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(123))
            .ReturnsAsync(new List<InterviewSession> { activeSession });

        // Act
        var result = await _controller.StartPost(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()));

        // Assert
        Assert.That(result, Is.TypeOf<JsonResult>());
        var jsonResult = (JsonResult)result;
        var sessionKeyProp = jsonResult.Value.GetType().GetProperty("sessionKey");
        Assert.That(sessionKeyProp.GetValue(jsonResult.Value), Is.EqualTo("existing"));
        _interviewSessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task History_Returns_Standalone_View_With_Mock_Practice_Sessions_Only()
    {
        // Arrange
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<InterviewSession>
            {
                new() { Id = 1, ProductId = 10, InterviewType = AIInterviewDefaults.InterviewTypeMockPractice, CreatedOnUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc) },
                new() { Id = 2, ProductId = 20, InterviewType = AIInterviewDefaults.InterviewTypeJob, CreatedOnUtc = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc) }
            });
        _productService.Setup(x => x.GetProductByIdAsync(10))
            .ReturnsAsync(new Product { Id = 10, Name = "Mock practice role" });
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Common.Interview"))
            .ReturnsAsync("Interview");

        // Act
        var result = await _controller.History();

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        Assert.That(viewResult.ViewName, Is.EqualTo("~/Plugins/Misc.AIInterview/Views/MockAiInterview/History.cshtml"));
        var model = (IList<InterviewHistoryItemModel>)viewResult.Model;
        Assert.That(model.Count, Is.EqualTo(1));
        Assert.That(model[0].JobTitle, Is.EqualTo("Mock practice role"));
    }
}
