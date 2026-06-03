using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Core;
using NUnit.Framework;

namespace Nop.Tests.Nop.Web.Tests.Public.Controllers.AIInterview;

[TestFixture]
public class MockAiInterviewAdminControllerTests
{
    private Mock<ICreditService> _creditService;
    private Mock<ISponsorInviteService> _inviteService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<INotificationService> _notificationService;
    private Mock<IWorkContext> _workContext;
    private Mock<ISettingService> _settingService;
    private AIInterviewSettings _aiInterviewSettings;
    private MockAIInterviewSettings _mockAIInterviewSettings;
    private MockAiInterviewAdminController _controller;
    private Customer _admin;

    [SetUp]
    public void SetUp()
    {
        _creditService = new Mock<ICreditService>();
        _inviteService = new Mock<ISponsorInviteService>();
        _localizationService = new Mock<ILocalizationService>();
        _notificationService = new Mock<INotificationService>();
        _workContext = new Mock<IWorkContext>();
        _settingService = new Mock<ISettingService>();
        _aiInterviewSettings = new AIInterviewSettings();
        _mockAIInterviewSettings = new MockAIInterviewSettings();

        _admin = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(_admin);

        _controller = new MockAiInterviewAdminController(
            _creditService.Object,
            _inviteService.Object,
            _localizationService.Object,
            _notificationService.Object,
            _workContext.Object,
            _settingService.Object,
            _aiInterviewSettings,
            _mockAIInterviewSettings);
    }

    [Test]
    public async Task TopUpCredits_ReturnsError_WhenAmountIsZeroOrNegative()
    {
        // Act
        var result = await _controller.TopUpCredits(123, 0);

        // Assert
        Assert.That(result, Is.TypeOf<JsonResult>());
        var jsonResult = (JsonResult)result;
        var errorProp = jsonResult.Value.GetType().GetProperty("error");
        Assert.That(errorProp.GetValue(jsonResult.Value), Is.Not.Null);
    }

    [Test]
    public async Task CreateSponsorInvite_ReturnsError_OnServiceException()
    {
        // Arrange
        _inviteService.Setup(x => x.CreateInviteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime?>()))
            .ThrowsAsync(new NopException("Service error"));

        // Act
        var result = await _controller.CreateSponsorInvite("test@test.com", 1, 1, null, null);

        // Assert
        Assert.That(result, Is.TypeOf<JsonResult>());
        var jsonResult = (JsonResult)result;
        var errorProp = jsonResult.Value.GetType().GetProperty("error");
        Assert.That(errorProp.GetValue(jsonResult.Value), Is.EqualTo("Service error"));
    }
}
