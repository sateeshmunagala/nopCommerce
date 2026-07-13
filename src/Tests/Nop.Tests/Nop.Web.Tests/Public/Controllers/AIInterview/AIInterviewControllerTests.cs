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
    private Mock<IJobRequirementService> _jobRequirementService;
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
        _jobRequirementService = new Mock<IJobRequirementService>();
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<int>()))
            .ReturnsAsync(new JobRequirementsModel());
        _aiInterviewSettings = new AIInterviewSettings { Enabled = true };

        _customer = new Customer { Id = 123 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(_customer);
        _workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new global::Nop.Core.Domain.Localization.Language { Id = 1 });

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key);

        _controller = new AIInterviewController(
            _applicationService.Object,
            _interviewSessionService.Object,
            _aiInterviewSettings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object,
            _jobRequirementService.Object,
            null,
            null,
            null);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Test]
    public async Task Apply_Post_Gating_Fails_When_Interview_Required_And_No_Session()
    {
        // Arrange
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(1))
            .ReturnsAsync(new JobRequirementsModel { InterviewRequired = true });
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
    public async Task Apply_Post_MinimumScoreNotReached_ReturnsError()
    {
        // Arrange
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(1))
            .ReturnsAsync(new JobRequirementsModel { InterviewRequired = true });
        _aiInterviewSettings.MinimumScore = 80;
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(_customer.Id, "Dev"))
            .ReturnsAsync(new List<JobApplication>());
        _interviewSessionService.Setup(x => x.GetHighestScoreByCustomerIdAndProductIdAsync(_customer.Id, 1))
            .ReturnsAsync(70);

        var model = new ApplyModel { JobTitle = "Dev", ProductId = 1 };

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

    [Test]
    public async Task EmployerApplications_FiltersCorrectly()
    {
        // Arrange
        _customerService.Setup(x => x.IsAdminAsync(_customer)).ReturnsAsync(true);
        var model = new ApplicationListModel { CandidateNameOrEmail = "John", Status = "Applied" };
        model.PageSize = 20;
        var applications = new PagedList<JobApplication>(new List<JobApplication> { new JobApplication { Id = 1, CustomerId = 123 } }, 0, 20);

        _applicationService.Setup(x => x.GetApplicationsAsync(
            "John", "Applied", null, null, null, null, 0, 0, 0, 20, false))
            .ReturnsAsync(applications);

        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.IsAny<int[]>())).ReturnsAsync(new List<Customer> { _customer });
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(It.IsAny<int>())).ReturnsAsync(new List<InterviewSession>());

        // Act
        var result = await _controller.EmployerApplications(model);

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var resultModel = (ApplicationListModel)viewResult.Model;
        Assert.That(resultModel.Applications.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task UpdateStatus_SavesCorrectly()
    {
        // Arrange
        _customerService.Setup(x => x.IsAdminAsync(_customer)).ReturnsAsync(true);
        var application = new JobApplication { Id = 1, Status = "Applied" };
        _applicationService.Setup(x => x.GetJobApplicationByIdAsync(1)).ReturnsAsync(application);

        var model = new UpdateStatusModel { Id = 1, Status = "Shortlisted", StatusComment = "Good" };

        // Act
        var result = await _controller.UpdateStatus(model);

        // Assert
        _applicationService.Verify(x => x.UpdateJobApplicationAsync(It.Is<JobApplication>(a => a.Status == "Shortlisted" && a.StatusComment == "Good")), Times.Once);
        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
    }

    [Test]
    public async Task ExportCsv_ReturnsFile()
    {
        // Arrange
        _customerService.Setup(x => x.IsAdminAsync(_customer)).ReturnsAsync(true);
        var applications = new List<JobApplication> { new JobApplication { Id = 1, CustomerId = 123 } };
        _applicationService.Setup(x => x.GetApplicationsAsync(null, null, null, null, null, null, 0, 0, 0, int.MaxValue, false))
            .ReturnsAsync(new PagedList<JobApplication>(applications, 0, int.MaxValue));
        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.IsAny<int[]>())).ReturnsAsync(new List<Customer> { _customer });
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(It.IsAny<int>())).ReturnsAsync(new List<InterviewSession>());

        // Act
        var result = await _controller.ExportCsv(new ApplicationListModel());

        // Assert
        Assert.That(result, Is.TypeOf<FileContentResult>());
        var fileResult = (FileContentResult)result;
        Assert.That(fileResult.ContentType, Is.EqualTo("text/csv"));
    }

    [Test]
    public async Task MyActivity_Defaults_To_AppliedJobs_Tab()
    {
        // Act
        var result = await _controller.MyActivity();

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        Assert.That(viewResult.ViewName, Is.EqualTo("~/Plugins/Misc.AIInterview/Views/MyActivity.cshtml"));

        var model = (MyActivityPageModel)viewResult.Model;
        Assert.That(model.ActiveTab, Is.EqualTo(AIInterviewDefaults.MyActivityAppliedJobsTabKey));
        Assert.That(model.AppliedJobs, Is.Not.Null);
    }

    [Test]
    public async Task MyActivity_Unknown_Tab_Falls_Back_To_AppliedJobs()
    {
        // Act
        var result = await _controller.MyActivity("not-a-real-tab");

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = (MyActivityPageModel)((ViewResult)result).Model;
        Assert.That(model.ActiveTab, Is.EqualTo(AIInterviewDefaults.MyActivityAppliedJobsTabKey));
    }

    [Test]
    public async Task MyActivity_Htmx_Request_Returns_Tab_Partial()
    {
        // Arrange
        _controller.ControllerContext.HttpContext.Request.Headers["HX-Request"] = "true";

        // Act
        var result = await _controller.MyActivity(AIInterviewDefaults.MyActivityAppliedJobsTabKey);

        // Assert
        Assert.That(result, Is.TypeOf<PartialViewResult>());
        var partialViewResult = (PartialViewResult)result;
        Assert.That(partialViewResult.ViewName, Is.EqualTo("~/Plugins/Misc.AIInterview/Views/Shared/_MyActivityTabContent.cshtml"));

        var model = (MyActivityPageModel)partialViewResult.Model;
        Assert.That(model.ActiveTab, Is.EqualTo(AIInterviewDefaults.MyActivityAppliedJobsTabKey));
    }
}
