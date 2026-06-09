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
using Nop.Services.Catalog;
using Nop.Services.Customers;
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
    private Mock<ICustomerService> _customerService;
    private Mock<IProductService> _productService;
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
        _customerService = new Mock<ICustomerService>();
        _productService = new Mock<IProductService>();

        _customer = new Customer { Id = 123 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(_customer);
        _workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new global::Nop.Core.Domain.Localization.Language { Id = 1 });

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key);
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication>());
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<InterviewSession>());

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

        var tempData = new Mock<ITempDataDictionary>();
        _controller.TempData = tempData.Object;
    }

    [Test]
    public async Task Apply_Post_AlreadyApplied_ReturnsWarning()
    {
        // Arrange
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(_customer.Id, "Dev"))
            .ReturnsAsync(new List<JobApplication> { new JobApplication { JobTitle = "Dev", Status = "Applied", ProductId = 1 } });
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication> { new JobApplication { JobTitle = "Dev", Status = "Applied", ProductId = 1 } });

        // Act
        var result = await _controller.Apply(new ApplyModel { JobTitle = "Dev", ProductId = 1 });

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
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
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(_customer.Id, "Dev"))
            .ReturnsAsync(new List<JobApplication>());
        _interviewSessionService.Setup(x => x.GetHighestScoreByCustomerIdAndProductIdAsync(_customer.Id, 1))
            .ReturnsAsync(0);
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<InterviewSession>());

        var model = new ApplyModel { JobTitle = "Dev", ProductId = 1 };

        // Act
        var result = await _controller.Apply(model);

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        _notificationService.Verify(x => x.ErrorNotification(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
    }

    [Test]
    public async Task Apply_Post_InterviewRequired_ScoreBelowMin_ReturnsError()
    {
        // Arrange
        _aiInterviewSettings.InterviewRequired = true;
        _aiInterviewSettings.MinimumScore = 80;
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(_customer.Id, "Dev"))
            .ReturnsAsync(new List<JobApplication>());
        _interviewSessionService.Setup(x => x.GetHighestScoreByCustomerIdAndProductIdAsync(_customer.Id, 1))
            .ReturnsAsync(75);
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<InterviewSession>
            {
                new() { CustomerId = _customer.Id, ProductId = 1, Score = 75, CompletedOnUtc = DateTime.UtcNow }
            });

        var model = new ApplyModel { JobTitle = "Dev", ProductId = 1 };

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
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(_customer.Id, "Dev"))
            .ReturnsAsync(new List<JobApplication>());
        _interviewSessionService.Setup(x => x.GetHighestScoreByCustomerIdAndProductIdAsync(_customer.Id, 1))
            .ReturnsAsync(70);
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<InterviewSession>
            {
                new() { CustomerId = _customer.Id, ProductId = 1, Score = 40, CompletedOnUtc = DateTime.UtcNow },
                new() { CustomerId = _customer.Id, ProductId = 1, Score = 70, CompletedOnUtc = DateTime.UtcNow.AddDays(-1) }
            });

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns("resume.pdf");
        fileMock.Setup(f => f.ContentType).Returns("application/pdf");

        var model = new ApplyModel { JobTitle = "Dev", ProductId = 1, ResumeFile = fileMock.Object };

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
            a.ProductId == 1 &&
            a.ResumeDownloadId == 456 &&
            a.Status == "Applied")), Times.Once);
        _notificationService.Verify(x => x.SuccessNotification(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Once);
    }

    [Test]
    public async Task ApplyInline_Post_Successful_ReturnsJsonSuccess()
    {
        _aiInterviewSettings.ResumeRequired = false;
        var result = await _controller.ApplyInline(new ApplyModel { JobTitle = "Dev", ProductId = 1 });

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var success = (bool)json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        Assert.That(success, Is.True);
    }

    [Test]
    public async Task Apply_Post_ResumeReuse_Successful()
    {
        // Arrange
        _aiInterviewSettings.ResumeRequired = true;
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(_customer.Id, "New Job"))
            .ReturnsAsync(new List<JobApplication>());
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication> {
                new JobApplication { JobTitle = "Old Job", ResumeDownloadId = 789, CreatedOnUtc = DateTime.UtcNow.AddDays(-1) }
            });

        // Simulate validation error for missing resume
        _controller.ModelState.AddModelError("ResumeFile", "Required");

        var model = new ApplyModel { JobTitle = "New Job", ProductId = 2, ResumeFile = null };

        // Act
        var result = await _controller.Apply(model);

        // Assert
        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.Is<JobApplication>(a =>
            a.JobTitle == "New Job" &&
            a.ProductId == 2 &&
            a.ResumeDownloadId == 789)), Times.Once);
    }

    [TestCase("Rejected")]
    [TestCase("Withdrawn")]
    public async Task Apply_Post_AllowsReapply_ForTerminalStatus(string status)
    {
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication>
            {
                new() { ProductId = 1, JobTitle = "Dev", Status = status }
            });

        var result = await _controller.Apply(new ApplyModel { JobTitle = "Dev", ProductId = 1 });

        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.Is<JobApplication>(application =>
            application.ProductId == 1 && application.Status == JobApplicationStatuses.Applied)), Times.Once);
    }

    [Test]
    public async Task Apply_Post_InvalidUploadedResume_DoesNotReusePreviousResume()
    {
        _aiInterviewSettings.ResumeRequired = true;
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication>
            {
                new() { ProductId = 2, ResumeDownloadId = 789, CreatedOnUtc = DateTime.UtcNow.AddDays(-1) }
            });

        var file = new Mock<IFormFile>();
        file.Setup(x => x.FileName).Returns("resume.exe");
        file.Setup(x => x.Length).Returns(100);
        var model = new ApplyModel { JobTitle = "Dev", ProductId = 1, ResumeFile = file.Object };

        var result = await _controller.Apply(model);

        Assert.That(result, Is.TypeOf<ViewResult>());
        Assert.That(_controller.ModelState[nameof(ApplyModel.ResumeFile)].Errors, Is.Not.Empty);
        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.IsAny<JobApplication>()), Times.Never);
        _downloadService.Verify(x => x.InsertDownloadAsync(It.IsAny<Download>()), Times.Never);
    }

    [Test]
    public async Task Apply_Post_LegacyLinkedSession_SatisfiesInterviewRequirement()
    {
        _aiInterviewSettings.InterviewRequired = true;
        _aiInterviewSettings.MinimumScore = 60;
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication>
            {
                new() { Id = 42, ProductId = 1, JobTitle = "Dev", Status = JobApplicationStatuses.Rejected }
            });
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<InterviewSession>
            {
                new() { JobApplicationId = 42, ProductId = 0, Score = 80, CompletedOnUtc = DateTime.UtcNow }
            });
        _interviewSessionService.Setup(x => x.GetHighestScoreByCustomerIdAndProductIdAsync(_customer.Id, 1))
            .ReturnsAsync(80);

        var result = await _controller.Apply(new ApplyModel { JobTitle = "Dev", ProductId = 1 });

        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.IsAny<JobApplication>()), Times.Once);
    }
}
