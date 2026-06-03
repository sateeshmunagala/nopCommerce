using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Media;
using Nop.Services.Customers;
using Nop.Services.Catalog;
using Nop.Core;
using NUnit.Framework;

namespace Nop.Tests.Nop.Web.Tests.Public.Controllers.AIInterview;

[TestFixture]
public class AIInterviewControllerTests
{
    private Mock<IApplicationService> _applicationService;
    private Mock<IInterviewSessionService> _interviewSessionService;
    private Mock<IWorkContext> _workContext;
    private Mock<INotificationService> _notificationService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<IDownloadService> _downloadService;
    private Mock<ICustomerService> _customerService;
    private Mock<IProductService> _productService;
    private AIInterviewSettings _aiInterviewSettings;
    private AIInterviewController _controller;
    private Customer _customer;

    [SetUp]
    public void SetUp()
    {
        _applicationService = new Mock<IApplicationService>();
        _interviewSessionService = new Mock<IInterviewSessionService>();
        _workContext = new Mock<IWorkContext>();
        _notificationService = new Mock<INotificationService>();
        _localizationService = new Mock<ILocalizationService>();
        _downloadService = new Mock<IDownloadService>();
        _customerService = new Mock<ICustomerService>();
        _productService = new Mock<IProductService>();
        _aiInterviewSettings = new AIInterviewSettings { Enabled = true };

        _customer = new Customer { Id = 123 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(_customer);

        _controller = new AIInterviewController(
            _applicationService.Object,
            _interviewSessionService.Object,
            _aiInterviewSettings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object);
    }

    [Test]
    public async Task Apply_Post_Gating_Fails_When_Interview_Required_And_No_Session()
    {
        // Arrange
        _aiInterviewSettings.InterviewRequired = true;
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication>());
        _interviewSessionService.Setup(x => x.GetLatestCompletedSessionByCustomerIdAsync(_customer.Id))
            .ReturnsAsync((InterviewSession)null);

        var model = new ApplyModel { JobTitle = "Dev" };

        // Act
        var result = await _controller.Apply(model);

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        _notificationService.Verify(x => x.ErrorNotification(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
    }

    [Test]
    public async Task EmployerApplications_Returns_Challenge_When_Not_Authorized()
    {
        // Arrange
        _customerService.Setup(x => x.IsAdminAsync(_customer)).ReturnsAsync(false);
        _customer.VendorId = 0;

        // Act
        var result = await _controller.EmployerApplications(new ApplicationListModel());

        // Assert
        Assert.That(result, Is.TypeOf<ChallengeResult>());
    }
}
