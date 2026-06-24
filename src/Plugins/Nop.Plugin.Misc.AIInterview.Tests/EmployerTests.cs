using Microsoft.AspNetCore.Mvc;
using Moq;
using Nop.Core;
using Nop.Core.Domain.Customers;
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
using Nop.Services.Seo;
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
        _jobRequirementService.Setup(x => x.SaveRequirementsAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<decimal>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _downloadService = new Mock<IDownloadService>();
        _productTemplateService = new Mock<IProductTemplateService>();
        _urlRecordService = new Mock<IUrlRecordService>();
        _specificationAttributeService = new Mock<ISpecificationAttributeService>();

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
            _specificationAttributeService.Object);

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
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Attempts")).ReturnsAsync("Attempts");
        _localizationService.Setup(x => x.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.PromptSource")).ReturnsAsync("Prompt Source");

        var result = await _controller.ExportCsv(new ApplicationListModel());

        Assert.That(result, Is.TypeOf<FileContentResult>());
        var fileResult = (FileContentResult)result;
        var csv = System.Text.Encoding.UTF8.GetString(fileResult.FileContents);
        Assert.That(csv, Does.Contain("ID,Candidate,Email,Status,Score,Date"));
        Assert.That(csv, Does.Contain("Job Title"));
        Assert.That(csv, Does.Contain("Charge Mode"));
        Assert.That(csv, Does.Contain("Attempts"));
        Assert.That(csv, Does.Contain("Prompt Source"));
        Assert.That(csv, Does.Contain("1,\"John Doe\",\"john@example.com\""));
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
        var expectedMinimumScore = interviewRequired ? 82m : 0m;
        var expectedQuestionCount = interviewRequired ? 5 : 3;
        _jobRequirementService.Verify(x => x.SaveRequirementsAsync(It.Is<Nop.Core.Domain.Catalog.Product>(product =>
            product.Name == "Platform Engineer"), resumeRequired, interviewRequired, expectedMinimumScore, expectedQuestionCount), Times.Once);
        _urlRecordService.Verify(x => x.SaveSlugAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>(), "platform-engineer", 0), Times.Once);
    }

    [Test]
    public async Task VendorJobCreation_Invalid_QuestionCount_When_InterviewRequired_Does_Not_Insert_Product()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate> { new() { Id = 7, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath } });
        SetupVendorSpecificationAttributes();

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            InterviewRequired = true,
            QuestionCount = 11
        });

        Assert.That(result, Is.TypeOf<ViewResult>());
        _productService.Verify(x => x.InsertProductAsync(It.IsAny<Product>()), Times.Never);
    }

    [TestCase(-1)]
    [TestCase(101)]
    public async Task VendorJobCreation_Invalid_MinimumScore_Does_Not_Insert_Product(decimal minimumScore)
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate> { new() { Id = 7, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath } });
        SetupVendorSpecificationAttributes();

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            InterviewRequired = true,
            MinimumScore = minimumScore,
            QuestionCount = 5
        });

        Assert.That(result, Is.TypeOf<ViewResult>());
        _productService.Verify(x => x.InsertProductAsync(It.IsAny<Product>()), Times.Never);
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
    public async Task VendorJobCreation_Rejects_CustomText_Metadata_When_Unsupported()
    {
        _productTemplateService.Setup(x => x.GetAllProductTemplatesAsync())
            .ReturnsAsync(new List<ProductTemplate> { new() { Id = 7, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath } });
        SetupVendorSpecificationAttributes(includeJobLocation: false, includeSalaryRange: false);

        var result = await _controller.VendorJobCreation(new VendorJobModel
        {
            Name = "Platform Engineer",
            JobLocation = "London",
            SalaryRange = "80k-90k"
        });

        Assert.That(result, Is.TypeOf<ViewResult>());
        _productService.Verify(x => x.InsertProductAsync(It.IsAny<Product>()), Times.Never);
    }

    [Test]
    public void VendorJobCreation_View_Toggles_MinimumScore_And_QuestionCount_With_InterviewRequired()
    {
        var viewText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "VendorJobCreation.cshtml"));

        Assert.That(viewText, Does.Contain("vendor-job-form-row"));
        Assert.That(viewText, Does.Contain("vendor-job-form-label"));
        Assert.That(viewText, Does.Contain("vendor-job-form-control"));
        Assert.That(viewText, Does.Contain("vendor-job-form-checkbox-list"));
        Assert.That(viewText, Does.Contain("vendor-job-form-actions"));
        Assert.That(viewText, Does.Contain("vendor-job-form-row vendor-job-form-actions"));
        Assert.That(viewText, Does.Contain("aiinterview-minimum-score-row"));
        Assert.That(viewText, Does.Contain("aiinterview-question-count-row"));
        Assert.That(viewText, Does.Contain("const minimumScoreRow = document.querySelector('.aiinterview-minimum-score-row');"));
        Assert.That(viewText, Does.Contain("const questionCountRow = document.querySelector('.aiinterview-question-count-row');"));
        Assert.That(viewText, Does.Contain("minimumScoreInput.disabled = !enabled;"));
        Assert.That(viewText, Does.Contain("questionCountInput.disabled = !enabled;"));
        Assert.That(viewText, Does.Contain("minimumScoreHidden.disabled = enabled;"));
        Assert.That(viewText, Does.Contain("questionCountHidden.disabled = enabled;"));
        Assert.That(viewText, Does.Contain("minimumScoreRow.style.display = enabled ? '' : 'none';"));
        Assert.That(viewText, Does.Contain("questionCountRow.style.display = enabled ? '' : 'none';"));
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

        Assert.That(vendorJobCreation, Does.Contain("Layout = \"_ColumnsTwo\""));
        Assert.That(vendorJobCreation, Does.Contain("class=\"section\""));
        Assert.That(vendorJobCreation, Does.Contain("class=\"fieldset\""));

        Assert.That(employerApplications, Does.Contain("Layout = \"_ColumnsTwo\""));
        Assert.That(employerApplications, Does.Contain("class=\"section\""));
        Assert.That(employerApplications, Does.Contain("class=\"fieldset\""));
        Assert.That(employerApplications, Does.Contain("class=\"table-wrapper\""));
        Assert.That(employerApplications, Does.Contain("class=\"data-table employer-table\""));

        Assert.That(vendorScoreboard, Does.Contain("Layout = \"_ColumnsTwo\""));
        Assert.That(vendorScoreboard, Does.Contain("class=\"section scoreboard-deck-shell\""));
        Assert.That(vendorScoreboard, Does.Contain("Employer Scoreboard"));
        Assert.That(vendorScoreboard, Does.Contain("Recruitment Analytics Desk"));
        Assert.That(vendorScoreboard, Does.Contain("html-aiinterview-scoreboard-page"));
        Assert.That(vendorScoreboard, Does.Contain("class=\"table-wrapper scoreboard-deck-table-wrapper\""));
        Assert.That(vendorScoreboard, Does.Contain("class=\"data-table scoreboard-deck-table\""));
        Assert.That(vendorScoreboard, Does.Contain("Total Completed Assessments"));
        Assert.That(vendorScoreboard, Does.Contain("Average Analytical Score"));
        Assert.That(vendorScoreboard, Does.Contain("Active Flagged Violations"));
        Assert.That(vendorScoreboard, Does.Contain("Candidate Assessment Matrix"));
        Assert.That(vendorScoreboard, Does.Contain("scoreboard-deck-status"));
        Assert.That(vendorScoreboard, Does.Not.Contain("Vendor Dashboard"));

        Assert.That(employerManage, Does.Contain("Layout = \"_ColumnsTwo\""));
        Assert.That(employerManage, Does.Contain("class=\"section create-invite\""));
        Assert.That(employerManage, Does.Contain("class=\"fieldset\""));
        Assert.That(employerManage, Does.Contain("class=\"table-wrapper\""));
        Assert.That(employerManage, Does.Contain("label for=\"invite-email\""));
        Assert.That(employerManage, Does.Contain("label for=\"productId\""));
    }

    private void SetupVendorSpecificationAttributes(bool includeJobLocation = true, bool includeSalaryRange = true)
    {
        var experience = new SpecificationAttribute { Id = 10, Name = "Experience Level" };
        var workMode = new SpecificationAttribute { Id = 11, Name = "Work Mode" };
        var employmentType = new SpecificationAttribute { Id = 12, Name = "Employment Type" };
        var jobLocation = new SpecificationAttribute { Id = 13, Name = "Job Location" };
        var salaryRange = new SpecificationAttribute { Id = 14, Name = "Salary Range" };

        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync("Experience Level", 0, 1))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(new List<SpecificationAttribute> { experience }, 0, 1));
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync("Experience", 0, 1))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(new List<SpecificationAttribute>(), 0, 1));
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync("Work Mode", 0, 1))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(new List<SpecificationAttribute> { workMode }, 0, 1));
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync("Work Arrangement", 0, 1))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(new List<SpecificationAttribute>(), 0, 1));
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync("Work Type", 0, 1))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(new List<SpecificationAttribute>(), 0, 1));
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync("Employment Type", 0, 1))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(new List<SpecificationAttribute> { employmentType }, 0, 1));
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync("Job Location", 0, 1))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(includeJobLocation ? new List<SpecificationAttribute> { jobLocation } : new List<SpecificationAttribute>(), 0, 1));
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync("Location", 0, 1))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(new List<SpecificationAttribute>(), 0, 1));
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync("Salary Range", 0, 1))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(includeSalaryRange ? new List<SpecificationAttribute> { salaryRange } : new List<SpecificationAttribute>(), 0, 1));
        _specificationAttributeService.Setup(x => x.GetSpecificationAttributesByNameAsync("Compensation", 0, 1))
            .ReturnsAsync(new PagedList<SpecificationAttribute>(new List<SpecificationAttribute>(), 0, 1));

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
}
