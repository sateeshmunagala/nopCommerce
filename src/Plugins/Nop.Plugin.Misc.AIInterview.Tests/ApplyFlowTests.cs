using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Plugin.Misc.AIInterview.Validators;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class ApplyFlowTests
{
    private Mock<IApplicationService> _applicationService;
    private Mock<IInterviewSessionService> _interviewSessionService;
    private AIInterviewSettings _aiInterviewSettings;
    private Mock<IWorkContext> _workContext;
    private Mock<INotificationService> _notificationService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<IDownloadService> _downloadService;
    private AIInterviewController _controller;
    private Customer _customer;

    [SetUp]
    public void SetUp()
    {
        _applicationService = new Mock<IApplicationService>();
        _interviewSessionService = new Mock<IInterviewSessionService>();
        _aiInterviewSettings = new AIInterviewSettings { Enabled = true };
        _workContext = new Mock<IWorkContext>();
        _notificationService = new Mock<INotificationService>();
        _localizationService = new Mock<ILocalizationService>();
        _downloadService = new Mock<IDownloadService>();

        _customer = new Customer { Id = 123 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(_customer);

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key);

        _controller = new AIInterviewController(
            _applicationService.Object,
            _interviewSessionService.Object,
            _aiInterviewSettings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object);

        var tempData = new Mock<ITempDataDictionary>();
        _controller.TempData = tempData.Object;
    }

    [Test]
    public async Task Apply_Post_AlreadyApplied_ReturnsWarning()
    {
        // Arrange
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication> { new JobApplication() });

        // Act
        var result = await _controller.Apply(new ApplyModel());

        // Assert
        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        var redirectResult = (RedirectToRouteResult)result;
        Assert.That(redirectResult.RouteName, Is.EqualTo(AIInterviewDefaults.IndexRouteName));
        _notificationService.Verify(x => x.WarningNotification(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
    }

    [Test]
    public async Task Apply_Post_ResumeRequired_InvalidModel_ReturnsView()
    {
        // Arrange
        _aiInterviewSettings.ResumeRequired = true;
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication>());

        var model = new ApplyModel { JobTitle = "Dev" };
        _controller.ModelState.AddModelError("ResumeFile", "Required");

        // Act
        var result = await _controller.Apply(model);

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        Assert.That(viewResult.Model, Is.EqualTo(model));
    }

    [Test]
    public async Task Apply_Post_InterviewRequired_NoSession_ReturnsError()
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
    public async Task Apply_Post_MinimumScoreNotReached_ReturnsError()
    {
        // Arrange
        _aiInterviewSettings.InterviewRequired = true;
        _aiInterviewSettings.MinimumScore = 80;
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication>());
        _interviewSessionService.Setup(x => x.GetLatestCompletedSessionByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new InterviewSession { Score = 70, CompletedOnUtc = DateTime.UtcNow });

        var model = new ApplyModel { JobTitle = "Dev" };

        // Act
        var result = await _controller.Apply(model);

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        _notificationService.Verify(x => x.ErrorNotification(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
    }

    [Test]
    public async Task Apply_Post_Successful_SavesApplication()
    {
        // Arrange
        _aiInterviewSettings.InterviewRequired = true;
        _aiInterviewSettings.MinimumScore = 60;
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication>());
        _interviewSessionService.Setup(x => x.GetLatestCompletedSessionByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new InterviewSession { Score = 70, CompletedOnUtc = DateTime.UtcNow });

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns("resume.pdf");
        fileMock.Setup(f => f.ContentType).Returns("application/pdf");

        var model = new ApplyModel { JobTitle = "Dev", ResumeFile = fileMock.Object };

        _downloadService.Setup(x => x.GetDownloadBitsAsync(fileMock.Object)).ReturnsAsync(new byte[] { 1, 2, 3 });
        _downloadService.Setup(x => x.InsertDownloadAsync(It.IsAny<Download>()))
            .Callback<Download>(d => d.Id = 456)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.Apply(model);

        // Assert
        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.Is<JobApplication>(a =>
            a.CustomerId == _customer.Id &&
            a.JobTitle == "Dev" &&
            a.ResumeDownloadId == 456 &&
            a.Status == "Applied")), Times.Once);
        _notificationService.Verify(x => x.SuccessNotification(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
    }
}
