using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Seo;
using Nop.Services.Stores;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class EmployerTests
{
    private Mock<IApplicationService> _applicationService;
    private Mock<IInterviewSessionService> _interviewSessionService;
    private Mock<ICustomerService> _customerService;
    private Mock<IWorkContext> _workContext;
    private Mock<INotificationService> _notificationService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<IProductService> _productService;
    private Mock<ISponsorInviteService> _inviteService;
    private Mock<ICreditService> _creditService;
    private Mock<IJobRequirementService> _jobRequirementService;
    private Mock<IDownloadService> _downloadService;
    private Mock<IProductTemplateService> _productTemplateService;
    private Mock<IUrlRecordService> _urlRecordService;
    private Mock<ISpecificationAttributeService> _specificationAttributeService;
    private Mock<IShoppingCartService> _shoppingCartService;
    private Mock<IStoreContext> _storeContext;
    private AIInterviewController _controller;
    private MockAiInterviewController _mockAiController;
    private Customer _employer;

    [SetUp]
    public void SetUp()
    {
        _applicationService = new Mock<IApplicationService>();
        _interviewSessionService = new Mock<IInterviewSessionService>();
        _customerService = new Mock<ICustomerService>();
        _workContext = new Mock<IWorkContext>();
        _notificationService = new Mock<INotificationService>();
        _localizationService = new Mock<ILocalizationService>();
        _productService = new Mock<IProductService>();
        _inviteService = new Mock<ISponsorInviteService>();
        _creditService = new Mock<ICreditService>();
        _jobRequirementService = new Mock<IJobRequirementService>();
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<int>()))
            .ReturnsAsync(new JobRequirementsModel());
        _jobRequirementService.Setup(x => x.IsJobProductAsync(It.IsAny<Product>()))
            .ReturnsAsync(true);
        _jobRequirementService.Setup(x => x.SaveRequirementsAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<decimal>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _downloadService = new Mock<IDownloadService>();
        _productTemplateService = new Mock<IProductTemplateService>();
        _urlRecordService = new Mock<IUrlRecordService>();
        _specificationAttributeService = new Mock<ISpecificationAttributeService>();
        _shoppingCartService = new Mock<IShoppingCartService>();
        _storeContext = new Mock<IStoreContext>();

        _employer = new Customer { Id = 123, VendorId = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(_employer);

        _customerService.Setup(x => x.IsAdminAsync(It.IsAny<Customer>(), It.IsAny<bool>())).ReturnsAsync(false);
        _customerService.Setup(x => x.IsAdminAsync(It.IsAny<Customer>())).ReturnsAsync(false);
        _workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new global::Nop.Core.Domain.Localization.Language { Id = 1 });
        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((string key, int _, bool __, string ___, bool ____) => key);
        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key);

        _creditService.Setup(x => x.GetOrCreateWalletAsync(It.IsAny<int>())).ReturnsAsync(new CreditWallet { Balance = 500 });
        _storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(new Nop.Core.Domain.Stores.Store { Id = 1 });
        _shoppingCartService.Setup(x => x.GetShoppingCartAsync(It.IsAny<Customer>(), ShoppingCartType.Wishlist, 1, It.IsAny<int?>(), null, null, 0))
            .ReturnsAsync(new List<ShoppingCartItem>());

        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(It.IsAny<int>())).ReturnsAsync(new List<JobApplication>());
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(It.IsAny<int>())).ReturnsAsync(new List<InterviewSession>());

        _controller = new AIInterviewController(
            _applicationService.Object,
            _interviewSessionService.Object,
            new AIInterviewSettings { Enabled = true },
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object,
            _jobRequirementService.Object,
            _productTemplateService.Object,
            _urlRecordService.Object,
            null,
            _specificationAttributeService.Object,
            null,
            null,
            null,
            _shoppingCartService.Object,
            _storeContext.Object);

        _mockAiController = new MockAiInterviewController(
            _interviewSessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object);
    }

    [Test]
    public async Task List_Unauthorized_ReturnsChallenge()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 456, VendorId = 0 });
        _customerService.Setup(x => x.IsAdminAsync(It.IsAny<Customer>())).ReturnsAsync(false);
        var result = await _controller.EmployerApplications(new ApplicationListModel());
        Assert.That(result, Is.TypeOf<ChallengeResult>());
    }

    [Test]
    public async Task List_FiltersCorrectly()
    {
        var model = new ApplicationListModel { CandidateNameOrEmail = "John", Status = "Pending", PageSize = 20 };
        var applications = new PagedList<JobApplication>(new List<JobApplication> { new JobApplication { Id = 1, CustomerId = 789 } }, 0, 20);

        _applicationService.Setup(x => x.GetApplicationsAsync(
            "John", "Pending", null, null, null, null, 0, 1, 0, 20, false))
            .ReturnsAsync(applications);

        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.IsAny<int[]>())).ReturnsAsync(new List<Customer> { new Customer { Id = 789, Email = "john@example.com" } });

        var result = await _controller.EmployerApplications(model);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var resultModel = (ApplicationListModel)viewResult.Model;
        Assert.That(resultModel.Applications.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task EmployerApplications_Populates_ReportPanelUrl_For_Completed_Session()
    {
        var applications = new PagedList<JobApplication>(new List<JobApplication>
        {
            new() { Id = 5, CustomerId = 789, ProductId = 22, JobTitle = "Platform Engineer", CreatedOnUtc = DateTime.UtcNow, Status = JobApplicationStatuses.Completed }
        }, 0, 10);
        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.Action(It.IsAny<UrlActionContext>()))
            .Returns<UrlActionContext>(context =>
                context.Action switch
                {
                    "Report" => "/AIInterview/Report/91",
                    "ReportPanel" => "/AIInterview/ReportPanel?sessionId=91",
                    _ => string.Empty
                });
        _controller.Url = urlHelper.Object;

        _applicationService.Setup(x => x.GetApplicationsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<decimal?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(applications);
        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.IsAny<int[]>()))
            .ReturnsAsync(new List<Customer> { new() { Id = 789, FirstName = "Jamie", LastName = "Doe", Email = "jamie@example.com" } });
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(789))
            .ReturnsAsync(new List<InterviewSession>
            {
                new() { Id = 91, CustomerId = 789, ProductId = 22, JobApplicationId = 5, CompletedOnUtc = DateTime.UtcNow, Score = 88, ReportData = "ready" }
            });

        var result = await _controller.EmployerApplications(new ApplicationListModel());

        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = (ApplicationListModel)((ViewResult)result).Model;
        var application = model.Applications.Single();
        Assert.That(application.InterviewReportUrl, Does.Contain("/AIInterview/Report/91").IgnoreCase);
        Assert.That(application.InterviewReportPanelUrl, Does.Contain("sessionId=91").IgnoreCase);
    }

    [Test]
    public async Task UpdateStatus_SavesCorrectly()
    {
        var application = new JobApplication { Id = 1, Status = "Applied", ProductId = 10 };
        _applicationService.Setup(x => x.GetJobApplicationByIdAsync(1)).ReturnsAsync(application);
        _productService.Setup(x => x.GetProductByIdAsync(10)).ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 10, VendorId = 1 });

        var model = new UpdateStatusModel { Id = 1, Status = "Shortlisted", StatusComment = "Great candidate" };
        var result = await _controller.UpdateStatus(model);

        _applicationService.Verify(x => x.UpdateJobApplicationAsync(It.Is<JobApplication>(a => a.Status == "Shortlisted" && a.StatusComment == "Great candidate")), Times.Once);
        Assert.That(result, Is.TypeOf<RedirectToActionResult>());
    }

    [Test]
    public async Task ExportCsv_OutputShape()
    {
        var applications = new PagedList<JobApplication>(new List<JobApplication> {
            new JobApplication { Id = 1, CustomerId = 789, CreatedOnUtc = new DateTime(2024, 1, 1) }
        }, 0, 10);

        _applicationService.Setup(x => x.GetApplicationsAsync(null, null, null, null, null, null, 0, 1, 0, int.MaxValue, false))
            .ReturnsAsync(applications);
        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.IsAny<int[]>())).ReturnsAsync(new List<Customer> { new Customer { Id = 789, FirstName = "John", LastName = "Doe", Email = "john@example.com" } });

        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ID")).ReturnsAsync("ID");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Candidate")).ReturnsAsync("Candidate");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Email")).ReturnsAsync("Email");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.History.Status")).ReturnsAsync("Status");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.History.Score")).ReturnsAsync("Score");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.History.Date")).ReturnsAsync("Date");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.JobTitle")).ReturnsAsync("Job Title");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ChargeMode")).ReturnsAsync("Charge Mode");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ChargeMode.CompanySponsored")).ReturnsAsync("Company sponsored");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ChargeMode.CandidatePaid")).ReturnsAsync("Candidate paid");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Attempts")).ReturnsAsync("Attempts");

        var result = await _controller.ExportCsv(new ApplicationListModel());

        Assert.That(result, Is.TypeOf<FileContentResult>());
        var fileResult = (FileContentResult)result;
        var csv = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
        Assert.That(csv, Does.Contain("ID,Candidate,Email,Status,Score,Date"));
        Assert.That(csv, Does.Contain("Job Title"));
        Assert.That(csv, Does.Contain("Charge Mode"));
        Assert.That(csv, Does.Contain("Attempts"));
        Assert.That(csv, Does.Not.Contain("Prompt Source"));
        Assert.That(csv, Does.Contain("1,\"John Doe\",\"john@example.com\""));
    }

    [Test]
    public async Task ExportCsv_Applies_OnlyWithInterviewScore_And_InterviewSort()
    {
        var applications = new PagedList<JobApplication>(new List<JobApplication>
        {
            new() { Id = 1, CustomerId = 100, ProductId = 21, JobTitle = "Role A", Status = JobApplicationStatuses.Applied, CreatedOnUtc = new DateTime(2024, 1, 2) },
            new() { Id = 2, CustomerId = 101, ProductId = 22, JobTitle = "Role B", Status = JobApplicationStatuses.Completed, CreatedOnUtc = new DateTime(2024, 1, 3) },
            new() { Id = 3, CustomerId = 102, ProductId = 23, JobTitle = "Role C", Status = JobApplicationStatuses.Completed, CreatedOnUtc = new DateTime(2024, 1, 1) }
        }, 0, 10);

        _applicationService.Setup(x => x.GetApplicationsAsync(null, null, null, null, null, null, 0, 1, 0, int.MaxValue, false))
            .ReturnsAsync(applications);
        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.IsAny<int[]>()))
            .ReturnsAsync(new List<Customer>
            {
                new() { Id = 100, FirstName = "Alpha", LastName = "Candidate", Email = "alpha@example.com" },
                new() { Id = 101, FirstName = "Bravo", LastName = "Candidate", Email = "bravo@example.com" },
                new() { Id = 102, FirstName = "Charlie", LastName = "Candidate", Email = "charlie@example.com" }
            });
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(100)).ReturnsAsync(new List<InterviewSession>());
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(101)).ReturnsAsync(new List<InterviewSession>
        {
            new() { JobApplicationId = 2, ProductId = 22, Score = 82, CompletedOnUtc = new DateTime(2024, 1, 3), SponsorInviteId = 12 }
        });
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(102)).ReturnsAsync(new List<InterviewSession>
        {
            new() { JobApplicationId = 3, ProductId = 23, Score = 61, CompletedOnUtc = new DateTime(2024, 1, 4) }
        });
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ID")).ReturnsAsync("ID");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Candidate")).ReturnsAsync("Candidate");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Email")).ReturnsAsync("Email");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.History.Status")).ReturnsAsync("Status");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.History.Score")).ReturnsAsync("Score");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.History.Date")).ReturnsAsync("Date");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.JobTitle")).ReturnsAsync("Job Title");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ChargeMode")).ReturnsAsync("Charge Mode");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ChargeMode.CompanySponsored")).ReturnsAsync("Company sponsored");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ChargeMode.CandidatePaid")).ReturnsAsync("Candidate paid");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Attempts")).ReturnsAsync("Attempts");

        var result = await _controller.ExportCsv(new ApplicationListModel
        {
            OnlyWithInterviewScore = true,
            InterviewSort = "LowestScorersFirst"
        });

        var csv = System.Text.Encoding.UTF8.GetString(((FileContentResult)result).FileContents);
        Assert.That(csv, Does.Not.Contain("alpha@example.com"));
        Assert.That(csv.IndexOf("charlie@example.com", StringComparison.Ordinal), Is.LessThan(csv.IndexOf("bravo@example.com", StringComparison.Ordinal)));
        Assert.That(csv, Does.Contain("Company sponsored"));
        Assert.That(csv, Does.Contain("Candidate paid"));
    }

    [Test]
    public async Task SponsorInvites_ReturnsViewWithBalance()
    {
        var invites = new List<SponsorInvite> { new SponsorInvite { Id = 1, Email = "invited@test.com", IsActive = true } };
        _inviteService.Setup(x => x.GetSponsorInvitesAsync(123)).ReturnsAsync(invites);

        var result = await _mockAiController.EmployerManage();

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        Assert.That(viewResult.ViewData["CreditBalance"], Is.EqualTo(500m));
        Assert.That(viewResult.ViewData["CreditBalanceDisplay"], Is.EqualTo("500"));
        Assert.That(viewResult.Model, Is.EqualTo(invites));
    }

    [Test]
    public async Task CreateInvite_Flow_Success()
    {
        _productService.Setup(x => x.GetProductByIdAsync(10))
            .ReturnsAsync(new Product { Id = 10, VendorId = _employer.VendorId, Name = "AI Developer" });

        var result = await _mockAiController.CreateInvite("invited@test.com", 10, 1, null);

        _inviteService.Verify(x => x.CreateInviteAsync(123, "invited@test.com", 10, 1, null), Times.Once);
        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var success = (bool)json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        Assert.That(success, Is.True);
    }

    [Test]
    public async Task CreateInvite_InvalidEmail_ReturnsFailure()
    {
        _productService.Setup(x => x.GetProductByIdAsync(10))
            .ReturnsAsync(new Product { Id = 10, VendorId = _employer.VendorId, Name = "AI Developer" });

        var result = await _mockAiController.CreateInvite("not-an-email", 10, 1, null);

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var success = (bool)json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var error = (string)json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(error, Is.EqualTo("Enter a valid email address."));
        _inviteService.Verify(x => x.CreateInviteAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime?>()), Times.Never);
    }

    [Test]
    public async Task CreateInvite_ServiceFailure_ReturnsFailureReason()
    {
        _productService.Setup(x => x.GetProductByIdAsync(10))
            .ReturnsAsync(new Product { Id = 10, VendorId = _employer.VendorId, Name = "AI Developer" });

        _inviteService.Setup(x => x.CreateInviteAsync(123, "invited@test.com", 10, 1, null))
            .ThrowsAsync(new NopException("Product is not owned by the sponsor."));

        var result = await _mockAiController.CreateInvite("invited@test.com", 10, 1, null);

        Assert.That(result, Is.TypeOf<JsonResult>());
        var json = (JsonResult)result;
        var success = (bool)json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var error = (string)json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(error, Is.EqualTo("Product is not owned by the sponsor."));
    }

    [Test]
    public async Task DeactivateInvite_Flow_Success()
    {
        var result = await _mockAiController.DeactivateInvite(1);

        _inviteService.Verify(x => x.DeactivateInviteAsync(1, 123), Times.Once);
        Assert.That(result, Is.TypeOf<JsonResult>());
    }

    [Test]
    public async Task CreateInvite_Unauthorized_ReturnsChallenge()
    {
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 456, VendorId = 0 });
        _customerService.Setup(x => x.IsAdminAsync(It.IsAny<Customer>())).ReturnsAsync(false);
        var result = await _mockAiController.CreateInvite("invited@test.com", 10, 1, null);
        Assert.That(result, Is.TypeOf<ChallengeResult>());
    }

    [Test]
    public async Task VendorScoreboard_ReturnsAggregatedMetrics()
    {
        var applications = new PagedList<JobApplication>(new List<JobApplication>
        {
            new() { Id = 10, CustomerId = 789, ProductId = 20, Status = JobApplicationStatuses.Shortlisted, CreatedOnUtc = DateTime.UtcNow }
        }, 0, int.MaxValue);
        _applicationService.Setup(x => x.GetApplicationsAsync(null, null, null, null, null, null, 0, 1, 0, int.MaxValue, false))
            .ReturnsAsync(applications);
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(789))
            .ReturnsAsync(new List<InterviewSession>
            {
                new() { JobApplicationId = 10, ProductId = 20, Score = 88, CompletedOnUtc = DateTime.UtcNow }
            });

        var result = await _controller.VendorScoreboard();

        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = (VendorScoreboardModel)((ViewResult)result).Model;
        Assert.Multiple(() =>
        {
            Assert.That(model.TotalApplications, Is.EqualTo(1));
            Assert.That(model.CompletedInterviews, Is.EqualTo(1));
            Assert.That(model.ShortlistedApplications, Is.EqualTo(1));
            Assert.That(model.ActiveFlaggedViolations, Is.EqualTo(0));
            Assert.That(model.AverageScore, Is.EqualTo(88));
            Assert.That(model.HighestScore, Is.EqualTo(88));
        });
    }

    [Test]
    public async Task VendorJobCreation_CreatesVendorOwnedProduct()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<Nop.Core.Domain.Catalog.ProductTemplate>
            {
                new() { Id = 1, Name = "Simple product", ViewPath = "ProductTemplate.Simple" },
                new() { Id = 7, Name = AIInterviewDefaults.JobProductTemplateName, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath }
            });
        SetupVendorSpecificationAttributes();
        _urlRecordService.Setup(x => x.ValidateSeNameAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>(), string.Empty, "Platform Engineer", true))
            .ReturnsAsync("platform-engineer");

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            ShortDescription = "Build reliable systems",
            Published = true
        });

        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        Assert.That(((RedirectToRouteResult)result).RouteName, Is.EqualTo(AIInterviewDefaults.VendorScoreboardRouteName));
        _productService.Verify(x => x.InsertProductAsync(It.Is<Nop.Core.Domain.Catalog.Product>(product =>
            product.Name == "Platform Engineer" &&
            product.VendorId == _employer.VendorId &&
            product.ProductTemplateId == 7 &&
            product.Published &&
            product.DisableBuyButton)), Times.Once);
        _jobRequirementService.Verify(x => x.SaveRequirementsAsync(It.Is<Nop.Core.Domain.Catalog.Product>(product =>
            product.Name == "Platform Engineer"), false, false, 0m, 3), Times.Once);
        _urlRecordService.Verify(x => x.SaveSlugAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>(), "platform-engineer", 0), Times.Once);
    }

    [Test]
    public async Task VendorJobCreation_Rejects_Empty_Name_And_Does_Not_Insert_Product()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate> { new() { Id = 7, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath } });
        SetupVendorSpecificationAttributes();

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "   "
        });

        Assert.That(result, Is.TypeOf<ViewResult>());
        _productService.Verify(x => x.InsertProductAsync(It.IsAny<Product>()), Times.Never);
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task VendorJobCreation_Saves_Checked_Requirement_Combinations(bool resumeRequired, bool interviewRequired)
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<Nop.Core.Domain.Catalog.ProductTemplate>
            {
                new() { Id = 1, Name = "Simple product", ViewPath = "ProductTemplate.Simple" },
                new() { Id = 7, Name = AIInterviewDefaults.JobProductTemplateName, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath }
            });
        SetupVendorSpecificationAttributes();
        _urlRecordService.Setup(x => x.ValidateSeNameAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>(), string.Empty, "Platform Engineer", true))
            .ReturnsAsync("platform-engineer");

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            ShortDescription = "Build reliable systems",
            Published = true,
            ResumeRequired = resumeRequired,
            InterviewRequired = interviewRequired,
            MinimumScore = 82,
            QuestionCount = 5
        });

        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        Assert.That(((RedirectToRouteResult)result).RouteName, Is.EqualTo(AIInterviewDefaults.VendorScoreboardRouteName));
        _productService.Verify(x => x.InsertProductAsync(It.Is<Nop.Core.Domain.Catalog.Product>(product =>
            product.Name == "Platform Engineer" &&
            product.VendorId == _employer.VendorId &&
            product.ProductTemplateId == 7 &&
            product.Published &&
            product.DisableBuyButton)), Times.Once);
        _jobRequirementService.Verify(x => x.SaveRequirementsAsync(It.Is<Nop.Core.Domain.Catalog.Product>(product =>
            product.Name == "Platform Engineer"), resumeRequired, interviewRequired, 0m, 3), Times.Once);
        _urlRecordService.Verify(x => x.SaveSlugAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>(), "platform-engineer", 0), Times.Once);
    }

    [Test]
    public async Task VendorJobCreation_Ignores_Posted_QuestionCount_When_InterviewRequired()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate>
            {
                new() { Id = 1, Name = "Simple product", ViewPath = "ProductTemplate.Simple" },
                new() { Id = 7, Name = AIInterviewDefaults.JobProductTemplateName, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath }
            });
        SetupVendorSpecificationAttributes();
        _urlRecordService.Setup(x => x.ValidateSeNameAsync(It.IsAny<Product>(), string.Empty, "Platform Engineer", true))
            .ReturnsAsync("platform-engineer");

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            InterviewRequired = true,
            QuestionCount = 11
        });

        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        _jobRequirementService.Verify(x => x.SaveRequirementsAsync(It.IsAny<Product>(), false, true, 0m, 3), Times.Once);
    }

    [TestCase(-1)]
    [TestCase(101)]
    public async Task VendorJobCreation_Ignores_Posted_MinimumScore(decimal minimumScore)
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate>
            {
                new() { Id = 1, Name = "Simple product", ViewPath = "ProductTemplate.Simple" },
                new() { Id = 7, Name = AIInterviewDefaults.JobProductTemplateName, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath }
            });
        SetupVendorSpecificationAttributes();
        _urlRecordService.Setup(x => x.ValidateSeNameAsync(It.IsAny<Product>(), string.Empty, "Platform Engineer", true))
            .ReturnsAsync("platform-engineer");

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            InterviewRequired = true,
            MinimumScore = minimumScore,
            QuestionCount = 5
        });

        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        _jobRequirementService.Verify(x => x.SaveRequirementsAsync(It.IsAny<Product>(), false, true, 0m, 3), Times.Once);
    }

    [Test]
    public async Task VendorJobCreation_InterviewNotRequired_Normalizes_Requirements_Before_Save()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate> { new() { Id = 7, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath } });
        SetupVendorSpecificationAttributes();
        _urlRecordService.Setup(x => x.ValidateSeNameAsync(It.IsAny<Product>(), string.Empty, "Platform Engineer", true))
            .ReturnsAsync("platform-engineer");

        await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            InterviewRequired = false,
            MinimumScore = 91,
            QuestionCount = 8
        });

        _jobRequirementService.Verify(x => x.SaveRequirementsAsync(It.IsAny<Product>(), false, false, 0m, 3), Times.Once);
    }

    [Test]
    public async Task VendorJobCreation_Always_Persists_Default_Interview_Settings()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate>
            {
                new() { Id = 1, Name = "Simple product", ViewPath = "ProductTemplate.Simple" },
                new() { Id = 7, Name = AIInterviewDefaults.JobProductTemplateName, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath }
            });
        SetupVendorSpecificationAttributes();
        _urlRecordService.Setup(x => x.ValidateSeNameAsync(It.IsAny<Product>(), string.Empty, "Platform Engineer", true))
            .ReturnsAsync("platform-engineer");

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            ResumeRequired = true,
            InterviewRequired = true,
            MinimumScore = 67,
            QuestionCount = 9
        });

        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        _jobRequirementService.Verify(x => x.SaveRequirementsAsync(It.IsAny<Product>(), true, true, 0m, 3), Times.Once);
    }

    [Test]
    public async Task VendorJobCreation_Trims_Text_Fields_Before_Saving()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate> { new() { Id = 7, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath } });
        SetupVendorSpecificationAttributes();
        _urlRecordService.Setup(x => x.ValidateSeNameAsync(It.IsAny<Product>(), string.Empty, "Platform Engineer", true))
            .ReturnsAsync("platform-engineer");

        await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "  Platform Engineer  ",
            Sku = "  REF-1  ",
            ShortDescription = "  Summary  ",
            FullDescription = "  Description  "
        });

        _productService.Verify(x => x.InsertProductAsync(It.Is<Product>(product =>
            product.Name == "Platform Engineer" &&
            product.Sku == "REF-1" &&
            product.ShortDescription == "Summary" &&
            product.FullDescription == "Description")), Times.Once);
    }

    [Test]
    public async Task VendorJobCreation_Saves_ApplyUntil_As_End_Of_Day_Utc()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate> { new() { Id = 7, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath } });
        SetupVendorSpecificationAttributes();
        _urlRecordService.Setup(x => x.ValidateSeNameAsync(It.IsAny<Product>(), string.Empty, "Platform Engineer", true))
            .ReturnsAsync("platform-engineer");
        var applyUntil = DateTime.UtcNow.Date.AddDays(7);

        await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            ApplyUntilUtc = applyUntil
        });

        _productService.Verify(x => x.InsertProductAsync(It.Is<Product>(product =>
            product.AvailableEndDateTimeUtc == applyUntil.Date.AddDays(1).AddTicks(-1))), Times.Once);
    }

    [Test]
    public async Task VendorJobCreation_Rejects_Past_ApplyUntil_Date()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate> { new() { Id = 7, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath } });
        SetupVendorSpecificationAttributes();

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            ApplyUntilUtc = DateTime.UtcNow.Date.AddDays(-1)
        });

        Assert.That(result, Is.TypeOf<ViewResult>());
        _productService.Verify(x => x.InsertProductAsync(It.IsAny<Product>()), Times.Never);
    }

    [Test]
    public async Task VendorJobCreation_Rejects_Invalid_Specification_Option_Id_And_Repopulates_Dropdowns()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate> { new() { Id = 7, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath } });
        SetupVendorSpecificationAttributes();
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributeOptionByIdAsync(999))
            .ReturnsAsync(new SpecificationAttributeOption { Id = 999, SpecificationAttributeId = 99, Name = "Invalid" });

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            ExperienceLevelOptionId = 999
        });

        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = (VendorJobModel)((ViewResult)result).Model;
        Assert.That(model.AvailableExperienceLevels, Is.Not.Empty);
        _productService.Verify(x => x.InsertProductAsync(It.IsAny<Product>()), Times.Never);
    }

    [Test]
    public async Task VendorJobCreation_Rejects_Metadata_When_Unsupported()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate> { new() { Id = 7, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath } });
        SetupVendorSpecificationAttributes(includeJobLocation: false, includeSalaryRange: false);

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            JobLocationOptionId = 3,
            SalaryRange = "80k-90k"
        });

        Assert.That(result, Is.TypeOf<ViewResult>());
        _productService.Verify(x => x.InsertProductAsync(It.IsAny<Product>()), Times.Never);
    }

    [Test]
    public void VendorJobCreation_View_Removes_Public_MinimumScore_And_QuestionCount_Controls()
    {
        var viewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "VendorJobCreation.cshtml"));

        Assert.That(viewText, Does.Contain("vendor-job-posting-shell"));
        Assert.That(viewText, Does.Contain("vendor-job-form-simple-grid"));
        Assert.That(viewText, Does.Contain("vendor-job-form-field"));
        Assert.That(viewText, Does.Contain("vendor-job-form-checkbox-list"));
        Assert.That(viewText, Does.Contain("vendor-job-form-actions"));
        Assert.That(viewText, Does.Not.Contain("vendor-job-form-row"));
        Assert.That(viewText, Does.Not.Contain("vendor-job-form-label"));
        Assert.That(viewText, Does.Not.Contain("aiinterview-minimum-score-row"));
        Assert.That(viewText, Does.Not.Contain("aiinterview-question-count-row"));
        Assert.That(viewText, Does.Not.Contain("MinimumScoreHidden"));
        Assert.That(viewText, Does.Not.Contain("QuestionCountHidden"));
        Assert.That(viewText, Does.Not.Contain("const minimumScoreRow = document.querySelector('.aiinterview-minimum-score-row');"));
        Assert.That(viewText, Does.Not.Contain("const questionCountRow = document.querySelector('.aiinterview-question-count-row');"));
        Assert.That(viewText, Does.Not.Contain("document.getElementById('MinimumScore')"));
        Assert.That(viewText, Does.Not.Contain("document.getElementById('QuestionCount')"));
        Assert.That(viewText, Does.Not.Contain("Apply Until:"));
        Assert.That(viewText, Does.Not.Contain("Experience Level:"));
        Assert.That(viewText, Does.Not.Contain("Work Mode:"));
        Assert.That(viewText, Does.Not.Contain("Employment Type:"));
        Assert.That(viewText, Does.Not.Contain("Job Location:"));
        Assert.That(viewText, Does.Not.Contain("Salary Range:"));
    }

    [Test]
    public void VendorAndEmployerViews_Use_Standard_Nop_Page_Structure()
    {
        var vendorJobCreation = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "VendorJobCreation.cshtml"));
        var employerApplications = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "EmployerApplications.cshtml"));
        var vendorScoreboard = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "VendorScoreboard.cshtml"));
        var employerManage = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "EmployerManage.cshtml"));
        var historyView = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "History.cshtml"));

        Assert.That(vendorJobCreation, Does.Contain("Layout = \"_ColumnsTwo\""));
        Assert.That(vendorJobCreation, Does.Contain("class=\"section\""));
        Assert.That(vendorJobCreation, Does.Contain("class=\"fieldset\""));

        Assert.That(employerApplications, Does.Contain("Layout = \"_ColumnsTwo\""));
        Assert.That(employerApplications, Does.Contain("class=\"section\""));
        Assert.That(employerApplications, Does.Contain("class=\"fieldset\""));
        Assert.That(employerApplications, Does.Contain("class=\"table-wrapper\""));
        Assert.That(employerApplications, Does.Contain("class=\"data-table employer-table\""));
        Assert.That(employerApplications, Does.Contain("js-open-report-drawer"));
        Assert.That(employerApplications, Does.Contain("data-report-panel-url"));
        Assert.That(employerApplications, Does.Contain("data-report-title"));
        Assert.That(employerApplications, Does.Contain("_CandidateReportDrawer.cshtml"));
        Assert.That(employerApplications, Does.Contain("class=\"status-form employer-status-form\""));
        Assert.That(employerApplications, Does.Contain("class=\"employer-status-select\""));
        Assert.That(employerApplications, Does.Contain("class=\"employer-status-comment\""));
        Assert.That(employerApplications, Does.Contain("class=\"button-2 employer-status-update\""));
        Assert.That(employerApplications, Does.Not.Contain("Admin.Common.PageSize"));
        Assert.That(employerApplications, Does.Not.Contain("Admin.Common.Reset"));
        Assert.That(employerApplications, Does.Not.Contain("Admin.Common.All"));
        Assert.That(employerApplications, Does.Not.Contain("Admin.Customers.Customers.Fields.Phone"));
        Assert.That(employerApplications, Does.Contain("Plugins.Misc.AIInterview.Employer.Applications.All"));
        Assert.That(employerApplications, Does.Contain("Plugins.Misc.AIInterview.Employer.Applications.Phone"));

        Assert.That(vendorScoreboard, Does.Contain("Layout = \"_ColumnsTwo\""));
        Assert.That(vendorScoreboard, Does.Contain("class=\"section scoreboard-deck-shell\""));
        Assert.That(vendorScoreboard, Does.Contain("Plugins.Misc.AIInterview.VendorScoreboard.Title"));
        Assert.That(vendorScoreboard, Does.Contain("Plugins.Misc.AIInterview.VendorScoreboard.Eyebrow"));
        Assert.That(vendorScoreboard, Does.Contain("html-aiinterview-scoreboard-page"));
        Assert.That(vendorScoreboard, Does.Contain("class=\"table-wrapper scoreboard-deck-table-wrapper\""));
        Assert.That(vendorScoreboard, Does.Contain("class=\"data-table scoreboard-deck-table\""));
        Assert.That(vendorScoreboard, Does.Contain("Plugins.Misc.AIInterview.VendorScoreboard.TotalCompletedAssessments"));
        Assert.That(vendorScoreboard, Does.Contain("Plugins.Misc.AIInterview.VendorScoreboard.AverageAnalyticalScore"));
        Assert.That(vendorScoreboard, Does.Contain("Plugins.Misc.AIInterview.VendorScoreboard.ActiveFlaggedViolations"));
        Assert.That(vendorScoreboard, Does.Contain("Plugins.Misc.AIInterview.VendorScoreboard.CandidateAssessmentMatrix"));
        Assert.That(vendorScoreboard, Does.Contain("scoreboard-deck-status"));
        Assert.That(vendorScoreboard, Does.Not.Contain(">Employer Scoreboard<"));
        Assert.That(vendorScoreboard, Does.Not.Contain("<h2 class=\"scoreboard-deck-title\">@titleText</h2>"));
        Assert.That(vendorScoreboard, Does.Contain("Plugins.Misc.AIInterview.VendorScoreboard.AssessmentWorkflow"));

        Assert.That(employerManage, Does.Contain("Layout = \"_ColumnsTwo\""));
        Assert.That(employerManage, Does.Contain("class=\"section create-invite\""));
        Assert.That(employerManage, Does.Contain("class=\"fieldset\""));
        Assert.That(employerManage, Does.Contain("class=\"table-wrapper\""));
        Assert.That(employerManage, Does.Contain("label for=\"invite-email\""));
        Assert.That(employerManage, Does.Contain("label for=\"productId\""));
        Assert.That(employerManage, Does.Contain("Plugins.Misc.AIInterview.Employer.Invite.Title"));
        Assert.That(employerManage, Does.Contain("Plugins.Misc.AIInterview.Employer.Invite.CreateTitle"));
        Assert.That(employerManage, Does.Contain("Plugins.Misc.AIInterview.Employer.Invite.ActiveTitle"));
        Assert.That(employerManage, Does.Contain("ViewBag.CreditBalanceDisplay"));
        Assert.That(employerManage, Does.Contain("class=\"invite-deactivate-form\""));
        Assert.That(employerManage, Does.Contain("class=\"button-2 invite-deactivate-button\""));

        Assert.That(historyView, Does.Contain("Plugins.Misc.AIInterview.Report.ViewReport"));
        Assert.That(historyView, Does.Contain("fa fa-eye"));
        Assert.That(historyView, Does.Contain("ai-view-report-link"));
        Assert.That(historyView, Does.Not.Contain("@T(\"Plugins.Misc.AIInterview.Report.OpenReport\")"));
    }

    [Test]
    public async Task ToggleSavedJob_Adds_Default_Wishlist_Item_Without_Duplicates()
    {
        var product = new Product { Id = 44, Name = "AI Role" };
        var wishlistItems = new List<ShoppingCartItem>();

        _productService.Setup(x => x.GetProductByIdAsync(44)).ReturnsAsync(product);
        _shoppingCartService.Setup(x => x.GetShoppingCartAsync(_employer, ShoppingCartType.Wishlist, 1, 44, null, null, 0))
            .ReturnsAsync(() => wishlistItems.ToList());
        _shoppingCartService.Setup(x => x.GetShoppingCartAsync(_employer, ShoppingCartType.Wishlist, 1, null, null, null, 0))
            .ReturnsAsync(() => wishlistItems.ToList());
        _shoppingCartService.Setup(x => x.AddToCartAsync(_employer, product, ShoppingCartType.Wishlist, 1, null, 0m, null, null, 1, true, null))
            .ReturnsAsync(new List<string>())
            .Callback(() =>
            {
                if (!wishlistItems.Any(item => item.ProductId == 44))
                {
                    wishlistItems.Add(new ShoppingCartItem
                    {
                        Id = 700,
                        ProductId = 44,
                        ShoppingCartType = ShoppingCartType.Wishlist,
                        StoreId = 1,
                        Quantity = 1
                    });
                }
            });

        var firstResult = (JsonResult)await _controller.ToggleSavedJob(44, true);
        var secondResult = (JsonResult)await _controller.ToggleSavedJob(44, true);

        Assert.That(firstResult.Value.GetType().GetProperty("success")?.GetValue(firstResult.Value), Is.EqualTo(true));
        Assert.That(firstResult.Value.GetType().GetProperty("isSaved")?.GetValue(firstResult.Value), Is.EqualTo(true));
        Assert.That(secondResult.Value.GetType().GetProperty("wishlistItemId")?.GetValue(secondResult.Value), Is.EqualTo(700));
        Assert.That(wishlistItems.Count(item => item.ProductId == 44), Is.EqualTo(1));
        _shoppingCartService.Verify(x => x.AddToCartAsync(_employer, product, ShoppingCartType.Wishlist, 1, null, 0m, null, null, 1, true, null), Times.Once);
    }

    [Test]
    public async Task ToggleSavedJob_Removes_Existing_Default_Wishlist_Items()
    {
        var product = new Product { Id = 55, Name = "Saved Role" };
        var wishlistItems = new List<ShoppingCartItem>
        {
            new() { Id = 801, ProductId = 55, ShoppingCartType = ShoppingCartType.Wishlist, StoreId = 1, Quantity = 1 },
            new() { Id = 802, ProductId = 55, ShoppingCartType = ShoppingCartType.Wishlist, StoreId = 1, Quantity = 1 }
        };

        _productService.Setup(x => x.GetProductByIdAsync(55)).ReturnsAsync(product);
        _shoppingCartService.Setup(x => x.GetShoppingCartAsync(_employer, ShoppingCartType.Wishlist, 1, 55, null, null, 0))
            .ReturnsAsync(() => wishlistItems.Where(item => item.ProductId == 55).ToList());
        _shoppingCartService.Setup(x => x.GetShoppingCartAsync(_employer, ShoppingCartType.Wishlist, 1, null, null, null, 0))
            .ReturnsAsync(() => wishlistItems.ToList());
        _shoppingCartService.Setup(x => x.DeleteShoppingCartItemAsync(It.IsAny<ShoppingCartItem>(), true, It.IsAny<bool>()))
            .Callback<ShoppingCartItem, bool, bool>((item, _, _) => wishlistItems.RemoveAll(existing => existing.Id == item.Id))
            .Returns(Task.CompletedTask);

        var result = (JsonResult)await _controller.ToggleSavedJob(55, false);

        Assert.That(result.Value.GetType().GetProperty("success")?.GetValue(result.Value), Is.EqualTo(true));
        Assert.That(result.Value.GetType().GetProperty("isSaved")?.GetValue(result.Value), Is.EqualTo(false));
        _shoppingCartService.Verify(x => x.DeleteShoppingCartItemAsync(It.IsAny<ShoppingCartItem>(), true, It.IsAny<bool>()), Times.Exactly(2));
    }

    [Test]
    public async Task ToggleSavedJob_MissingProduct_ReturnsLocalizedMessage()
    {
        _productService.Setup(x => x.GetProductByIdAsync(999)).ReturnsAsync((Product)null);
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.JobNotFound"))
            .ReturnsAsync("Localized job missing");

        var result = (JsonResult)await _controller.ToggleSavedJob(999, true);

        Assert.That(result.Value.GetType().GetProperty("success")?.GetValue(result.Value), Is.EqualTo(false));
        Assert.That(result.Value.GetType().GetProperty("message")?.GetValue(result.Value), Is.EqualTo("Localized job missing"));
    }

    [Test]
    public async Task ToggleSavedJob_NonAiProduct_ReturnsLocalizedInvalidJobMessage()
    {
        var product = new Product { Id = 77, Name = "Regular Product" };

        _productService.Setup(x => x.GetProductByIdAsync(77)).ReturnsAsync(product);
        _jobRequirementService.Setup(x => x.IsJobProductAsync(product)).ReturnsAsync(false);
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.InvalidJob"))
            .ReturnsAsync("Localized invalid job");

        var result = (JsonResult)await _controller.ToggleSavedJob(77, true);

        Assert.That(result.Value.GetType().GetProperty("success")?.GetValue(result.Value), Is.EqualTo(false));
        Assert.That(result.Value.GetType().GetProperty("message")?.GetValue(result.Value), Is.EqualTo("Localized invalid job"));
        _shoppingCartService.Verify(x => x.DeleteShoppingCartItemAsync(It.IsAny<ShoppingCartItem>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Test]
    public async Task ToggleSavedJob_UnavailableServices_ReturnsLocalizedUnavailableMessage()
    {
        var controller = new AIInterviewController(
            _applicationService.Object,
            _interviewSessionService.Object,
            new AIInterviewSettings { Enabled = true },
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object,
            _jobRequirementService.Object,
            _productTemplateService.Object,
            _urlRecordService.Object,
            null,
            _specificationAttributeService.Object,
            null,
            null,
            null,
            null,
            _storeContext.Object);

        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.SavedJobsUnavailable"))
            .ReturnsAsync("Localized unavailable");

        var result = (JsonResult)await controller.ToggleSavedJob(44, true);

        Assert.That(result.Value.GetType().GetProperty("success")?.GetValue(result.Value), Is.EqualTo(false));
        Assert.That(result.Value.GetType().GetProperty("message")?.GetValue(result.Value), Is.EqualTo("Localized unavailable"));
    }

    [Test]
    public void JobCard_Rendering_Uses_Plugin_Component_And_Shared_Spec_Mapping()
    {
        var productBox = File.ReadAllText(Path.Combine(TestFilePathHelper.GetPluginRootPath(), "..", "..", "Presentation", "Nop.Web", "Themes", "JobBoardVenture", "Views", "Shared", "_ProductBox.cshtml"));
        var jobCardView = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "Components", "AIInterviewJobProductCard", "Default.cshtml"));
        var jobDetailView = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "ProductTemplate.JobDetails.cshtml"));
        var sharedJobDetailView = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_AIInterviewJobDetailsContent.cshtml"));
        var drawerView = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_AIInterviewJobDetailsDrawer.cshtml"));
        var cssText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Content", "css", "aiinterview-public.css"));
        var serviceText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Services", "AIInterviewJobDisplayService.cs"));
        var modelText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Models", "JobCardModels.cs"));
        var controllerText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Controllers", "AIInterviewController.cs"));

        Assert.That(productBox, Does.Contain("Component.InvokeAsync(\"AIInterviewJobProductCard\""));
        Assert.That(productBox, Does.Contain("if (!string.IsNullOrWhiteSpace(aiInterviewCardMarkup))"));
        Assert.That(productBox, Does.Contain("<article class=\"product-item\" data-productid=\"@Model.Id\">"));

        Assert.That(jobCardView, Does.Contain("ai-job-product-card"));
        Assert.That(jobCardView, Does.Contain("ai-job-card-title-link"));
        Assert.That(jobCardView, Does.Contain("ai-job-preview-drawer"));
        Assert.That(jobCardView, Does.Contain("data-drawer-url"));
        Assert.That(jobCardView, Does.Contain("ai-job-card-save"));
        Assert.That(jobCardView, Does.Contain("aria-pressed"));
        Assert.That(jobCardView, Does.Contain("fa-bookmark"));
        Assert.That(jobCardView, Does.Contain("data-toggle-url"));
        Assert.That(jobCardView, Does.Contain("<a class=\"ai-job-card-title-link\" href=\"@productUrl\">"));
        Assert.That(jobCardView, Does.Not.Contain("<a class=\"ai-job-card-title-link\" href=\"@Model.ProductUrl\">"));
        Assert.That(jobCardView, Does.Contain("class=\"button-2 ai-job-card-preview-trigger\""));
        Assert.That(jobCardView, Does.Contain("data-ai-job-preview-open=\"@drawerId\""));
        Assert.That(jobCardView, Does.Contain("href=\"@productUrl\""));
        Assert.That(jobCardView, Does.Not.Contain("href=\"#\""));
        Assert.That(jobCardView, Does.Not.Contain("@Model.PreviewDescription"));
        Assert.That(jobCardView, Does.Not.Contain("Prompt Source"));
        Assert.That(jobCardView, Does.Contain("data-loading-text=\"@loadingJobDetailsText\""));
        Assert.That(jobCardView, Does.Contain("data-error-text=\"@unableToLoadJobDetailsText\""));
        Assert.That(jobCardView, Does.Contain("data-product-url=\"@productUrl\""));
        Assert.That(jobCardView, Does.Contain("data-product-link-text=\"@viewJobLinkText\""));
        Assert.That(jobCardView, Does.Contain("T(\"Plugins.Misc.AIInterview.JobCard.LoadingJobDetails\")"));
        Assert.That(jobCardView, Does.Contain("T(\"Plugins.Misc.AIInterview.JobCard.UnableToLoadJobDetails\")"));

        Assert.That(jobDetailView, Does.Contain("_AIInterviewJobDetailsContent.cshtml"));
        Assert.That(jobDetailView, Does.Contain("@using Nop.Services.Helpers"));
        Assert.That(sharedJobDetailView, Does.Contain("@inject IAIInterviewJobDisplayService aiInterviewJobDisplayService"));
        Assert.That(sharedJobDetailView, Does.Contain("@using Nop.Services.Helpers"));
        Assert.That(sharedJobDetailView, Does.Contain("@using Nop.Web.Framework.Infrastructure"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.Kicker"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.HiringCompany"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.CandidatesApplied"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.ViewJob"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.EmailAFriend"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.SaveJob"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.SavedJob"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.SaveToCustomWishlist"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.JobDescription"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.RoleHighlights"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.RoleHighlightsFallback"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.Skills"));
        Assert.That(sharedJobDetailView, Does.Contain("Plugins.Misc.AIInterview.JobDetails.SkillsFallback"));
        Assert.That(jobCardView, Does.Contain("Model.PostedDateText"));
        Assert.That(jobCardView, Does.Not.Contain("ToString(\"MMM d, yyyy\")"));
        Assert.That(sharedJobDetailView, Does.Contain("var candidateCount = await applicationService.GetApplicationCountAsync(productId: Model.Id);"));
        Assert.That(sharedJobDetailView, Does.Contain("var candidatesAppliedText = string.Format(T(\"Plugins.Misc.AIInterview.JobDetails.CandidatesApplied\").Text, candidateCount);"));
        Assert.That(sharedJobDetailView, Does.Not.Contain("|| 'Saved job'"));
        Assert.That(sharedJobDetailView, Does.Not.Contain("|| \"Saved job\""));
        Assert.That(sharedJobDetailView, Does.Not.Contain(">AI Interview Role<"));
        Assert.That(sharedJobDetailView, Does.Not.Contain(">Hiring Company<"));
        Assert.That(sharedJobDetailView, Does.Not.Contain(">View job<"));
        Assert.That(sharedJobDetailView, Does.Not.Contain(">Email a friend<"));
        Assert.That(sharedJobDetailView, Does.Not.Contain(">Save to custom wishlist<"));
        Assert.That(sharedJobDetailView, Does.Not.Contain(">Job Description<"));
        Assert.That(sharedJobDetailView, Does.Not.Contain(">Role Highlights<"));
        Assert.That(sharedJobDetailView, Does.Not.Contain(">Skills<"));
        Assert.That(sharedJobDetailView, Does.Contain("aiInterviewJobDisplayService.IsCompactSpecificationAttributeName(specificationAttribute.Name ?? string.Empty)"));
        Assert.That(sharedJobDetailView, Does.Contain("GetSpecificationSnapshotAsync(Model.Id, Model.ProductSpecificationModel)"));
        Assert.That(sharedJobDetailView, Does.Contain("AIInterviewProductDetailsViewComponent"));
        Assert.That(drawerView, Does.Contain("_AIInterviewJobDetailsContent.cshtml"));
        Assert.That(controllerText, Does.Contain("JobDetailsDrawer(int productId)"));
        Assert.That(controllerText, Does.Contain("return NotFound(unavailableText);"));
        Assert.That(controllerText, Does.Contain("return BadRequest(unavailableText);"));
        Assert.That(controllerText, Does.Contain("InterviewReportPanelUrl = session != null ? BuildReportPanelUrl(session.Id) : null"));
        Assert.That(controllerText, Does.Contain("_AIInterviewJobDetailsDrawer.cshtml"));

        Assert.That(serviceText, Does.Contain("Workplace Type"));
        Assert.That(serviceText, Does.Contain("Job Type"));
        Assert.That(serviceText, Does.Contain("Office Location"));
        Assert.That(serviceText, Does.Contain("Pay Range"));
        Assert.That(serviceText, Does.Contain("Seniority Level"));
        Assert.That(serviceText, Does.Contain("PostedDateText = await FormatPostedDateAsync(product.CreatedOnUtc)"));
        Assert.That(serviceText, Does.Contain("var appliedCount = await _applicationService.GetApplicationCountAsync(productId: product.Id);"));
        Assert.That(serviceText, Does.Contain("NormalizeSpecificationAttributeName"));
        Assert.That(serviceText, Does.Contain("public bool IsCompactSpecificationAttributeName(string name)"));
        Assert.That(serviceText, Does.Not.Contain("ToString(\"D\", culture)"));
        Assert.That(serviceText, Does.Contain("ToString(\"d\", culture)"));
        Assert.That(modelText, Does.Contain("public string ProductUrl { get; set; }"));
        Assert.That(modelText, Does.Contain("public string PostedDateText { get; set; }"));
        Assert.That(serviceText, Does.Contain("ProductUrl = await ResolveProductUrlAsync(product)"));
        Assert.That(serviceText, Does.Contain("RouteGenericUrlAsync(product)"));
        Assert.That(controllerText, Does.Contain("GetExperienceLevelAttributeAliases() => AIInterviewJobDisplayService.ExperienceLevelAliases"));
        Assert.That(controllerText, Does.Contain("GetWorkArrangementAttributeAliases() => AIInterviewJobDisplayService.WorkArrangementAliases"));
        Assert.That(controllerText, Does.Contain("GetJobLocationAttributeAliases() => AIInterviewJobDisplayService.JobLocationAliases"));
        Assert.That(controllerText, Does.Contain("ResolveSalaryRangeSpecificationOptionIdAsync()"));

        Assert.That(cssText, Does.Contain(".ai-job-product-card"));
        Assert.That(cssText, Does.Contain("grid-template-columns: 84px minmax(0, 1fr);"));
        Assert.That(cssText, Does.Contain(".ai-job-card-summary"));
        Assert.That(cssText, Does.Contain(".ai-job-card-content,"));
        Assert.That(cssText, Does.Contain(".ai-job-card-meta,"));
        Assert.That(cssText, Does.Contain(".ai-job-card-spec {"));
        Assert.That(cssText, Does.Contain("min-width: 0;"));
        Assert.That(cssText, Does.Contain(".ai-job-card-spec span:first-child"));
        Assert.That(cssText, Does.Contain("flex: 0 0 auto;"));
        Assert.That(cssText, Does.Contain("max-width: 44%;"));
        Assert.That(cssText, Does.Contain("text-overflow: ellipsis;"));
        Assert.That(cssText, Does.Contain(".ai-job-card-spec span:last-child"));
        Assert.That(cssText, Does.Contain("flex: 1 1 auto;"));
        Assert.That(cssText, Does.Contain("-webkit-line-clamp: 2;"));
        Assert.That(cssText, Does.Contain("white-space: normal;"));
        Assert.That(cssText, Does.Contain(".ai-job-preview-fallback-link"));
        Assert.That(cssText, Does.Contain(".ai-job-preview-drawer"));
        Assert.That(cssText, Does.Contain("width: 50vw;"));
        Assert.That(cssText, Does.Contain("width: 88vw;"));
        Assert.That(cssText, Does.Contain("height: 100dvh;"));
        Assert.That(cssText, Does.Contain("rgba(15, 23, 42, 0.78)"));
        Assert.That(cssText, Does.Contain("backdrop-filter: blur(2px);"));
        Assert.That(cssText, Does.Contain(".ai-job-card-save.is-saved"));
        Assert.That(cssText, Does.Contain(".ai-job-card-preview-trigger"));
        Assert.That(cssText, Does.Contain(".vendor-job-posting-shell"));
        Assert.That(cssText, Does.Contain(".vendor-job-form-simple-grid"));
        Assert.That(cssText, Does.Contain("background: #20252b;"));
        Assert.That(cssText, Does.Contain(".ai-job-card-save[aria-pressed=\"true\"]"));
        Assert.That(cssText, Does.Contain(".invite-deactivate-button"));
        Assert.That(cssText, Does.Contain(".employer-status-form"));
    }

    private void SetupVendorSpecificationAttributes(bool includeJobLocation = true, bool includeSalaryRange = true)
    {
        var experience = new SpecificationAttribute { Id = 10, Name = "Experience Level" };
        var workMode = new SpecificationAttribute { Id = 11, Name = "Work Mode" };
        var employmentType = new SpecificationAttribute { Id = 12, Name = "Employment Type" };
        var jobLocation = new SpecificationAttribute { Id = 13, Name = "Job Location" };
        var salaryRange = new SpecificationAttribute { Id = 14, Name = "Salary Range" };
        var allAttributes = new List<SpecificationAttribute> { experience, workMode, employmentType, jobLocation, salaryRange };

        SetupSpecificationAttributeLookup("Experience Level", new List<SpecificationAttribute> { experience });
        SetupSpecificationAttributeLookup("Experience");
        SetupSpecificationAttributeLookup("Seniority");
        SetupSpecificationAttributeLookup("Seniority Level");
        SetupSpecificationAttributeLookup("Level");
        SetupSpecificationAttributeLookup("Work Mode", new List<SpecificationAttribute> { workMode });
        SetupSpecificationAttributeLookup("Work Arrangement");
        SetupSpecificationAttributeLookup("Work Type");
        SetupSpecificationAttributeLookup("Workplace Type");
        SetupSpecificationAttributeLookup("Workplace");
        SetupSpecificationAttributeLookup("Work Setup");
        SetupSpecificationAttributeLookup("Work Location Type");
        SetupSpecificationAttributeLookup("Remote Type");
        SetupSpecificationAttributeLookup("Employment Type", new List<SpecificationAttribute> { employmentType });
        SetupSpecificationAttributeLookup("Job Type");
        SetupSpecificationAttributeLookup("Contract Type");
        SetupSpecificationAttributeLookup("Employment Basis");
        SetupSpecificationAttributeLookup("Job Location", includeJobLocation ? new List<SpecificationAttribute> { jobLocation } : new List<SpecificationAttribute>());
        SetupSpecificationAttributeLookup("Location");
        SetupSpecificationAttributeLookup("Office Location");
        SetupSpecificationAttributeLookup("Work Location");
        SetupSpecificationAttributeLookup("City");
        SetupSpecificationAttributeLookup("Region");
        SetupSpecificationAttributeLookup("Salary Range", includeSalaryRange ? new List<SpecificationAttribute> { salaryRange } : new List<SpecificationAttribute>());
        SetupSpecificationAttributeLookup("Compensation");
        SetupSpecificationAttributeLookup("Pay Range");
        SetupSpecificationAttributeLookup("Salary");
        SetupSpecificationAttributeLookup("Compensation Range");

        _specificationAttributeService.Setup(x => x.GetAllSpecificationAttributesAsync(0, int.MaxValue))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(allAttributes, 0, allAttributes.Count));

        _specificationAttributeService.Setup(x => x.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(experience.Id))
            .ReturnsAsync(new List<SpecificationAttributeOption> { new() { Id = 101, SpecificationAttributeId = experience.Id, Name = "Senior" } });
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(workMode.Id))
            .ReturnsAsync(new List<SpecificationAttributeOption> { new() { Id = 201, SpecificationAttributeId = workMode.Id, Name = "Remote" } });
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(employmentType.Id))
            .ReturnsAsync(new List<SpecificationAttributeOption> { new() { Id = 301, SpecificationAttributeId = employmentType.Id, Name = "Full-time" } });
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(jobLocation.Id))
            .ReturnsAsync(new List<SpecificationAttributeOption> { new() { Id = 401, SpecificationAttributeId = jobLocation.Id, Name = "Value" } });
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(salaryRange.Id))
            .ReturnsAsync(new List<SpecificationAttributeOption> { new() { Id = 501, SpecificationAttributeId = salaryRange.Id, Name = "Value" } });

        _specificationAttributeService.Setup(x => x.GetSpecificationAttributeOptionByIdAsync(101))
            .ReturnsAsync(new SpecificationAttributeOption { Id = 101, SpecificationAttributeId = experience.Id, Name = "Senior" });
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributeOptionByIdAsync(201))
            .ReturnsAsync(new SpecificationAttributeOption { Id = 201, SpecificationAttributeId = workMode.Id, Name = "Remote" });
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributeOptionByIdAsync(301))
            .ReturnsAsync(new SpecificationAttributeOption { Id = 301, SpecificationAttributeId = employmentType.Id, Name = "Full-time" });
    }

    private void SetupSpecificationAttributeLookup(string name, IList<SpecificationAttribute> matches = null)
    {
        var results = matches?.ToList() ?? new List<SpecificationAttribute>();
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync(name, 0, 10))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(results, 0, results.Count));
    }
}
