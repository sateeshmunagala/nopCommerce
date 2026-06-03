using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Localization;
using Nop.Services.Customers;
using Nop.Core;
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

        _customer = new Customer { Id = 123 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(_customer);

        _controller = new MockAiInterviewController(
            _interviewSessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object);
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

        var errorProp = jsonResult.Value.GetType().GetProperty("error");
        Assert.That(errorProp.GetValue(jsonResult.Value), Is.Not.Null);
    }
}
