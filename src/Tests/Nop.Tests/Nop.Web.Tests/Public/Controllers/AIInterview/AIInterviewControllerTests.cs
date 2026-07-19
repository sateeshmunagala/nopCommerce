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
using Nop.Services.Orders;
using Nop.Services.Stores;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using NUnit.Framework;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Stores;
using Nop.Web.Factories;
using Nop.Web.Models.Catalog;

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
    private Mock<IShoppingCartService> _shoppingCartService;
    private Mock<IStoreContext> _storeContext;
    private Mock<IProductModelFactory> _productModelFactory;
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
        _shoppingCartService = new Mock<IShoppingCartService>();
        _storeContext = new Mock<IStoreContext>();
        _productModelFactory = new Mock<IProductModelFactory>();
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<int>()))
            .ReturnsAsync(new JobRequirementsModel());
        _aiInterviewSettings = new AIInterviewSettings { Enabled = true };

        _customer = new Customer { Id = 123, Email = "candidate@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(_customer);
        _workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new global::Nop.Core.Domain.Localization.Language { Id = 1 });
        _customerService.Setup(x => x.IsRegisteredAsync(_customer)).ReturnsAsync(true);

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
            shoppingCartService: _shoppingCartService.Object,
            storeContext: _storeContext.Object,
            productModelFactory: _productModelFactory.Object);
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

    [Test]
    public async Task MyActivity_SavedJobs_Filters_Deduplicates_And_Uses_Latest_Wishlist_Order()
    {
        // Arrange
        var store = new Store { Id = 7 };
        var olderDate = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        var newerDate = olderDate.AddDays(5);
        var middleDate = olderDate.AddDays(2);
        var wishlistItems = new List<ShoppingCartItem>
        {
            new() { ProductId = 1, CreatedOnUtc = olderDate },
            new() { ProductId = 1, CreatedOnUtc = newerDate },
            new() { ProductId = 2, CreatedOnUtc = middleDate },
            new() { ProductId = 3, CreatedOnUtc = middleDate.AddHours(-1) },
            new() { ProductId = 4, CreatedOnUtc = middleDate.AddHours(-2) }
        };
        var products = new List<Product>
        {
            new() { Id = 1, Name = "Newest saved job", Published = true, Deleted = false },
            new() { Id = 2, Name = "Catalog item", Published = true, Deleted = false },
            new() { Id = 3, Name = "Deleted job", Published = true, Deleted = true },
            new() { Id = 4, Name = "Hidden job", Published = false, Deleted = false }
        };

        _storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);
        _shoppingCartService.Setup(x => x.GetShoppingCartAsync(_customer, ShoppingCartType.Wishlist, store.Id, null, null, null, 0))
            .ReturnsAsync(wishlistItems);
        _productService.Setup(x => x.GetProductsByIdsAsync(It.IsAny<int[]>()))
            .ReturnsAsync((int[] ids) => products.Where(product => ids.Contains(product.Id)).ToList());
        _jobRequirementService.Setup(x => x.IsJobProductAsync(It.Is<Product>(product => product.Id == 1))).ReturnsAsync(true);
        _jobRequirementService.Setup(x => x.IsJobProductAsync(It.Is<Product>(product => product.Id == 2))).ReturnsAsync(false);
        _jobRequirementService.Setup(x => x.IsJobProductAsync(It.Is<Product>(product => product.Id == 3))).ReturnsAsync(true);
        _jobRequirementService.Setup(x => x.IsJobProductAsync(It.Is<Product>(product => product.Id == 4))).ReturnsAsync(true);
        _productModelFactory.Setup(x => x.PrepareProductOverviewModelsAsync(It.IsAny<IEnumerable<Product>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((IEnumerable<Product> selectedProducts, bool preparePriceModel, bool preparePictureModel, int? productThumbPictureSize, bool prepareSpecificationAttributes, bool forceRedirectionAfterAddingToCart) => selectedProducts
                .Select(product => new ProductOverviewModel { Id = product.Id, Name = product.Name })
                .ToList());

        // Act
        var result = await _controller.MyActivity(AIInterviewDefaults.MyActivitySavedJobsTabKey);

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = (MyActivityPageModel)((ViewResult)result).Model;
        Assert.That(model.ActiveTab, Is.EqualTo(AIInterviewDefaults.MyActivitySavedJobsTabKey));
        Assert.That(model.SavedJobs.Products.Select(product => product.Id).ToArray(), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public async Task MyActivity_SavedJobs_Empty_Wishlist_Returns_Empty_Model()
    {
        // Arrange
        var store = new Store { Id = 9 };
        _storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);
        _shoppingCartService.Setup(x => x.GetShoppingCartAsync(_customer, ShoppingCartType.Wishlist, store.Id, null, null, null, 0))
            .ReturnsAsync(new List<ShoppingCartItem>());

        // Act
        var result = await _controller.MyActivity(AIInterviewDefaults.MyActivitySavedJobsTabKey);

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = (MyActivityPageModel)((ViewResult)result).Model;
        Assert.That(model.SavedJobs, Is.Not.Null);
        Assert.That(model.SavedJobs.Products, Is.Empty);
    }

    [Test]
    public async Task MyActivity_Htmx_SavedJobs_Request_Returns_SavedJobs_Partial_Model()
    {
        // Arrange
        var store = new Store { Id = 11 };
        _controller.ControllerContext.HttpContext.Request.Headers["HX-Request"] = "true";
        _storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);
        _shoppingCartService.Setup(x => x.GetShoppingCartAsync(_customer, ShoppingCartType.Wishlist, store.Id, null, null, null, 0))
            .ReturnsAsync(new List<ShoppingCartItem>());

        // Act
        var result = await _controller.MyActivity(AIInterviewDefaults.MyActivitySavedJobsTabKey);

        // Assert
        Assert.That(result, Is.TypeOf<PartialViewResult>());
        var partialViewResult = (PartialViewResult)result;
        var model = (MyActivityPageModel)partialViewResult.Model;
        Assert.That(model.ActiveTab, Is.EqualTo(AIInterviewDefaults.MyActivitySavedJobsTabKey));
    }

    [Test]
    public async Task MyActivity_AppliedJobs_SortOrder_Is_Preserved()
    {
        // Arrange
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<JobApplication>
            {
                new() { Id = 1, JobTitle = "First role", Status = "Applied", CreatedOnUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc) },
                new() { Id = 2, JobTitle = "Second role", Status = "Applied", CreatedOnUtc = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc) }
            });
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<InterviewSession>
            {
                new() { Id = 10, ProductId = 1, Score = 65, CompletedOnUtc = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc) },
                new() { Id = 11, ProductId = 2, Score = 92, CompletedOnUtc = new DateTime(2026, 7, 4, 0, 0, 0, DateTimeKind.Utc) }
            });

        // Act
        var result = await _controller.MyActivity(AIInterviewDefaults.MyActivityAppliedJobsTabKey, "HighestScore");

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var model = (MyActivityPageModel)((ViewResult)result).Model;
        Assert.That(model.ActiveTab, Is.EqualTo(AIInterviewDefaults.MyActivityAppliedJobsTabKey));
        Assert.That(model.AppliedJobs.SortOrder, Is.EqualTo("HighestScore"));
    }

    [Test]
    public async Task MyActivity_AppliedJobs_Paginates_Results()
    {
        // Arrange
        var applications = Enumerable.Range(1, 6)
            .Select(index => new JobApplication
            {
                Id = index,
                JobTitle = $"Role {index}",
                Status = "Applied",
                CreatedOnUtc = new DateTime(2026, 7, index, 0, 0, 0, DateTimeKind.Utc)
            })
            .ToList();

        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(applications);
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(new List<InterviewSession>());

        // Act
        var result = await _controller.MyActivity(AIInterviewDefaults.MyActivityAppliedJobsTabKey, page: 2, pageSize: 5);

        // Assert
        var model = (MyActivityPageModel)((ViewResult)result).Model;
        Assert.That(model.AppliedJobs.TotalCount, Is.EqualTo(6));
        Assert.That(model.AppliedJobs.TotalPages, Is.EqualTo(2));
        Assert.That(model.AppliedJobs.Page, Is.EqualTo(2));
        Assert.That(model.AppliedJobs.PageSize, Is.EqualTo(5));
        Assert.That(model.AppliedJobs.Applications.Select(application => application.Id).ToArray(), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public async Task MyActivity_SavedJobs_Paginates_Filtered_Job_Products()
    {
        // Arrange
        var store = new Store { Id = 12 };
        var wishlistItems = Enumerable.Range(1, 6)
            .Select(index => new ShoppingCartItem
            {
                ProductId = index,
                CreatedOnUtc = new DateTime(2026, 7, index, 8, 0, 0, DateTimeKind.Utc)
            })
            .ToList();
        var products = Enumerable.Range(1, 6)
            .Select(index => new Product
            {
                Id = index,
                Name = $"Saved job {index}",
                Published = true,
                Deleted = false
            })
            .ToList();

        _storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);
        _shoppingCartService.Setup(x => x.GetShoppingCartAsync(_customer, ShoppingCartType.Wishlist, store.Id, null, null, null, 0))
            .ReturnsAsync(wishlistItems);
        _productService.Setup(x => x.GetProductsByIdsAsync(It.IsAny<int[]>()))
            .ReturnsAsync((int[] ids) => products.Where(product => ids.Contains(product.Id)).ToList());
        _jobRequirementService.Setup(x => x.IsJobProductAsync(It.IsAny<Product>())).ReturnsAsync(true);
        _productModelFactory.Setup(x => x.PrepareProductOverviewModelsAsync(It.IsAny<IEnumerable<Product>>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((IEnumerable<Product> selectedProducts, bool preparePriceModel, bool preparePictureModel, int? productThumbPictureSize, bool prepareSpecificationAttributes, bool forceRedirectionAfterAddingToCart) => selectedProducts
                .Select(product => new ProductOverviewModel { Id = product.Id, Name = product.Name })
                .ToList());

        // Act
        var result = await _controller.MyActivity(AIInterviewDefaults.MyActivitySavedJobsTabKey, page: 2, pageSize: 5);

        // Assert
        var model = (MyActivityPageModel)((ViewResult)result).Model;
        Assert.That(model.SavedJobs.TotalCount, Is.EqualTo(6));
        Assert.That(model.SavedJobs.TotalPages, Is.EqualTo(2));
        Assert.That(model.SavedJobs.Page, Is.EqualTo(2));
        Assert.That(model.SavedJobs.Products.Select(product => product.Id).ToArray(), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public async Task MyActivity_MockInterviews_Paginates_Results()
    {
        // Arrange
        _interviewSessionService.Setup(x => x.GetSessionsByCustomerIdAsync(_customer.Id))
            .ReturnsAsync(Enumerable.Range(1, 6)
                .Select(index => new InterviewSession
                {
                    Id = index,
                    ProductId = index,
                    InterviewType = AIInterviewDefaults.InterviewTypeMockPractice,
                    CreatedOnUtc = new DateTime(2026, 7, index, 0, 0, 0, DateTimeKind.Utc),
                    Score = 50 + index
                })
                .ToList());

        // Act
        var result = await _controller.MyActivity(AIInterviewDefaults.MyActivityMockInterviewsTabKey, page: 2, pageSize: 5);

        // Assert
        var model = (MyActivityPageModel)((ViewResult)result).Model;
        Assert.That(model.MockInterviews.TotalCount, Is.EqualTo(6));
        Assert.That(model.MockInterviews.TotalPages, Is.EqualTo(2));
        Assert.That(model.MockInterviews.Page, Is.EqualTo(2));
        Assert.That(model.MockInterviews.Items.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task MyApplications_Legacy_Route_Still_Returns_Standalone_View()
    {
        // Act
        var result = await _controller.MyApplications("LatestApplied");

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        Assert.That(viewResult.ViewName, Is.EqualTo("~/Plugins/Misc.AIInterview/Views/MyApplications.cshtml"));
        var model = (ApplicationListModel)viewResult.Model;
        Assert.That(model.SortOrder, Is.EqualTo("LatestApplied"));
    }
}
