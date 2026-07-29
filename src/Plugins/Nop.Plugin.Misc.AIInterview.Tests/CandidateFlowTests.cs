using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Primitives;
using Moq;
using System.Net;
using System.Net.Http;
using System.Threading;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Data;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Web.Framework.Mvc.Routing;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class CandidateFlowTests
{
    private Mock<IApplicationService> _applicationService;
    private Mock<IInterviewSessionService> _sessionService;
    private AIInterviewSettings _settings;
    private Mock<IWorkContext> _workContext;
    private Mock<INotificationService> _notificationService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<IDownloadService> _downloadService;
    private Mock<ICustomerService> _customerService;
    private Mock<IProductService> _productService;
    private Mock<ISponsorInviteService> _inviteService;
    private Mock<ICreditService> _creditService;
    private Mock<IJobRequirementService> _jobRequirementService;
    private Mock<IJobInterviewExperienceService> _jobInterviewExperienceService;
    private Mock<IInterviewTurnService> _turnService;
    private AIInterviewController _controller;
    private MockAiInterviewController _runtimeController;

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }

    [SetUp]
    public void SetUp()
    {
        _applicationService = new Mock<IApplicationService>();
        _sessionService = new Mock<IInterviewSessionService>();
        _settings = new AIInterviewSettings { Enabled = true };
        _workContext = new Mock<IWorkContext>();
        _notificationService = new Mock<INotificationService>();
        _localizationService = new Mock<ILocalizationService>();
        _downloadService = new Mock<IDownloadService>();
        _customerService = new Mock<ICustomerService>();
        _productService = new Mock<IProductService>();
        _inviteService = new Mock<ISponsorInviteService>();
        _creditService = new Mock<ICreditService>();
        _jobRequirementService = new Mock<IJobRequirementService>();
        _jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        _turnService = new Mock<IInterviewTurnService>();
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<int>()))
            .ReturnsAsync(new JobRequirementsModel());
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>()))
            .ReturnsAsync(new JobRequirementsModel());

        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(It.IsAny<int>())).ReturnsAsync(new List<JobApplication>());
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(It.IsAny<int>())).ReturnsAsync(new List<InterviewSession>());
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(true);

        _controller = new AIInterviewController(
            _applicationService.Object,
            _sessionService.Object,
            _settings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            _turnService.Object);

        _runtimeController = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object);

        _customerService.Setup(x => x.IsRegisteredAsync(It.IsAny<Customer>(), true)).ReturnsAsync(true);

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key);
    }

    [Test]
    public async Task Apply_ResumeRequired_Validation_Fails()
    {
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<int>()))
            .ReturnsAsync(new JobRequirementsModel { ResumeRequired = true });
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new global::Nop.Core.Domain.Localization.Language { Id = 1 });
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(customer.Id, "Software Engineer"))
            .ReturnsAsync(new List<JobApplication>());
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<JobApplication>());

        var model = new ApplyModel { JobTitle = "Software Engineer" };
        _controller.ModelState.AddModelError("ResumeFile", "Required");
        var result = await _controller.Apply(model);

        var viewResult = (ViewResult)result;
        Assert.That(viewResult.ViewData.ModelState.ErrorCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task Apply_DoesNotSilentlyReuseResume()
    {
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<int>()))
            .ReturnsAsync(new JobRequirementsModel { ResumeRequired = true });
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new global::Nop.Core.Domain.Localization.Language { Id = 1 });

        var previousApps = new List<JobApplication>
        {
            new JobApplication { CustomerId = 1, JobTitle = "Old Job", ResumeDownloadId = 123, CreatedOnUtc = DateTime.UtcNow.AddDays(-1) }
        };
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(customer.Id, "Senior Dev"))
            .ReturnsAsync(new List<JobApplication>());
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(previousApps);

        var model = new ApplyModel { JobTitle = "Senior Dev" };

        var result = await _controller.Apply(model);

        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.IsAny<JobApplication>()), Times.Never);
        Assert.That(result, Is.InstanceOf<ViewResult>());
    }

    [Test]
    public async Task Apply_SelectedPreviousResume_Works()
    {
        _jobRequirementService.Setup(x => x.GetRequirementsAsync(It.IsAny<int>()))
            .ReturnsAsync(new JobRequirementsModel { ResumeRequired = true });
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new global::Nop.Core.Domain.Localization.Language { Id = 1 });

        var previousApps = new List<JobApplication>
        {
            new JobApplication { CustomerId = 1, JobTitle = "Old Job", ResumeDownloadId = 123, CreatedOnUtc = DateTime.UtcNow.AddDays(-1) }
        };
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAndJobTitleAsync(customer.Id, "Senior Dev"))
            .ReturnsAsync(new List<JobApplication>());
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(previousApps);

        var result = await _controller.Apply(new ApplyModel
        {
            JobTitle = "Senior Dev",
            SelectedResumeDownloadId = 123
        });

        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.Is<JobApplication>(a => a.ResumeDownloadId == 123)), Times.Once);
        Assert.That(result, Is.InstanceOf<RedirectToRouteResult>());
    }

    [Test]
    public async Task Runtime_Start_Uses_ResolvedDifficulty()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);
        _productService.Setup(x => x.GetProductByIdAsync(1)).ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 1, Name = "Backend Engineer" });
        _jobInterviewExperienceService.Setup(x => x.ResolveInterviewDifficultyAsync(It.IsAny<Nop.Core.Domain.Catalog.Product>(), It.IsAny<IFormCollection>()))
            .ReturnsAsync("Hard");

        await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1, "Hard");

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.Difficulty == "Hard" && s.ProductId == 1)), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_SelectedPreviousResume_CreatesApplicationWithThatResume()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);
        _productService.Setup(x => x.GetProductByIdAsync(1)).ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 1, Name = "Backend Engineer" });
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<JobApplication>
        {
            new() { CustomerId = 1, ProductId = 7, JobTitle = "Old Job", ResumeDownloadId = 123, CreatedOnUtc = DateTime.UtcNow.AddDays(-1) }
        });
        _applicationService.Setup(x => x.InsertJobApplicationAsync(It.IsAny<JobApplication>()))
            .Callback<JobApplication>(application => application.Id = 99)
            .Returns(Task.CompletedTask);

        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["SelectedResumeDownloadId"] = "123"
        });

        var result = await _runtimeController.StartPost(form, 1, "Medium");

        Assert.That(result, Is.InstanceOf<JsonResult>());
        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.Is<JobApplication>(application =>
            application.CustomerId == 1 &&
            application.ProductId == 1 &&
            application.ResumeDownloadId == 123)), Times.Once);
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session =>
            session.CustomerId == 1 &&
            session.ProductId == 1 &&
            session.JobApplicationId > 0)), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_Idempotency_Works()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var activeSession = new InterviewSession
        {
            SessionKey = "existing",
            Token = "t1",
            IsActive = true,
            CustomerId = 1,
            ProductId = 1,
            TokenExpiryUtc = DateTime.UtcNow.AddHours(1)
        };
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession> { activeSession });

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1);
        var json = (JsonResult)result;

        var sessionKey = json.Value.GetType().GetProperty("sessionKey").GetValue(json.Value, null);
        Assert.That(sessionKey, Is.EqualTo("existing"));
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_ExpiredActiveSession_Creates_New_Session()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var staleSession = new InterviewSession
        {
            Id = 7,
            SessionKey = "stale-session",
            Token = "expired-token",
            IsActive = true,
            CustomerId = 1,
            ProductId = 1,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-2),
            TokenExpiryUtc = DateTime.UtcNow.AddMinutes(-10)
        };

        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession> { staleSession });
        _productService.Setup(x => x.GetProductByIdAsync(1))
            .ReturnsAsync(new Product { Id = 1, Name = "Backend Engineer" });
        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<JobApplication>());
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>()))
            .ReturnsAsync(true);
        InterviewSession insertedSession = null;
        _sessionService.Setup(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()))
            .Callback<InterviewSession>(session =>
            {
                insertedSession = session;
                session.Id = 8;
            })
            .Returns(Task.CompletedTask);
        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(x => x.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns("/mockaiinterview/runtime?token=generated");
        _runtimeController.Url = urlHelperMock.Object;

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1);
        var json = (JsonResult)result;
        var runtimeUrl = json.Value.GetType().GetProperty("runtimeUrl")?.GetValue(json.Value, null) as string;
        var token = json.Value.GetType().GetProperty("token")?.GetValue(json.Value, null) as string;

        Assert.That(staleSession.IsActive, Is.True);
        Assert.That(staleSession.CompletedOnUtc, Is.Null);
        Assert.That(insertedSession, Is.Not.Null);
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), It.IsAny<string>(), 1, 0), Times.Once);
        Assert.That(token, Is.EqualTo(insertedSession.Token));
        Assert.That(token, Is.Not.EqualTo("expired-token"));
        Assert.That(runtimeUrl, Is.EqualTo("/mockaiinterview/runtime?token=generated"));
    }

    [Test]
    public async Task Apply_With_ProductId_Redirects_To_Generic_Product_Url()
    {
        var product = new Product { Id = 9, Name = "Net Developer" };
        var nopUrlHelper = new Mock<INopUrlHelper>();
        nopUrlHelper.Setup(x => x.RouteGenericUrlAsync(product, null, null, null))
            .ReturnsAsync("/jobs/net-developer");
        _productService.Setup(x => x.GetProductByIdAsync(9)).ReturnsAsync(product);

        var controller = new AIInterviewController(
            _applicationService.Object,
            _sessionService.Object,
            _settings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            _turnService.Object,
            null,
            nopUrlHelper.Object);

        var result = await controller.Apply("Net Developer", 9);

        Assert.That(result, Is.TypeOf<RedirectResult>());
        Assert.That(((RedirectResult)result).Url, Is.EqualTo("/jobs/net-developer?jobTitle=Net%20Developer"));
        nopUrlHelper.Verify(x => x.RouteGenericUrlAsync(product, null, null, null), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_Get_Redirects_To_Generic_Product_Url_And_Preserves_SponsorToken()
    {
        var product = new Product { Id = 5, Name = "AI Developer" };
        var nopUrlHelper = new Mock<INopUrlHelper>();
        nopUrlHelper.Setup(x => x.RouteGenericUrlAsync(product, null, null, null))
            .ReturnsAsync("/jobs/ai-developer");
        _productService.Setup(x => x.GetProductByIdAsync(5)).ReturnsAsync(product);

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object,
            null,
            null,
            null,
            _jobRequirementService.Object,
            null,
            null,
            nopUrlHelper.Object);

        var result = await controller.Start(5, "invite-token");

        Assert.That(result, Is.TypeOf<RedirectResult>());
        Assert.That(((RedirectResult)result).Url, Is.EqualTo("/jobs/ai-developer?sponsorToken=invite-token"));
        nopUrlHelper.Verify(x => x.RouteGenericUrlAsync(product, null, null, null), Times.Once);
    }

    [Test]
    public void ProductDetails_StartInterview_Button_Wires_Post_And_Redirect()
    {
        var viewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "Components", "AIInterviewProductDetails", "Default.cshtml"));

        Assert.That(viewText, Does.Contain("data-job-ai-panel=\"true\""));
        Assert.That(viewText, Does.Contain("data-start-interview-button=\"true\""));
        Assert.That(viewText, Does.Contain("data-job-ai-action=\"start\""));
        Assert.That(viewText, Does.Contain("data-job-ai-action=\"apply\""));
        Assert.That(viewText, Does.Contain("data-start-url=\"@Url.RouteUrl(AIInterviewDefaults.MockStartRouteName)\""));
        Assert.That(viewText, Does.Contain("data-apply-url=\"@Url.RouteUrl(AIInterviewDefaults.ApplyInlineRouteName)\""));
        Assert.That(viewText, Does.Contain("SelectedResumeDownloadId"));
        Assert.That(viewText, Does.Contain("Plugins.Misc.AIInterview.Apply.ResumeRequired"));
        Assert.That(viewText, Does.Contain("AppendScriptParts(Nop.Web.Framework.UI.ResourceLocation.Footer, \"~/Plugins/Misc.AIInterview/Content/js/aiinterview-job-card.js\")"));
        Assert.That(viewText, Does.Not.Contain("postJson('@Url.RouteUrl(AIInterviewDefaults.MockStartRouteName)'"));
        Assert.That(viewText, Does.Not.Contain("document.addEventListener('click'"));
        Assert.That(viewText, Does.Not.Contain("aiinterview-server-fallback-shell"));
        Assert.That(viewText, Does.Not.Contain("@T(\"Plugins.Misc.AIInterview.Runtime.Error.NoCredits\")"));
    }

    [Test]
    public async Task Runtime_Start_MockPractice_WithSkillOnly_CreatesMockPracticeSession_WithoutJobApplication()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        var product = new Product { Id = 14, Name = "Generic AI Interview Practice", ProductTemplateId = 8 };
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeParser = new Mock<IProductAttributeParser>();
        var productAttributeService = new Mock<IProductAttributeService>();

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _productService.Setup(x => x.GetProductByIdAsync(product.Id)).ReturnsAsync(product);
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(product.ProductTemplateId))
            .ReturnsAsync(new ProductTemplate
            {
                Id = product.ProductTemplateId,
                Name = AIInterviewDefaults.MockPracticeProductTemplateName,
                ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath
            });
        productAttributeParser.Setup(x => x.ParseProductAttributesAsync(product, It.IsAny<IFormCollection>(), It.IsAny<List<string>>()))
            .ReturnsAsync("<attributes />");
        productAttributeParser.Setup(x => x.ParseProductAttributeValuesAsync("<attributes />", 0))
            .ReturnsAsync(new List<ProductAttributeValue>
            {
                new() { Id = 101, Name = "Medium", ProductAttributeMappingId = 21 },
                new() { Id = 102, Name = "Software Development", ProductAttributeMappingId = 22 }
            });
        productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(21))
            .ReturnsAsync(new ProductAttributeMapping { Id = 21, ProductAttributeId = 31 });
        productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(22))
            .ReturnsAsync(new ProductAttributeMapping { Id = 22, ProductAttributeId = 32 });
        productAttributeService.Setup(x => x.GetProductAttributeByIdAsync(31))
            .ReturnsAsync(new ProductAttribute { Id = 31, Name = "Practice Difficulty" });
        productAttributeService.Setup(x => x.GetProductAttributeByIdAsync(32))
            .ReturnsAsync(new ProductAttribute { Id = 32, Name = "Practice Skill" });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object,
            null,
            _turnService.Object,
            null,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            null,
            productTemplateService.Object,
            productAttributeParser.Object,
            productAttributeService.Object);

        var result = await controller.StartPost(new FormCollection(new Dictionary<string, StringValues>()), product.Id, "Medium");

        Assert.That(result, Is.InstanceOf<JsonResult>());
        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.IsAny<JobApplication>()), Times.Never);
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session =>
            session.CustomerId == customer.Id &&
            session.ProductId == product.Id &&
            session.SourceProductId == product.Id &&
            session.JobApplicationId == 0 &&
            session.InterviewType == AIInterviewDefaults.InterviewTypeMockPractice &&
            session.Difficulty == "Medium" &&
            session.ResumeDownloadId == 0 &&
            !string.IsNullOrWhiteSpace(session.SelectedProductAttributesJson))), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_MockPractice_MissingDifficulty_Blocks_Before_CreditCharge()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        var product = new Product { Id = 15, Name = "Generic AI Interview Practice", ProductTemplateId = 8 };
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeParser = new Mock<IProductAttributeParser>();
        var productAttributeService = new Mock<IProductAttributeService>();

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _productService.Setup(x => x.GetProductByIdAsync(product.Id)).ReturnsAsync(product);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(product.ProductTemplateId))
            .ReturnsAsync(new ProductTemplate
            {
                Id = product.ProductTemplateId,
                Name = AIInterviewDefaults.MockPracticeProductTemplateName,
                ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath
            });
        productAttributeParser.Setup(x => x.ParseProductAttributesAsync(product, It.IsAny<IFormCollection>(), It.IsAny<List<string>>()))
            .ReturnsAsync("<attributes />");
        productAttributeParser.Setup(x => x.ParseProductAttributeValuesAsync("<attributes />", 0))
            .ReturnsAsync(new List<ProductAttributeValue>
            {
                new() { Id = 202, Name = "Software Development", ProductAttributeMappingId = 42 }
            });
        productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(42))
            .ReturnsAsync(new ProductAttributeMapping { Id = 42, ProductAttributeId = 52 });
        productAttributeService.Setup(x => x.GetProductAttributeByIdAsync(52))
            .ReturnsAsync(new ProductAttribute { Id = 52, Name = "Practice Skill" });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object,
            null,
            _turnService.Object,
            null,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            null,
            productTemplateService.Object,
            productAttributeParser.Object,
            productAttributeService.Object);

        var result = await controller.StartPost(new FormCollection(new Dictionary<string, StringValues>()), product.Id, "Medium");

        Assert.That(result, Is.InstanceOf<JsonResult>());
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_MockPractice_ParserErrors_Block_Before_CreditCharge()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        var product = new Product { Id = 16, Name = "Generic AI Interview Practice", ProductTemplateId = 8 };
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeParser = new Mock<IProductAttributeParser>();
        var productAttributeService = new Mock<IProductAttributeService>();

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _productService.Setup(x => x.GetProductByIdAsync(product.Id)).ReturnsAsync(product);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(product.ProductTemplateId))
            .ReturnsAsync(new ProductTemplate
            {
                Id = product.ProductTemplateId,
                Name = AIInterviewDefaults.MockPracticeProductTemplateName,
                ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath
            });
        productAttributeParser.Setup(x => x.ParseProductAttributesAsync(product, It.IsAny<IFormCollection>(), It.IsAny<List<string>>()))
            .ReturnsAsync((Product parsedProduct, IFormCollection parsedForm, List<string> errors) =>
            {
                errors.Add("Please select a practice difficulty.");
                return "<attributes />";
            });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object,
            null,
            _turnService.Object,
            null,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            null,
            productTemplateService.Object,
            productAttributeParser.Object,
            productAttributeService.Object);

        var result = await controller.StartPost(new FormCollection(new Dictionary<string, StringValues>()), product.Id, "Medium");

        Assert.That(result, Is.InstanceOf<JsonResult>());
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_MockPractice_WithPreviousPracticeResume_AllowsResumeOnlyStart()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        var product = new Product { Id = 17, Name = "Generic AI Interview Practice", ProductTemplateId = 8 };
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeParser = new Mock<IProductAttributeParser>();
        var productAttributeService = new Mock<IProductAttributeService>();

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>
        {
            new()
            {
                Id = 90,
                CustomerId = customer.Id,
                ProductId = 999,
                SourceProductId = 999,
                ResumeDownloadId = 555,
                InterviewType = AIInterviewDefaults.InterviewTypeMockPractice,
                CreatedOnUtc = DateTime.UtcNow.AddDays(-2)
            }
        });
        _productService.Setup(x => x.GetProductByIdAsync(product.Id)).ReturnsAsync(product);
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(product.ProductTemplateId))
            .ReturnsAsync(new ProductTemplate
            {
                Id = product.ProductTemplateId,
                Name = AIInterviewDefaults.MockPracticeProductTemplateName,
                ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath
            });
        productAttributeParser.Setup(x => x.ParseProductAttributesAsync(product, It.IsAny<IFormCollection>(), It.IsAny<List<string>>()))
            .ReturnsAsync("<attributes />");
        productAttributeParser.Setup(x => x.ParseProductAttributeValuesAsync("<attributes />", 0))
            .ReturnsAsync(new List<ProductAttributeValue>
            {
                new() { Id = 301, Name = "Low", ProductAttributeMappingId = 61 }
            });
        productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(61))
            .ReturnsAsync(new ProductAttributeMapping { Id = 61, ProductAttributeId = 71 });
        productAttributeService.Setup(x => x.GetProductAttributeByIdAsync(71))
            .ReturnsAsync(new ProductAttribute { Id = 71, Name = "Practice Difficulty" });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object,
            null,
            _turnService.Object,
            null,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            null,
            productTemplateService.Object,
            productAttributeParser.Object,
            productAttributeService.Object);

        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["SelectedResumeDownloadId"] = "555"
        });

        var result = await controller.StartPost(form, product.Id, "Low");

        Assert.That(result, Is.InstanceOf<JsonResult>());
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session =>
            session.CustomerId == customer.Id &&
            session.ProductId == product.Id &&
            session.ResumeDownloadId == 555 &&
            session.JobApplicationId == 0 &&
            session.InterviewType == AIInterviewDefaults.InterviewTypeMockPractice &&
            session.Difficulty == "Low" &&
            !session.SelectedProductAttributesJson.Contains("skill", StringComparison.OrdinalIgnoreCase))), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_MockPractice_WithUploadedResume_AllowsResumeOnlyStart_AndPersistsDownload()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        var product = new Product { Id = 20, Name = "Generic AI Interview Practice", ProductTemplateId = 8 };
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeParser = new Mock<IProductAttributeParser>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var resumeFileService = new Mock<IResumeFileService>();
        var resumeBytes = new byte[] { 1, 2, 3, 4 };
        var resumeFile = new FormFile(new MemoryStream(resumeBytes), 0, resumeBytes.Length, "ResumeFile", "candidate.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _productService.Setup(x => x.GetProductByIdAsync(product.Id)).ReturnsAsync(product);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(product.ProductTemplateId))
            .ReturnsAsync(new ProductTemplate
            {
                Id = product.ProductTemplateId,
                Name = AIInterviewDefaults.MockPracticeProductTemplateName,
                ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath
            });
        productAttributeParser.Setup(x => x.ParseProductAttributesAsync(product, It.IsAny<IFormCollection>(), It.IsAny<List<string>>()))
            .ReturnsAsync("<attributes />");
        productAttributeParser.Setup(x => x.ParseProductAttributeValuesAsync("<attributes />", 0))
            .ReturnsAsync(new List<ProductAttributeValue>
            {
                new() { Id = 601, Name = "Hard", ProductAttributeMappingId = 121 }
            });
        productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(121))
            .ReturnsAsync(new ProductAttributeMapping { Id = 121, ProductAttributeId = 131 });
        productAttributeService.Setup(x => x.GetProductAttributeByIdAsync(131))
            .ReturnsAsync(new ProductAttribute { Id = 131, Name = "Practice Difficulty" });
        resumeFileService.Setup(x => x.ValidateResumeFile(resumeFile))
            .Returns(new ResumeFileValidationResult { Success = true });
        resumeFileService.Setup(x => x.StoreResumeAsync(resumeFile))
            .ReturnsAsync(new ResumeFileStoreResult { Success = true, DownloadId = 777 });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object,
            null,
            _turnService.Object,
            null,
            _jobRequirementService.Object,
            null,
            null,
            null,
            resumeFileService.Object,
            null,
            productTemplateService.Object,
            productAttributeParser.Object,
            productAttributeService.Object);
        var files = new FormFileCollection { resumeFile };
        var form = new FormCollection(new Dictionary<string, StringValues>(), files);

        var result = await controller.StartPost(form, product.Id, "Hard");

        Assert.That(result, Is.InstanceOf<JsonResult>());
        resumeFileService.Verify(x => x.StoreResumeAsync(resumeFile), Times.Once);
        _applicationService.Verify(x => x.InsertJobApplicationAsync(It.IsAny<JobApplication>()), Times.Never);
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session =>
            session.CustomerId == customer.Id &&
            session.ProductId == product.Id &&
            session.ResumeDownloadId == 777 &&
            session.JobApplicationId == 0 &&
            session.InterviewType == AIInterviewDefaults.InterviewTypeMockPractice &&
            session.Difficulty == "Hard" &&
            !session.SelectedProductAttributesJson.Contains("skill", StringComparison.OrdinalIgnoreCase))), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_MockPractice_WithDifficultyOnly_ReturnsAlternativeGuidance_WithoutChargeOrSession()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        var product = new Product { Id = 21, Name = "Generic AI Interview Practice", ProductTemplateId = 8 };
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeParser = new Mock<IProductAttributeParser>();
        var productAttributeService = new Mock<IProductAttributeService>();

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _productService.Setup(x => x.GetProductByIdAsync(product.Id)).ReturnsAsync(product);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(product.ProductTemplateId))
            .ReturnsAsync(new ProductTemplate
            {
                Id = product.ProductTemplateId,
                Name = AIInterviewDefaults.MockPracticeProductTemplateName,
                ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath
            });
        productAttributeParser.Setup(x => x.ParseProductAttributesAsync(product, It.IsAny<IFormCollection>(), It.IsAny<List<string>>()))
            .ReturnsAsync("<attributes />");
        productAttributeParser.Setup(x => x.ParseProductAttributeValuesAsync("<attributes />", 0))
            .ReturnsAsync(new List<ProductAttributeValue>
            {
                new() { Id = 701, Name = "Medium", ProductAttributeMappingId = 141 }
            });
        productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(141))
            .ReturnsAsync(new ProductAttributeMapping { Id = 141, ProductAttributeId = 151 });
        productAttributeService.Setup(x => x.GetProductAttributeByIdAsync(151))
            .ReturnsAsync(new ProductAttribute { Id = 151, Name = "Practice Difficulty" });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object,
            null,
            _turnService.Object,
            null,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            null,
            productTemplateService.Object,
            productAttributeParser.Object,
            productAttributeService.Object);

        var result = (JsonResult)await controller.StartPost(
            new FormCollection(new Dictionary<string, StringValues>()),
            product.Id,
            "Medium");
        var error = result.Value?.GetType().GetProperty("error")?.GetValue(result.Value) as string;

        Assert.That(error, Does.Contain("Select a practice skill or provide a resume"));
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(
            It.IsAny<int>(),
            It.IsAny<decimal>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<int>()), Times.Never);
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_MockPractice_WithPromptSynonyms_CreatesSession()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        var product = new Product { Id = 19, Name = "Generic AI Interview Practice", ProductTemplateId = 8 };
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeParser = new Mock<IProductAttributeParser>();
        var productAttributeService = new Mock<IProductAttributeService>();

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _productService.Setup(x => x.GetProductByIdAsync(product.Id)).ReturnsAsync(product);
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(product.ProductTemplateId))
            .ReturnsAsync(new ProductTemplate
            {
                Id = product.ProductTemplateId,
                Name = AIInterviewDefaults.MockPracticeProductTemplateName,
                ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath
            });
        productAttributeParser.Setup(x => x.ParseProductAttributesAsync(product, It.IsAny<IFormCollection>(), It.IsAny<List<string>>()))
            .ReturnsAsync("<attributes />");
        productAttributeParser.Setup(x => x.ParseProductAttributeValuesAsync("<attributes />", 0))
            .ReturnsAsync(new List<ProductAttributeValue>
            {
                new() { Id = 501, Name = "Low", ProductAttributeMappingId = 101 },
                new() { Id = 502, Name = "JAVA", ProductAttributeMappingId = 102 }
            });
        productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(101))
            .ReturnsAsync(new ProductAttributeMapping { Id = 101, ProductAttributeId = 111, TextPrompt = "Difficulty" });
        productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(102))
            .ReturnsAsync(new ProductAttributeMapping { Id = 102, ProductAttributeId = 112, TextPrompt = "Skill" });
        productAttributeService.Setup(x => x.GetProductAttributeByIdAsync(111))
            .ReturnsAsync(new ProductAttribute { Id = 111, Name = "Practice Setup" });
        productAttributeService.Setup(x => x.GetProductAttributeByIdAsync(112))
            .ReturnsAsync(new ProductAttribute { Id = 112, Name = "Practice Focus" });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object,
            null,
            _turnService.Object,
            null,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            null,
            productTemplateService.Object,
            productAttributeParser.Object,
            productAttributeService.Object);

        var result = await controller.StartPost(new FormCollection(new Dictionary<string, StringValues>()), product.Id, "Low");

        Assert.That(result, Is.InstanceOf<JsonResult>());
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session =>
            session.ProductId == product.Id &&
            session.InterviewType == AIInterviewDefaults.InterviewTypeMockPractice &&
            session.Difficulty == "Low" &&
            session.SelectedProductAttributesJson.Contains("\"attributeName\":\"Practice Focus\"") &&
            session.SelectedProductAttributesJson.Contains("\"textPrompt\":\"Skill\"") &&
            session.SelectedProductAttributesJson.Contains("\"value\":\"JAVA\"") &&
            !string.IsNullOrWhiteSpace(session.SelectedProductAttributesJson))), Times.Once);
    }

    [Test]
    public async Task Runtime_Start_MockPractice_DoesNotReuse_ActiveJobSession_ForSameProduct()
    {
        var customer = new Customer { Id = 1, Email = "candidate@example.com" };
        var product = new Product { Id = 18, Name = "Generic AI Interview Practice", ProductTemplateId = 8 };
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeParser = new Mock<IProductAttributeParser>();
        var productAttributeService = new Mock<IProductAttributeService>();

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>
        {
            new()
            {
                Id = 77,
                CustomerId = customer.Id,
                ProductId = product.Id,
                JobApplicationId = 12,
                InterviewType = AIInterviewDefaults.InterviewTypeJob,
                SessionKey = "job-session",
                Token = "job-token",
                IsActive = true,
                CreatedOnUtc = DateTime.UtcNow.AddMinutes(-15),
                TokenExpiryUtc = DateTime.UtcNow.AddMinutes(15)
            }
        });
        _productService.Setup(x => x.GetProductByIdAsync(product.Id)).ReturnsAsync(product);
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(product.ProductTemplateId))
            .ReturnsAsync(new ProductTemplate
            {
                Id = product.ProductTemplateId,
                Name = AIInterviewDefaults.MockPracticeProductTemplateName,
                ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath
            });
        productAttributeParser.Setup(x => x.ParseProductAttributesAsync(product, It.IsAny<IFormCollection>(), It.IsAny<List<string>>()))
            .ReturnsAsync("<attributes />");
        productAttributeParser.Setup(x => x.ParseProductAttributeValuesAsync("<attributes />", 0))
            .ReturnsAsync(new List<ProductAttributeValue>
            {
                new() { Id = 401, Name = "Advanced", ProductAttributeMappingId = 81 },
                new() { Id = 402, Name = "Python", ProductAttributeMappingId = 82 }
            });
        productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(81))
            .ReturnsAsync(new ProductAttributeMapping { Id = 81, ProductAttributeId = 91 });
        productAttributeService.Setup(x => x.GetProductAttributeMappingByIdAsync(82))
            .ReturnsAsync(new ProductAttributeMapping { Id = 82, ProductAttributeId = 92 });
        productAttributeService.Setup(x => x.GetProductAttributeByIdAsync(91))
            .ReturnsAsync(new ProductAttribute { Id = 91, Name = "Practice Difficulty" });
        productAttributeService.Setup(x => x.GetProductAttributeByIdAsync(92))
            .ReturnsAsync(new ProductAttribute { Id = 92, Name = "Practice Skill" });

        var controller = new MockAiInterviewController(
            _sessionService.Object,
            _localizationService.Object,
            _workContext.Object,
            _inviteService.Object,
            _creditService.Object,
            _customerService.Object,
            _productService.Object,
            new Mock<global::Nop.Services.Vendors.IVendorService>().Object,
            _applicationService.Object,
            null,
            _jobInterviewExperienceService.Object,
            null,
            _turnService.Object,
            null,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            null,
            productTemplateService.Object,
            productAttributeParser.Object,
            productAttributeService.Object);

        var result = await controller.StartPost(new FormCollection(new Dictionary<string, StringValues>()), product.Id, "Advanced");

        Assert.That(result, Is.InstanceOf<JsonResult>());
        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(session =>
            session.ProductId == product.Id &&
            session.InterviewType == AIInterviewDefaults.InterviewTypeMockPractice &&
            session.Token != "job-token")), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, product.Id, 0), Times.Once);
    }

    [Test]
    public void ProductDetails_And_StartViews_Handle_Fetch_Errors_Safely()
    {
        var productViewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "Components", "AIInterviewProductDetails", "Default.cshtml"));
        var jobCardScript = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Content", "js", "aiinterview-job-card.js"));
        var startViewText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Start.cshtml"));

        Assert.That(productViewText, Does.Contain("Unable to reach the interview service. Please check your network and try again."));
        Assert.That(productViewText, Does.Contain("interviewError"));
        Assert.That(productViewText, Does.Contain("Plugins.Misc.AIInterview.Runtime.Error.ExpiredLink"));
        Assert.That(jobCardScript, Does.Contain("response.ok"));
        Assert.That(jobCardScript, Does.Contain("content-type"));
        Assert.That(jobCardScript, Does.Contain("Unable to reach the interview service. Please check your network and try again."));
        Assert.That(startViewText, Does.Contain("Unable to reach the interview service. Please check your network and try again."));
        Assert.That(startViewText, Does.Contain("response.ok"));
        Assert.That(startViewText, Does.Contain("content-type"));
    }

    [Test]
    public void Legacy_Interview_View_Is_Removed()
    {
        Assert.That(File.Exists(TestFilePathHelper.GetPluginFilePath("Views", "Interview.cshtml")), Is.False);
    }

    [Test]
    public async Task Runtime_Start_SponsorFallback_Works()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());

        var sponsorInvite = new SponsorInvite { Id = 10, SponsorId = 2, Email = "test@example.com", InviteCode = "SPONSOR123", ExpiryDateUtc = DateTime.UtcNow.AddDays(1), IsActive = true };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("SPONSOR123")).ReturnsAsync(sponsorInvite);

        // Sponsor has no credits
        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 0 });

        // Customer has credits
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1, "Medium", "SPONSOR123");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, 1, 0), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_ProductMismatchSponsoredInvite_FallsBack_To_CandidateCharge()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());

        var sponsorInvite = new SponsorInvite
        {
            Id = 13,
            SponsorId = 2,
            ProductId = 99,
            Email = "test@example.com",
            InviteCode = "MISMATCH123",
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
            IsActive = true,
            MaxAttempts = 2
        };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("MISMATCH123")).ReturnsAsync(sponsorInvite);

        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1, "Medium", "MISMATCH123");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, 1, 0), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_InactiveSponsorInvite_FallsBack_To_CandidateCharge()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());

        var sponsorInvite = new SponsorInvite
        {
            Id = 11,
            SponsorId = 2,
            ProductId = 1,
            Email = "test@example.com",
            InviteCode = "INACTIVE123",
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
            IsActive = false
        };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("INACTIVE123")).ReturnsAsync(sponsorInvite);

        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1, "Medium", "INACTIVE123");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, 1, 0), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Runtime_Start_ExhaustedSponsorInvite_FallsBack_To_CandidateCharge()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var sponsorInvite = new SponsorInvite
        {
            Id = 12,
            SponsorId = 2,
            ProductId = 1,
            Email = "test@example.com",
            InviteCode = "EXHAUSTED123",
            ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
            IsActive = true,
            MaxAttempts = 2
        };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("EXHAUSTED123")).ReturnsAsync(sponsorInvite);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession>());
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(12)).ReturnsAsync(2);

        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _creditService.Setup(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>())).ReturnsAsync(true);

        var result = await _runtimeController.StartPost(new FormCollection(new Dictionary<string, StringValues>()), 1, "Medium", "EXHAUSTED123");
        var json = (JsonResult)result;

        _sessionService.Verify(x => x.InsertInterviewSessionAsync(It.Is<InterviewSession>(s => s.SponsorInviteId == 0)), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(customer.Id, 1, It.IsAny<string>(), CreditLedgerSources.InterviewUsage, 1, 0), Times.Once);
        _creditService.Verify(x => x.AuthorizeAndChargeAsync(2, 1, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Runtime_SubmitAnswer_InvalidToken_ReturnsError()
    {
        _sessionService.Setup(x => x.GetSessionByTokenAsync("invalid")).ReturnsAsync((InterviewSession)null);

        var result = await _runtimeController.SubmitAnswer("invalid", "answer");
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var message = json.Value.GetType().GetProperty("message").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Invalid or expired session token."));
        Assert.That(error, Is.EqualTo("Invalid or expired session token."));
    }

    [Test]
    public async Task Runtime_SubmitAnswer_InvalidAnswer_ReturnsError()
    {
        var session = new InterviewSession { Token = "valid", IsActive = true, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("valid")).ReturnsAsync(session);

        var result = await _runtimeController.SubmitAnswer("valid", "");
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var message = json.Value.GetType().GetProperty("message").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Answer cannot be empty."));
        Assert.That(error, Is.EqualTo("Answer cannot be empty."));
    }

    [Test]
    public async Task Runtime_SubmitAnswer_ExpiredSession_ReturnsError()
    {
        var session = new InterviewSession { Token = "expired", IsActive = true, TokenExpiryUtc = DateTime.UtcNow };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("expired")).ReturnsAsync(session);

        var result = await _runtimeController.SubmitAnswer("expired", "answer");
        var json = (JsonResult)result;
        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var message = json.Value.GetType().GetProperty("message").GetValue(json.Value, null);
        var error = json.Value.GetType().GetProperty("error").GetValue(json.Value, null);
        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Invalid or expired session token."));
        Assert.That(error, Is.EqualTo("Invalid or expired session token."));
    }

    [Test]
    public async Task Runtime_RefreshToken_Success()
    {
        var session = new InterviewSession { Token = "old", IsActive = true, CompletedOnUtc = null, TokenExpiryUtc = DateTime.UtcNow.AddHours(1) };
        _sessionService.Setup(x => x.GetSessionByTokenAsync("old")).ReturnsAsync(session);

        var result = await _runtimeController.RefreshToken("old");
        var json = (JsonResult)result;

        var success = json.Value.GetType().GetProperty("success").GetValue(json.Value, null);
        var newToken = json.Value.GetType().GetProperty("newToken").GetValue(json.Value, null);
        var tokenExpiryUtc = json.Value.GetType().GetProperty("tokenExpiryUtc").GetValue(json.Value, null);
        Assert.That(success, Is.EqualTo(true));
        Assert.That(newToken, Is.Not.Null);
        Assert.That(newToken, Is.EqualTo("old"));
        Assert.That(tokenExpiryUtc, Is.EqualTo(session.TokenExpiryUtc.Value));
        _sessionService.Verify(x => x.UpdateInterviewSessionAsync(It.IsAny<InterviewSession>()), Times.Never);
    }

    [Test]
    public async Task History_Page_Loads()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id))
            .ReturnsAsync(new List<InterviewSession> { new InterviewSession { Id = 1, CustomerId = 1 } });

        var result = await _runtimeController.History();
        Assert.That(result, Is.InstanceOf<ViewResult>());
    }

    [Test]
    public async Task Report_UnauthorizedAccess_Redirects()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 99)).ReturnsAsync(false);

        var result = await _controller.Report(99);
        Assert.That(result, Is.InstanceOf<ChallengeResult>());
    }

    [Test]
    public async Task Report_MissingReport_HandlesGracefully()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 1)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(1)).ReturnsAsync(new InterviewSession { Id = 1, ReportData = "" });

        var result = await _controller.Report(1);
        Assert.That(result, Is.InstanceOf<RedirectToRouteResult>());
        _notificationService.Verify(x => x.ErrorNotification(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task Report_WithoutSavedRecording_LeavesRecordingUrlNull()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 3)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(3)).ReturnsAsync(new InterviewSession
        {
            Id = 3,
            CustomerId = 1,
            ProductId = 0,
            ReportData = "overall score: 70",
            QuestionScores = "[70]",
            Score = 70,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-1),
            CompletedOnUtc = DateTime.UtcNow
        });

        var result = await _controller.Report(3);

        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.RecordingUrl, Is.Null.Or.Empty);
    }

    [Test]
    public async Task Report_IncludesSavedTurns()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 2)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(2)).ReturnsAsync(new InterviewSession
        {
            Id = 2,
            CustomerId = 1,
            ProductId = 11,
            SessionKey = "session-2",
            Token = "token-2",
            ReportData = "overall score: 88",
            QuestionScores = "[88, 92]",
            Score = 90,
            RecordingUrl = "https://storage.example.com/recordings/session-2.webm",
            CreatedOnUtc = DateTime.UtcNow.AddHours(-1),
            CompletedOnUtc = DateTime.UtcNow
        });
        _productService.Setup(x => x.GetProductByIdAsync(11)).ReturnsAsync(new Product { Id = 11, Name = "Backend Engineer" });
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(2)).ReturnsAsync(new List<InterviewTurn>
        {
            new InterviewTurn
            {
                Id = 100,
                InterviewSessionId = 2,
                SequenceNumber = 1,
                QuestionText = "Q1",
                AnswerText = "A1",
                Score = 88,
                Feedback = "Good",
                RubricJson = "{\"technicalScore\":91,\"communicationScore\":86,\"professionalismScore\":84,\"positiveAttitudeScore\":92,\"score\":88}",
                AskedOnUtc = DateTime.UtcNow.AddMinutes(-30),
                AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-29)
            }
        });

        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlActionContext ctx) =>
                ctx.Action == "Recording" ? "/aiinterview/recording/2" : "/aiinterview/report/2");
        _controller.Url = urlHelperMock.Object;

        var result = await _controller.Report(2);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.Turns.Count, Is.EqualTo(1));
        Assert.That(model.Turns[0].QuestionText, Is.EqualTo("Q1"));
        Assert.That(model.Turns[0].AnswerText, Is.EqualTo("A1"));
        Assert.That(model.Turns[0].TechnicalScore, Is.EqualTo(91));
        Assert.That(model.Turns[0].CommunicationScore, Is.EqualTo(86));
        Assert.That(model.Turns[0].ProfessionalismScore, Is.EqualTo(84));
        Assert.That(model.Turns[0].PositiveAttitudeScore, Is.EqualTo(92));
        Assert.That(model.ParsedQuestionScores, Is.EquivalentTo(new[] { 88m, 92m }));
        Assert.That(model.RecordingUrl, Is.EqualTo("/aiinterview/recording/2"));
    }

    [Test]
    public async Task Report_PrefersCompletedDate_WhenPresent()
    {
        var customer = new Customer { Id = 1, Email = "test@example.com" };
        var createdOnUtc = new DateTime(2026, 06, 07, 15, 09, 46, DateTimeKind.Utc);
        var completedOnUtc = new DateTime(2026, 07, 01, 13, 26, 15, DateTimeKind.Utc);
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 25)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(25)).ReturnsAsync(new InterviewSession
        {
            Id = 25,
            CustomerId = 1,
            ProductId = 11,
            ReportData = "overall score: 88",
            QuestionScores = "[88]",
            Score = 88,
            CreatedOnUtc = createdOnUtc,
            StartedOnUtc = createdOnUtc.AddMinutes(1),
            CompletedOnUtc = completedOnUtc
        });
        _productService.Setup(x => x.GetProductByIdAsync(11)).ReturnsAsync(new Product { Id = 11, Name = "Backend Engineer" });
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(25)).ReturnsAsync(new List<InterviewTurn>());

        var result = await _controller.Report(25);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.CreatedOnUtc, Is.EqualTo(createdOnUtc));
        Assert.That(model.CompletedOnUtc, Is.EqualTo(completedOnUtc));
        Assert.That(model.ReportDateUtc, Is.EqualTo(completedOnUtc));
    }

    [Test]
    public async Task Report_SanitizesPersistedStrengths_ThatRepeatQuestionText()
    {
        var customer = new Customer { Id = 1 };
        var staleQuestionText = "Can you describe your role in the Copilot4ServiceNow project and how you optimized agent prompts?";
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 39)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(39)).ReturnsAsync(new InterviewSession
        {
            Id = 39,
            CustomerId = 1,
            ProductId = 11,
            ReportData = $"Overall score: 73/100{Environment.NewLine}Strengths: {staleQuestionText}{Environment.NewLine}Improvement areas: Continue refining examples.{Environment.NewLine}Completion note: Existing completion note.",
            QuestionScores = "[73]",
            Score = 73,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-2),
            CompletedOnUtc = DateTime.UtcNow
        });
        _productService.Setup(x => x.GetProductByIdAsync(11)).ReturnsAsync(new Product { Id = 11, Name = "Gen AI Engineer" });
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(39)).ReturnsAsync(new List<InterviewTurn>
        {
            new()
            {
                Id = 1,
                InterviewSessionId = 39,
                SequenceNumber = 1,
                QuestionText = staleQuestionText,
                AnswerText = "I led Copilot and ServiceNow integration work, tuned prompts, and coordinated Teams workflows for enterprise support cases.",
                Feedback = "Strong answer with clear structure.",
                Score = 79
            }
        });

        var result = await _controller.Report(39);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.ReportData, Does.Not.Contain($"Strengths: {staleQuestionText}"));
        Assert.That(model.ReportData, Does.Contain("Strengths: Demonstrated clear structure and communication."));
        Assert.That(model.ReportData, Does.Contain("Completion note: Existing completion note."));
    }

    [Test]
    public async Task Report_And_ReportPanel_ShareSanitizedReportData_WhenImprovementLineRepeatsQuestionText()
    {
        var customer = new Customer { Id = 1 };
        var staleImprovementQuestion = "What challenges did you face while developing the Searchlight AI Enterprise Chatbot and how did you overcome them?";
        var staleStrengthQuestion = "Can you describe your role in the Copilot4ServiceNow project and how you optimized agent prompts?";
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 40)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(40)).ReturnsAsync(new InterviewSession
        {
            Id = 40,
            CustomerId = 1,
            ProductId = 11,
            ReportData = $"Overall score: 73/100{Environment.NewLine}Strengths: {staleStrengthQuestion}{Environment.NewLine}Improvement areas: {staleImprovementQuestion}{Environment.NewLine}Completion note: Existing completion note.",
            QuestionScores = "[73]",
            Score = 73,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-2),
            CompletedOnUtc = DateTime.UtcNow
        });
        _productService.Setup(x => x.GetProductByIdAsync(11)).ReturnsAsync(new Product { Id = 11, Name = "Gen AI Engineer" });
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(40)).ReturnsAsync(new List<InterviewTurn>
        {
            new()
            {
                Id = 1,
                InterviewSessionId = 40,
                SequenceNumber = 1,
                QuestionText = staleStrengthQuestion,
                AnswerText = "I led Copilot and ServiceNow integration work, tuned prompts, and coordinated Teams workflows for enterprise support cases.",
                Feedback = "Strong answer with clear structure.",
                Score = 79
            },
            new()
            {
                Id = 2,
                InterviewSessionId = 40,
                SequenceNumber = 2,
                QuestionText = staleImprovementQuestion,
                AnswerText = "I mentioned hallucinations but not enough implementation detail.",
                Feedback = "More detail on the solutions implemented would strengthen the response.",
                Score = 61
            }
        });

        var fullReportResult = await _controller.Report(40);
        var panelResult = await _controller.ReportPanel(40);

        Assert.That(fullReportResult, Is.TypeOf<ViewResult>());
        Assert.That(panelResult, Is.TypeOf<PartialViewResult>());
        var fullModel = (InterviewReportModel)((ViewResult)fullReportResult).Model;
        var panelModel = (InterviewReportModel)((PartialViewResult)panelResult).Model;

        Assert.That(fullModel.ReportData, Does.Not.Contain(staleStrengthQuestion));
        Assert.That(fullModel.ReportData, Does.Not.Contain(staleImprovementQuestion));
        Assert.That(fullModel.ReportData, Does.Contain("Provide more detail on the solutions implemented."));
        Assert.That(panelModel.ReportData, Is.EqualTo(fullModel.ReportData));
    }

    [Test]
    public async Task Report_OldTurnWithoutRubric_LeavesCategoryScoresNull()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 12)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(12)).ReturnsAsync(new InterviewSession
        {
            Id = 12,
            CustomerId = 1,
            ProductId = 11,
            ReportData = "overall score: 75",
            QuestionScores = "[75]",
            Score = 75,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-1),
            CompletedOnUtc = DateTime.UtcNow
        });
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(12)).ReturnsAsync(new List<InterviewTurn>
        {
            new InterviewTurn
            {
                Id = 101,
                InterviewSessionId = 12,
                SequenceNumber = 1,
                QuestionText = "Legacy Q1",
                AnswerText = "Legacy A1",
                Score = 75,
                Feedback = "Legacy feedback"
            }
        });

        var result = await _controller.Report(12);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.Turns, Has.Count.EqualTo(1));
        Assert.That(model.Turns[0].TechnicalScore, Is.Null);
        Assert.That(model.Turns[0].CommunicationScore, Is.Null);
        Assert.That(model.Turns[0].ProfessionalismScore, Is.Null);
        Assert.That(model.Turns[0].PositiveAttitudeScore, Is.Null);
    }

    [Test]
    public async Task MockReport_WithoutSavedRecording_LeavesRecordingUrlNull()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 4)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(4)).ReturnsAsync(new InterviewSession
        {
            Id = 4,
            CustomerId = 1,
            ProductId = 0,
            ReportData = "overall score: 70",
            QuestionScores = "[70]",
            Score = 70,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-1),
            CompletedOnUtc = DateTime.UtcNow
        });

        var result = await _runtimeController.Report(4);

        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.RecordingUrl, Is.Null.Or.Empty);
    }

    [Test]
    public async Task Report_HandlesTurnLookupFailure_AndStillRendersSessionReport()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 18)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(18)).ReturnsAsync(new InterviewSession
        {
            Id = 18,
            CustomerId = 1,
            ProductId = 11,
            SessionKey = "session-18",
            Token = "token-18",
            ReportData = "overall score: 77",
            QuestionScores = "[77]",
            Score = 77,
            CreatedOnUtc = DateTime.UtcNow.AddHours(-1),
            CompletedOnUtc = DateTime.UtcNow
        });
        _productService.Setup(x => x.GetProductByIdAsync(11)).ReturnsAsync(new Product { Id = 11, Name = "Backend Engineer" });
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(18)).ThrowsAsync(new InvalidOperationException("missing turn table"));

        var result = await _controller.Report(18);

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (InterviewReportModel)viewResult.Model;
        Assert.That(model.SessionId, Is.EqualTo(18));
        Assert.That(model.ReportData, Is.EqualTo("overall score: 77"));
        Assert.That(model.Turns, Is.Empty);
        Assert.That(model.ParsedQuestionScores, Is.EquivalentTo(new[] { 77m }));
    }

    [Test]
    public async Task MyApplications_IncludesRecordingAccess_ForLatestCompletedSession()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.EnsureRecordingShareTokenAsync(It.IsAny<InterviewSession>())).ReturnsAsync("share-token-99");

        var applications = new List<JobApplication>
        {
            new JobApplication { Id = 10, ProductId = 5, JobTitle = "Test Job", CreatedOnUtc = DateTime.UtcNow.AddDays(-2) }
        };
        var sessions = new List<InterviewSession>
        {
            new InterviewSession { Id = 98, ProductId = 5, CompletedOnUtc = DateTime.UtcNow.AddDays(-1), RecordingUrl = "" },
            new InterviewSession { Id = 99, ProductId = 5, CompletedOnUtc = DateTime.UtcNow, RecordingUrl = "https://storage.example.com/recordings/session-99.webm" }
        };

        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(applications);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(sessions);

        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlActionContext ctx) => ctx.Action switch
            {
                "Recording" => "/aiinterview/recording/99",
                "ReportPanel" => "/aiinterview/report-panel/99",
                _ => "/aiinterview/report/99"
            });
        urlHelperMock.Setup(u => u.RouteUrl(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlRouteContext>()))
            .Returns("/aiinterview/recording/share/share-token-99");
        _controller.Url = urlHelperMock.Object;

        var result = await _controller.MyApplications("LatestApplied");

        var viewResult = (ViewResult)result;
        var model = (ApplicationListModel)viewResult.Model;
        Assert.That(model.Applications.Single().RecordingUrl, Is.Not.Null.And.Contain("/aiinterview/recording/99"));
        Assert.That(model.Applications.Single().RecordingShareUrl, Is.EqualTo("/aiinterview/recording/share/share-token-99"));
        Assert.That(model.Applications.Single().RecordingShareUrl, Does.Not.EndWith("/99"));
        Assert.That(model.Applications.Single().RecordingShareUrl, Does.Not.Contain("storage.example.com"));
        Assert.That(model.Applications.Single().RecordingShareUrl, Does.Not.Contain("sig="));
    }

    [Test]
    public async Task RecordingRoute_AllowsOwner_AndRejectsUnauthorizedUser()
    {
        _settings.AzureBlobStorageContainerUrl = "https://storage.example.com/recordings";
        _settings.AzureBlobStorageSasToken = "?sig=token";
        var allowedCustomer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(allowedCustomer);

        _sessionService.Setup(x => x.CanAccessReportAsync(allowedCustomer.Id, 55)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(55)).ReturnsAsync(new InterviewSession
        {
            Id = 55,
            CustomerId = 1,
            RecordingUrl = "https://storage.example.com/recordings/session-55.webm"
        });

        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("recording-bytes")))
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

        var controller = new AIInterviewController(
            _applicationService.Object,
            _sessionService.Object,
            _settings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            _turnService.Object,
            factory.Object);

        var result = await controller.Recording(55);
        Assert.That(result, Is.TypeOf<FileStreamResult>());

        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(new Customer { Id = 2 });
        _sessionService.Setup(x => x.CanAccessReportAsync(2, 55)).ReturnsAsync(false);
        var unauthorized = await controller.Recording(55);
        Assert.That(unauthorized, Is.TypeOf<ChallengeResult>());
    }

    [Test]
    public async Task RecordingRoute_RejectsUrls_OutsideConfiguredContainer()
    {
        _settings.AzureBlobStorageContainerUrl = "https://storage.example.com/recordings";
        _settings.AzureBlobStorageSasToken = "?sig=token";
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 56)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(56)).ReturnsAsync(new InterviewSession
        {
            Id = 56,
            CustomerId = 1,
            RecordingUrl = "https://evil.example.com/recordings/session-56.webm"
        });
        _sessionService.Setup(x => x.CanAccessReportAsync(customer.Id, 57)).ReturnsAsync(true);
        _sessionService.Setup(x => x.GetInterviewSessionByIdAsync(57)).ReturnsAsync(new InterviewSession
        {
            Id = 57,
            CustomerId = 1,
            RecordingUrl = "https://storage.example.com/other/session-57.webm?sig=old"
        });

        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("recording-bytes")))
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

        var controller = new AIInterviewController(
            _applicationService.Object,
            _sessionService.Object,
            _settings,
            _workContext.Object,
            _notificationService.Object,
            _localizationService.Object,
            _downloadService.Object,
            _customerService.Object,
            _productService.Object,
            _jobRequirementService.Object,
            null,
            null,
            null,
            null,
            _turnService.Object,
            factory.Object);

        var outsideContainer = await controller.Recording(56);
        Assert.That(outsideContainer, Is.TypeOf<NotFoundResult>());

        var queryBearing = await controller.Recording(57);
        Assert.That(queryBearing, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public void ReportAndHistoryViews_IncludeRecordingAccessMarkup()
    {
        var reportText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_InterviewReportContent.cshtml"));
        var drawerText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_CandidateReportDrawer.cshtml"));
        var historyText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "History.cshtml")) +
            File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_MockInterviewHistoryContent.cshtml"));
        var myApplicationsText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MyApplications.cshtml")) +
            File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_MyApplicationsContent.cshtml"));

        Assert.That(reportText, Does.Contain("Plugins.Misc.AIInterview.Report.Recording"));
        Assert.That(historyText, Does.Contain("Plugins.Misc.AIInterview.Report.OpenRecording"));
        Assert.That(historyText, Does.Contain("ai-view-report-link"));
        Assert.That(historyText, Does.Contain("ai-icon-action"));
        Assert.That(historyText, Does.Contain("fa-solid fa-eye"));
        Assert.That(historyText, Does.Contain("ai-copy-share-link"));
        Assert.That(historyText, Does.Contain("ai-native-share-link"));
        Assert.That(historyText, Does.Contain("class=\"sr-only\">@viewReportText</span>"));
        Assert.That(myApplicationsText, Does.Contain("js-open-report-drawer"));
        Assert.That(myApplicationsText, Does.Not.Contain("Plugins.Misc.AIInterview.MyApplications.HistoryFootnote"));
        Assert.That(myApplicationsText, Does.Contain("class=\"button-2 ai-copy-share-link ai-icon-action\""));
        Assert.That(reportText, Does.Contain("class=\"button-2 ai-report-action ai-report-action-secondary ai-copy-share-link ai-icon-action\""));
        Assert.That(reportText, Does.Contain("class=\"button-2 ai-report-action ai-report-action-secondary ai-native-share-link aiinterview-hidden ai-icon-action\""));
        Assert.That(reportText, Does.Contain("var reportSubject ="));
        Assert.That(reportText, Does.Contain("var openRecordingLabel ="));
        Assert.That(reportText, Does.Contain("var copyShareLinkLabel ="));
        Assert.That(reportText, Does.Contain("var shareLabel ="));
        Assert.That(drawerText, Does.Contain("ai-report-drawer"));
        Assert.That(drawerText, Does.Contain("data-report-drawer-close"));
    }

    [Test]
    public async Task MyApplications_ReportLink_IsCorrectlyGenerated()
    {
        // Arrange
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var applications = new List<JobApplication>
        {
            new JobApplication { Id = 10, ProductId = 5, JobTitle = "Test Job" }
        };
        var sessions = new List<InterviewSession>
        {
            new InterviewSession { Id = 99, ProductId = 5, CompletedOnUtc = DateTime.UtcNow, Score = 85 }
        };

        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(applications);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(sessions);
        _sessionService.Setup(x => x.EnsureRecordingShareTokenAsync(It.IsAny<InterviewSession>())).ReturnsAsync((string)null);

        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>()))
            .Returns((Microsoft.AspNetCore.Mvc.Routing.UrlActionContext ctx) => ctx.Action == "ReportPanel" ? "panel-url" : "dummy-url");
        _controller.Url = urlHelperMock.Object;

        // Act
        var result = await _controller.MyApplications("LatestApplied");

        // Assert
        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (ApplicationListModel)viewResult.Model;
        var firstApp = model.Applications.First();

        Assert.That(firstApp.Id, Is.EqualTo(10)); // Id remains application ID
        Assert.That(firstApp.InterviewReportUrl, Is.EqualTo("dummy-url")); // Report URL is populated properly
        Assert.That(firstApp.InterviewReportPanelUrl, Is.EqualTo("panel-url"));
    }

    [Test]
    public async Task RecordingShareRoute_InvalidToken_ReturnsNotFound()
    {
        _sessionService.Setup(x => x.GetSessionByRecordingShareTokenAsync("missing-token")).ReturnsAsync((InterviewSession)null);

        var result = await _controller.RecordingShare("missing-token");

        Assert.That(result, Is.TypeOf<NotFoundResult>());
    }

    [Test]
    public async Task MyApplications_Uses_Actual_Turn_Data_For_Assessment_Preview()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);

        var applications = new List<JobApplication>
        {
            new JobApplication { Id = 10, ProductId = 5, JobTitle = "Test Job" }
        };
        var sessions = new List<InterviewSession>
        {
            new InterviewSession { Id = 99, ProductId = 5, CompletedOnUtc = DateTime.UtcNow, Score = 85, QuestionScores = "[85]" }
        };
        var turns = new List<InterviewTurn>
        {
            new InterviewTurn
            {
                Id = 1,
                InterviewSessionId = 99,
                SequenceNumber = 1,
                QuestionText = "What is dependency injection?",
                AnswerText = "It removes hard coupling.",
                Score = 85,
                Feedback = "Strong answer",
                AskedOnUtc = DateTime.UtcNow.AddMinutes(-2),
                AnsweredOnUtc = DateTime.UtcNow.AddMinutes(-1)
            }
        };

        _applicationService.Setup(x => x.GetJobApplicationsByCustomerIdAsync(customer.Id)).ReturnsAsync(applications);
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(sessions);
        _turnService.Setup(x => x.GetTurnsBySessionIdAsync(99)).ReturnsAsync(turns);

        var urlHelperMock = new Mock<Microsoft.AspNetCore.Mvc.IUrlHelper>();
        urlHelperMock.Setup(u => u.Action(It.IsAny<Microsoft.AspNetCore.Mvc.Routing.UrlActionContext>())).Returns("dummy-url");
        _controller.Url = urlHelperMock.Object;

        var result = await _controller.MyApplications("LatestApplied");

        Assert.That(result, Is.TypeOf<ViewResult>());
        var viewResult = (ViewResult)result;
        var model = (ApplicationListModel)viewResult.Model;
        var firstApp = model.Applications.First();

        Assert.That(firstApp.Turns, Has.Count.EqualTo(1));
        Assert.That(firstApp.Turns[0].SequenceNumber, Is.EqualTo(1));
        Assert.That(firstApp.Turns[0].QuestionText, Is.EqualTo("What is dependency injection?"));
        Assert.That(firstApp.Turns[0].Score, Is.EqualTo(85));
        Assert.That(firstApp.Turns[0].Feedback, Is.EqualTo("Strong answer"));
    }

    [Test]
    public async Task Interview_LegacyAction_Redirects_To_Runtime()
    {
        var customer = new Customer { Id = 1 };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _sessionService.Setup(x => x.GetSessionBySessionKeyAsync("legacy-key")).ReturnsAsync(new InterviewSession
        {
            Id = 20,
            CustomerId = 1,
            Token = "runtime-token",
            SessionKey = "legacy-key"
        });

        var result = await _controller.Interview("legacy-key");

        Assert.That(result, Is.TypeOf<RedirectToRouteResult>());
        var redirect = (RedirectToRouteResult)result;
        Assert.That(redirect.RouteName, Is.EqualTo(AIInterviewDefaults.MockRuntimeRouteName));
        Assert.That(redirect.RouteValues["token"], Is.EqualTo("runtime-token"));
    }

    [Test]
    public void RuntimeAndReportViews_UseSafeTextRendering()
    {
        var runtimeText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Runtime.cshtml"));
        var mockReportText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterview", "Report.cshtml"));
        var reportText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Report.cshtml"));
        var reportContentText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_InterviewReportContent.cshtml"));
        var drawerText = System.IO.File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_CandidateReportDrawer.cshtml"));

        Assert.That(runtimeText, Does.Contain("textContent"));
        Assert.That(runtimeText, Does.Not.Contain("card.innerHTML ="));
        Assert.That(runtimeText, Does.Contain("Unable to reach the interview service. Please check your network and try again."));
        Assert.That(runtimeText, Does.Not.Contain("console.log(\"Settings Config\")"));
        Assert.That(runtimeText, Does.Not.Contain("console.log(config)"));
        Assert.That(runtimeText, Does.Not.Contain("Body: ${text}"));
        Assert.That(runtimeText, Does.Contain("requestRecordingMediaForStart"));
        Assert.That(runtimeText, Does.Contain("await setCamera(true, true);"));
        Assert.That(runtimeText, Does.Contain("await setMic(true, true);"));
        Assert.That(runtimeText, Does.Contain("const ensureRuntimeTokenFresh = async () =>"));
        Assert.That(runtimeText, Does.Contain("Interview session token expired."));
        Assert.That(runtimeText, Does.Not.Contain("refreshTokenWithRetry"));
        Assert.That(runtimeText, Does.Not.Contain("scheduleTokenRefresh"));
        Assert.That(runtimeText, Does.Not.Contain("tokenRefreshPromise"));
        Assert.That(runtimeText, Does.Not.Contain("tokenRefreshInFlight"));
        Assert.That(runtimeText, Does.Not.Contain("applyTokenUpdate"));
        Assert.That(runtimeText, Does.Not.Contain("updateRuntimeUrlToken"));
        Assert.That(runtimeText, Does.Contain("showUnavailableQuestionState"));
        Assert.That(runtimeText, Does.Contain("const beginResult = await postForm(config.beginInterviewUrl"));
        Assert.That(runtimeText, Does.Contain("let interviewUnavailable = false;"));
        Assert.That(runtimeText, Does.Contain("normalized === 'AI service unavailable. Please try again later.'"));
        Assert.That(runtimeText, Does.Contain("const hasActiveQuestion = () => !interviewUnavailable && !isPlaceholderSpeechText(currentQuestionText());"));
        Assert.That(runtimeText, Does.Contain("const disableSubmit = answerBox.disabled"));
        Assert.That(runtimeText, Does.Contain("|| !interviewStarted"));
        Assert.That(runtimeText, Does.Contain("|| runtimeStoppedOrCompleted"));
        Assert.That(runtimeText, Does.Contain("|| stopInProgress"));
        Assert.That(runtimeText, Does.Contain("|| !isCameraActive()"));
        Assert.That(runtimeText, Does.Contain("|| !isMicActive()"));
        Assert.That(runtimeText, Does.Contain("|| !hasActiveQuestion()"));
        Assert.That(runtimeText, Does.Contain("|| isScreenShareBlockingInterview()"));
        Assert.That(runtimeText, Does.Contain("interviewUnavailable = true;"));
        Assert.That(runtimeText, Does.Contain("const autoSubmitDelaySeconds = 15;"));
        Assert.That(runtimeText, Does.Contain("const clearAnswerTimers = () =>"));
        Assert.That(runtimeText, Does.Not.Contain("const clearTokenRefreshTimer = () =>"));
        Assert.That(runtimeText, Does.Contain("const clearAllRuntimeTimers = () =>"));
        Assert.That(runtimeText, Does.Contain("id=\"screen-share-status\""));
        Assert.That(runtimeText, Does.Contain("id=\"screen-share-interruption-warning\""));
        Assert.That(runtimeText, Does.Contain("Resume screen share to continue."));
        Assert.That(runtimeText, Does.Contain("setScreenShareInterruptionWarning(true);"));
        Assert.That(runtimeText, Does.Contain("setScreenShareInterruptionWarning(false);"));
        Assert.That(runtimeText, Does.Not.Contain("Plugins.Misc.AIInterview.Runtime.ScreenSharingOptional"));
        Assert.That(runtimeText, Does.Contain("Entire screen sharing is required for this interview."));
        Assert.That(runtimeText, Does.Contain("Use full screen and keep the interview tab visible."));
        Assert.That(runtimeText, Does.Contain("Do not select a browser tab or a single window."));
        Assert.That(runtimeText, Does.Contain("runtime-screen-share-guide"));
        Assert.That(runtimeText, Does.Contain("Also share system audio"));
        Assert.That(runtimeText, Does.Contain("Screen sharing active"));
        Assert.That(runtimeText, Does.Contain("Screen sharing ended. Resume screen sharing to continue."));
        Assert.That(runtimeText, Does.Contain("Screen sharing resumed"));
        Assert.That(runtimeText, Does.Contain("let screenShareRequired = true;"));
        Assert.That(runtimeText, Does.Contain("const ensureRequiredMediaReady = async () =>"));
        Assert.That(runtimeText, Does.Contain("const shouldWarnBeforeUnload = () => interviewStarted && !runtimeStoppedOrCompleted && !stopInProgress;"));
        Assert.That(runtimeText, Does.Contain("window.addEventListener('beforeunload', (event) => {"));
        Assert.That(runtimeText, Does.Contain("if (!shouldWarnBeforeUnload())"));
        Assert.That(runtimeText, Does.Contain("runtime-log-panel"));
        Assert.That(runtimeText, Does.Contain("if (!trimmedAnswer)"));
        Assert.That(runtimeText, Does.Contain("Auto submitting..."));
        Assert.That(runtimeText, Does.Contain("Submit Answer (${countdownValue})"));
        Assert.That(runtimeText, Does.Not.Contain("answerStageTimer = setTimeout(() => {"));
        Assert.That(runtimeText, Does.Not.Contain("}, autoSubmitDelaySeconds * 1000);"));
        Assert.That(runtimeText, Does.Contain("fa-solid fa-robot"));
        Assert.That(runtimeText, Does.Contain("fa-solid fa-user"));
        Assert.That(runtimeText, Does.Contain("runtime-chat-message"));
        Assert.That(runtimeText, Does.Contain("runtime-chat-avatar"));
        Assert.That(runtimeText, Does.Contain("id=\"conversation\""));
        Assert.That(runtimeText, Does.Contain("toggle-screen-share"));
        Assert.That(runtimeText, Does.Contain("Please speak or type something."));
        Assert.That(runtimeText, Does.Contain("answerNeedsEditAfterFailure = true;"));
        Assert.That(runtimeText, Does.Contain("if (!isSuccess(result)) {"));
        Assert.That(runtimeText, Does.Not.Contain("answerNeedsEditAfterFailure = true;\r\n                updateSubmitAvailability();\r\n                resetTimers();"));
        Assert.That(runtimeText, Does.Contain("Speaking question."));
        Assert.That(runtimeText, Does.Contain("Speaking reminder."));
        Assert.That(runtimeText, Does.Contain("Question speech completed."));
        Assert.That(runtimeText, Does.Contain("Reminder shown."));
        Assert.That(runtimeText, Does.Contain("Reminder spoken."));
        Assert.That(runtimeText, Does.Contain("Reminder speech failed."));
        Assert.That(runtimeText, Does.Contain("if (!interviewStarted || isSpeakingOrSubmitting || !hasActiveQuestion() || answerNeedsEditAfterFailure || isScreenShareBlockingInterview())"));
        Assert.That(runtimeText, Does.Contain("startRuntimeTimer();"));
        Assert.That(runtimeText, Does.Contain("const updateStartButtonState = () =>"));
        Assert.That(runtimeText, Does.Contain("setButtonLabel(primaryActionButton, 'Submit Answer');"));
        Assert.That(runtimeText, Does.Contain("Interview Started"));
        Assert.That(runtimeText, Does.Not.Contain("startButton.textContent = 'Next Question';"));
        Assert.That(runtimeText, Does.Contain("const normalizeTurn = (turn, index = 0) =>"));
        Assert.That(runtimeText, Does.Contain("getValue(turn, 'questionText', 'QuestionText')"));
        Assert.That(runtimeText, Does.Contain("messageBox.textContent = isTerminated ? '' : 'Please answer the next question.';"));
        Assert.That(runtimeText, Does.Contain("prepareInterviewUrl"));
        Assert.That(runtimeText, Does.Contain("startPrepareInterview();"));
        Assert.That(runtimeText, Does.Contain("Preparing interviewer voice..."));
        Assert.That(runtimeText, Does.Contain("Recording start failed independently"));
        Assert.That(runtimeText, Does.Contain("Generating your report..."));
        Assert.That(runtimeText, Does.Contain("const finalizeRecordingBeforeCompletion = async () =>"));
        Assert.That(runtimeText, Does.Contain("finalizeRecordingBeforeCompletion()"));
        Assert.That(runtimeText, Does.Not.Contain("const startReportGenerationTimer = (reportUrl) =>"));
        Assert.That(runtimeText, Does.Not.Contain(".finally(() => startReportGenerationTimer(reportUrl));"));
        Assert.That(runtimeText, Does.Contain(".finally(() => updateReportButton(reportUrl));"));
        Assert.That(runtimeText, Does.Contain("timer.textContent = `Time remaining: ${formatCountdown(remainingSeconds)}`;"));
        Assert.That(runtimeText, Does.Not.Contain("messageBox.textContent = getValue(result, 'feedback', 'Feedback') || getRuntimeMessage(result, '') || '';"));
        Assert.That(runtimeText, Does.Contain("await stopRecording(true);"));
        Assert.That(runtimeText, Does.Not.Contain("setRecordingStatus('Recording ready.', false);"));
        Assert.That(runtimeText, Does.Not.Contain("setRecordingStatus('Recording waiting for screen share, camera, or microphone.', false);"));
        Assert.That(runtimeText, Does.Contain("Recording paused until screen sharing resumes."));
        Assert.That(runtimeText, Does.Contain("let preservedRecordingSegments = [];"));
        Assert.That(runtimeText, Does.Contain("const canStartRecording = () => {"));
        Assert.That(runtimeText, Does.Contain("if (screenShareRequired && (!screenShareActive || screenShareInterrupted))"));
        Assert.That(runtimeText, Does.Contain("const segments = [...preservedRecordingSegments];"));
        Assert.That(runtimeText, Does.Contain("await stopRecording(false, { preserveSegment: true, statusMessage: 'Recording restarting with resumed screen share.' });"));
        Assert.That(runtimeText, Does.Contain("acknowledgeGuidelinesUrl"));
        Assert.That(runtimeText, Does.Contain("sendGuidelinesAcknowledgementAudit"));
        Assert.That(runtimeText, Does.Contain("Mobile phones and tablets are not allowed by policy, but they are not blocked technically."));
        Assert.That(runtimeText, Does.Contain("Recording live."));
        Assert.That(runtimeText, Does.Not.Contain("Questions and answers appear here in order."));
        Assert.That(runtimeText, Does.Contain("~/Plugins/Misc.AIInterview/Content/css/aiinterview-public.css"));
        Assert.That(runtimeText, Does.Contain("Plugins.Misc.AIInterview.Runtime.MockMode.Warning"));
        var myApplicationsText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "MyApplications.cshtml")) +
            File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Shared", "_MyApplicationsContent.cshtml"));
        Assert.That(myApplicationsText, Does.Not.Contain("Q1 Relevancy"));
        Assert.That(myApplicationsText, Does.Not.Contain("Q1 Correctness"));
        Assert.That(myApplicationsText, Does.Not.Contain("Q1 Answer Score"));
        Assert.That(myApplicationsText, Does.Contain("js-open-report-drawer"));
        Assert.That(mockReportText, Does.Contain("_InterviewReportContent.cshtml"));
        Assert.That(reportText, Does.Contain("_InterviewReportContent.cshtml"));
        Assert.That(reportContentText, Does.Not.Contain("Html.Raw(Model.ReportData)"));
        Assert.That(reportContentText, Does.Not.Contain("Q@turn.SequenceNumber"));
        Assert.That(reportContentText, Does.Contain("Q@(turn.SequenceNumber)"));
        Assert.That(reportContentText, Does.Contain("Plugins.Misc.AIInterview.Report.TechnicalScore"));
        Assert.That(reportContentText, Does.Contain("Plugins.Misc.AIInterview.Report.Communication"));
        Assert.That(reportContentText, Does.Contain("Plugins.Misc.AIInterview.Report.Professionalism"));
        Assert.That(reportContentText, Does.Contain("Plugins.Misc.AIInterview.Report.PositiveAttitude"));
        Assert.That(reportContentText, Does.Contain("ai-copy-share-link"));
        Assert.That(reportContentText, Does.Contain("ai-native-share-link"));
        Assert.That(reportContentText, Does.Contain("ai-icon-action"));
        Assert.That(reportContentText, Does.Contain("class=\"sr-only\">@copyShareLinkLabel</span>"));
        Assert.That(reportContentText, Does.Contain("data-share-title=\"@recordingShareTitle\""));
        Assert.That(reportContentText, Does.Contain("title=\"@shareLabel\""));
        Assert.That(reportContentText, Does.Contain("aria-label=\"@openRecordingLabel\""));
        Assert.That(drawerText, Does.Contain("navigator.share"));
        Assert.That(drawerText, Does.Contain("Escape"));
        Assert.That(reportContentText, Does.Not.Contain(">Technical Score<"));
    }

    [Test]
    public void MockPracticeProductTemplate_ShowsStartProgressAndElapsedTimer()
    {
        var productTemplateText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "ProductTemplate.MockPractice.cshtml"));

        Assert.That(productTemplateText, Does.Contain("data-practice-start-progress=\"true\""));
        Assert.That(productTemplateText, Does.Contain("role=\"status\""));
        Assert.That(productTemplateText, Does.Contain("aria-live=\"polite\""));
        Assert.That(productTemplateText, Does.Contain("Starting interview..."));
        Assert.That(productTemplateText, Does.Contain("Uploading your resume and preparing your interview..."));
        Assert.That(productTemplateText, Does.Contain("Your resume is ready. We are preparing your interview questions..."));
        Assert.That(productTemplateText, Does.Contain("This is taking a little longer than usual. Please keep this page open."));
        Assert.That(productTemplateText, Does.Contain("formatElapsed"));
    }

    [Test]
    public void MockPracticeMappingRepair_ChangesOnlyRequiredSkillMappings_AndIsIdempotent()
    {
        var templates = new List<ProductTemplate>
        {
            new() { Id = 8, Name = "Practice Template", ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath },
            new() { Id = 9, Name = AIInterviewDefaults.JobProductTemplateName, ViewPath = AIInterviewDefaults.JobProductTemplateViewPath }
        };
        var products = new List<Product>
        {
            new() { Id = 100, ProductTemplateId = 8, Name = "Backend Engineer" },
            new() { Id = 200, ProductTemplateId = 9, Name = "Generic AI Interview Practice" }
        };
        var attributes = new List<ProductAttribute>
        {
            new() { Id = 1, Name = "Core Technical Skills" },
            new() { Id = 2, Name = AIInterviewDefaults.InterviewDifficultyAttributeName },
            new() { Id = 3, Name = "Practice Format" }
        };
        var mockSkill = new ProductAttributeMapping
        {
            Id = 11,
            ProductId = 100,
            ProductAttributeId = 1,
            TextPrompt = "Practice focus",
            IsRequired = true,
            DisplayOrder = 4
        };
        var mockDifficulty = new ProductAttributeMapping
        {
            Id = 12,
            ProductId = 100,
            ProductAttributeId = 2,
            TextPrompt = "Difficulty",
            IsRequired = true,
            DisplayOrder = 1
        };
        var compliantMockSkill = new ProductAttributeMapping
        {
            Id = 13,
            ProductId = 100,
            ProductAttributeId = 1,
            TextPrompt = "Practice Skill",
            IsRequired = false,
            DisplayOrder = 5
        };
        var unrelatedMockMapping = new ProductAttributeMapping
        {
            Id = 14,
            ProductId = 100,
            ProductAttributeId = 3,
            TextPrompt = "Format",
            IsRequired = true,
            DisplayOrder = 6
        };
        var jobSkill = new ProductAttributeMapping
        {
            Id = 15,
            ProductId = 200,
            ProductAttributeId = 1,
            TextPrompt = "Skill",
            IsRequired = true,
            DisplayOrder = 2
        };
        var mappings = new List<ProductAttributeMapping>
        {
            mockSkill,
            mockDifficulty,
            compliantMockSkill,
            unrelatedMockMapping,
            jobSkill
        };
        var dataProvider = new Mock<INopDataProvider>();
        dataProvider.Setup(provider => provider.GetTable<ProductTemplate>()).Returns(templates.AsQueryable());
        dataProvider.Setup(provider => provider.GetTable<Product>()).Returns(products.AsQueryable());
        dataProvider.Setup(provider => provider.GetTable<ProductAttribute>()).Returns(attributes.AsQueryable());
        dataProvider.Setup(provider => provider.GetTable<ProductAttributeMapping>()).Returns(mappings.AsQueryable());
        var migration = new MockPracticeSkillMappingOptionalMigration(dataProvider.Object);

        migration.Up();
        migration.Up();

        Assert.Multiple(() =>
        {
            Assert.That(mockSkill.IsRequired, Is.False);
            Assert.That(mockSkill.DisplayOrder, Is.EqualTo(4));
            Assert.That(mockDifficulty.IsRequired, Is.True);
            Assert.That(mockDifficulty.DisplayOrder, Is.EqualTo(1));
            Assert.That(compliantMockSkill.IsRequired, Is.False);
            Assert.That(unrelatedMockMapping.IsRequired, Is.True);
            Assert.That(jobSkill.IsRequired, Is.True);
            Assert.That(mappings, Has.Count.EqualTo(5));
            Assert.That(attributes, Has.Count.EqualTo(3));
        });
        dataProvider.Verify(provider => provider.UpdateEntity(mockSkill), Times.Once);
        dataProvider.Verify(provider => provider.UpdateEntity(It.IsAny<ProductAttributeMapping>()), Times.Once);
        dataProvider.Verify(provider => provider.InsertEntity(It.IsAny<ProductAttributeMapping>()), Times.Never);
        dataProvider.Verify(provider => provider.InsertEntity(It.IsAny<ProductAttribute>()), Times.Never);
    }

    [Test]
    public void MockPracticeProductTemplate_ExplainsRequiredAndAlternativeInputs_WithoutIndividualRequiredControls()
    {
        var productTemplateText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "ProductTemplate.MockPractice.cshtml"));
        var productAttributesText = File.ReadAllText(
            Path.GetFullPath(Path.Combine(
                TestFilePathHelper.GetPluginRootPath(),
                "..",
                "..",
                "Presentation",
                "Nop.Web",
                "Views",
                "Product",
                "_ProductAttributes.cshtml")));
        var publicCssText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Content", "css", "aiinterview-public.css"));
        var renderedControlSources = productTemplateText + Environment.NewLine + productAttributesText;
        var individualRequiredControl = System.Text.RegularExpressions.Regex.IsMatch(
            renderedControlSources,
            @"<(input|select|textarea)\b[^>]*\srequired(?:\s|=|/?>)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.Multiple(() =>
        {
            Assert.That(productTemplateText, Does.Contain("practice-difficulty-section"));
            Assert.That(productTemplateText, Does.Contain("<span>Difficulty</span><span class=\"practice-required-marker\" aria-hidden=\"true\">*</span>"));
            Assert.That(productTemplateText, Does.Contain("<span class=\"practice-required-text\">Required</span>"));
            Assert.That(productTemplateText, Does.Contain("<fieldset class=\"practice-alternative-inputs\""));
            Assert.That(productTemplateText, Does.Contain("<legend>Choose a Skill or Resume</legend>"));
            Assert.That(productTemplateText, Does.Contain("At least one is required: select a Skill, or select or upload a Resume."));
            Assert.That(productTemplateText, Does.Contain("Optional if you upload or select a resume"));
            Assert.That(productTemplateText, Does.Contain("Optional if you select a skill"));
            Assert.That(productTemplateText, Does.Contain("class=\"practice-or-divider\" role=\"separator\""));
            Assert.That(productTemplateText, Does.Contain("<span>OR</span>"));
            Assert.That(productTemplateText, Does.Contain("@Html.AntiForgeryToken()"));
            Assert.That(productTemplateText, Does.Contain("HtmlFieldPrefix = $\"attributes_{Model.Id}\""));
            Assert.That(productAttributesText, Does.Contain("name=\"@(controlId)\""));
            Assert.That(productTemplateText, Does.Contain("name=\"SelectedResumeDownloadId\""));
            Assert.That(productTemplateText, Does.Contain("name=\"ResumeFile\""));
            Assert.That(individualRequiredControl, Is.False);
            Assert.That(publicCssText, Does.Contain(".html-aiinterview-mock-practice-product-page .practice-required-marker"));
            Assert.That(publicCssText, Does.Contain("color: #c62828;"));
            Assert.That(publicCssText, Does.Contain(".html-aiinterview-mock-practice-product-page .practice-optional-badge"));
            Assert.That(publicCssText, Does.Contain(".html-aiinterview-mock-practice-product-page .practice-or-divider"));
            Assert.That(publicCssText, Does.Contain("@media (max-width: 420px)"));
        });
    }

    [Test]
    public void MockPracticeProductTemplate_ClassifiesDifficultySkillAndAdditionalAttributesIntoDistinctSections()
    {
        var productTemplateText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "ProductTemplate.MockPractice.cshtml"));
        var controllerText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Controllers", "MockAiInterviewController.cs"));
        var productAttributesText = File.ReadAllText(
            Path.GetFullPath(Path.Combine(
                TestFilePathHelper.GetPluginRootPath(),
                "..",
                "..",
                "Presentation",
                "Nop.Web",
                "Views",
                "Product",
                "_ProductAttributes.cshtml")));
        var publicCssText = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Content", "css", "aiinterview-public.css"));

        static IList<string> ExtractKeywords(string source, string declaration)
        {
            var declarationIndex = source.IndexOf(declaration, StringComparison.Ordinal);
            Assert.That(declarationIndex, Is.GreaterThanOrEqualTo(0), $"Missing keyword declaration: {declaration}");
            var initializerStart = source.IndexOf('[', declarationIndex);
            var initializerEnd = source.IndexOf("];", initializerStart, StringComparison.Ordinal);
            Assert.That(initializerStart, Is.GreaterThan(declarationIndex));
            Assert.That(initializerEnd, Is.GreaterThan(initializerStart));

            return System.Text.RegularExpressions.Regex
                .Matches(source[(initializerStart + 1)..initializerEnd], "\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value)
                .ToList();
        }

        static string NormalizeAttributeLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sanitized = new string(value
                .Trim()
                .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')
                .ToArray());
            return string.Join(" ", sanitized
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                .ToLowerInvariant();
        }

        static bool MatchesAttributeKeyword(
            (string Name, string TextPrompt, bool IsRequired) attribute,
            IEnumerable<string> keywords)
        {
            var normalizedCandidates = new[] { attribute.Name, attribute.TextPrompt }
                .Select(NormalizeAttributeLabel)
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return normalizedCandidates.Any(candidate => keywords.Any(keyword =>
            {
                var normalizedKeyword = NormalizeAttributeLabel(keyword);
                return string.Equals(candidate, normalizedKeyword, StringComparison.Ordinal) ||
                    candidate.Contains(normalizedKeyword, StringComparison.Ordinal);
            }));
        }

        var razorDifficultyKeywords = ExtractKeywords(productTemplateText, "string[] practiceDifficultyKeywords");
        var razorSkillKeywords = ExtractKeywords(productTemplateText, "string[] practiceSkillKeywords");
        var controllerDifficultyKeywords = ExtractKeywords(controllerText, "PracticeDifficultyKeywords =");
        var controllerSkillKeywords = ExtractKeywords(controllerText, "PracticeSkillKeywords =");
        var sourceAttributes = new[]
        {
            (Name: "Practice setup", TextPrompt: "Interview Difficulty", IsRequired: true),
            (Name: "Core technical skills", TextPrompt: "Practice focus", IsRequired: false),
            (Name: "Practice format", TextPrompt: "Question format", IsRequired: true)
        };
        var difficultyAttributes = sourceAttributes
            .Where(attribute => MatchesAttributeKeyword(attribute, razorDifficultyKeywords))
            .ToList();
        var skillAttributes = sourceAttributes
            .Where(attribute =>
                !difficultyAttributes.Contains(attribute) &&
                MatchesAttributeKeyword(attribute, razorSkillKeywords))
            .ToList();
        var additionalAttributes = sourceAttributes
            .Where(attribute =>
                !difficultyAttributes.Contains(attribute) &&
                !skillAttributes.Contains(attribute))
            .ToList();

        var difficultyPartialIndex = productTemplateText.IndexOf(
            "Html.PartialAsync(\"_ProductAttributes\", difficultyModel",
            StringComparison.Ordinal);
        var alternativeStartIndex = productTemplateText.IndexOf(
            "<fieldset class=\"practice-alternative-inputs\"",
            StringComparison.Ordinal);
        var guidanceIndex = productTemplateText.IndexOf(
            "class=\"practice-alternative-guidance\"",
            alternativeStartIndex,
            StringComparison.Ordinal);
        var skillPartialIndex = productTemplateText.IndexOf(
            "Html.PartialAsync(\"_ProductAttributes\", skillModel",
            alternativeStartIndex,
            StringComparison.Ordinal);
        var orDividerIndex = productTemplateText.IndexOf(
            "class=\"practice-or-divider\"",
            skillPartialIndex,
            StringComparison.Ordinal);
        var alternativeEndIndex = productTemplateText.IndexOf(
            "</fieldset>",
            alternativeStartIndex,
            StringComparison.Ordinal);
        var additionalSectionIndex = productTemplateText.IndexOf(
            "<section class=\"practice-additional-section\"",
            StringComparison.Ordinal);
        var additionalPartialIndex = productTemplateText.IndexOf(
            "Html.PartialAsync(\"_ProductAttributes\", additionalModel",
            additionalSectionIndex,
            StringComparison.Ordinal);
        var additionalSectionEndIndex = productTemplateText.IndexOf(
            "</section>",
            additionalSectionIndex,
            StringComparison.Ordinal);
        var additionalSectionText = productTemplateText[additionalSectionIndex..additionalSectionEndIndex];

        Assert.Multiple(() =>
        {
            Assert.That(razorDifficultyKeywords, Is.EqualTo(controllerDifficultyKeywords));
            Assert.That(razorSkillKeywords, Is.EqualTo(controllerSkillKeywords));
            Assert.That(difficultyAttributes.Select(attribute => attribute.Name), Is.EqualTo(new[] { "Practice setup" }));
            Assert.That(skillAttributes.Select(attribute => attribute.Name), Is.EqualTo(new[] { "Core technical skills" }));
            Assert.That(additionalAttributes.Select(attribute => attribute.Name), Is.EqualTo(new[] { "Practice format" }));
            Assert.That(additionalAttributes.Single().IsRequired, Is.True);
            Assert.That(productTemplateText, Does.Contain("MatchesAttributeKeyword(attribute, practiceDifficultyKeywords)"));
            Assert.That(productTemplateText, Does.Contain("MatchesAttributeKeyword(attribute, practiceSkillKeywords)"));
            Assert.That(productTemplateText, Does.Contain("!skillAttributes.Contains(attribute)"));
            Assert.That(productTemplateText, Does.Contain("ProductAttributes = difficultyAttributes"));
            Assert.That(productTemplateText, Does.Contain("ProductAttributes = skillAttributes"));
            Assert.That(productTemplateText, Does.Contain("ProductAttributes = additionalAttributes"));
            Assert.That(difficultyPartialIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(difficultyPartialIndex, Is.LessThan(alternativeStartIndex));
            Assert.That(guidanceIndex, Is.GreaterThan(alternativeStartIndex));
            Assert.That(skillPartialIndex, Is.GreaterThan(guidanceIndex));
            Assert.That(skillPartialIndex, Is.LessThan(orDividerIndex));
            Assert.That(orDividerIndex, Is.LessThan(alternativeEndIndex));
            Assert.That(additionalSectionIndex, Is.GreaterThan(alternativeEndIndex));
            Assert.That(additionalPartialIndex, Is.GreaterThan(additionalSectionIndex));
            Assert.That(additionalSectionText, Does.Not.Contain("practice-optional-badge"));
            Assert.That(additionalSectionText, Does.Not.Contain("practice-alternative-guidance"));
            Assert.That(additionalSectionText, Does.Contain("HtmlFieldPrefix = $\"attributes_{Model.Id}\""));
            Assert.That(productAttributesText, Does.Contain("@if (attribute.IsRequired)"));
            Assert.That(productAttributesText, Does.Contain("<span class=\"required\">*</span>"));
            Assert.That(publicCssText, Does.Contain(".html-aiinterview-mock-practice-product-page .practice-additional-section"));
            Assert.That(publicCssText, Does.Contain(".practice-additional-section .attributes .required"));
        });
    }

    [Test]
    public async Task WidgetView_Rendering_Works()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        var jobProductAccessService = new Mock<IJobProductAccessService>();
        _productService.Setup(x => x.GetProductByIdAsync(99))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 99, ProductTemplateId = 7 });
        jobProductAccessService.Setup(x => x.CanAcceptJobApplicationsAsync(It.IsAny<Product>())).ReturnsAsync(true);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(7))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 7,
                ViewPath = AIInterviewDefaults.JobProductTemplateViewPath
            });
        var component = new Nop.Plugin.Misc.AIInterview.Components.AIInterviewProductDetailsViewComponent(
            _creditService.Object,
            _workContext.Object,
            _customerService.Object,
            productAttributeService.Object,
            jobInterviewExperienceService.Object,
            _productService.Object,
            jobProductAccessService.Object,
            productTemplateService.Object,
            _applicationService.Object,
            _sessionService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/buy-credits" },
            _jobRequirementService.Object,
            _inviteService.Object,
            _downloadService.Object);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?sponsorToken=abc");

        var viewEngineMock = new Mock<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>();
        var requestServicesMock = new Mock<IServiceProvider>();
        requestServicesMock.Setup(s => s.GetService(typeof(ISponsorInviteService))).Returns(_inviteService.Object);
        requestServicesMock.Setup(s => s.GetService(typeof(Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine))).Returns(viewEngineMock.Object);
        httpContext.RequestServices = requestServicesMock.Object;

        var actionContext = new ActionContext();
        actionContext.HttpContext = httpContext;

        var viewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext();
        viewContext.HttpContext = httpContext;

        var viewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext();
        viewComponentContext.ViewContext = viewContext;

        component.ViewComponentContext = viewComponentContext;

        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(customer.Id)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());

        var sponsorInvite = new SponsorInvite { Id = 10, SponsorId = 2, ProductId = 99, Email = "test@example.com", InviteCode = "abc", ExpiryDateUtc = DateTime.UtcNow.AddDays(1), IsActive = true, MaxAttempts = 1 };
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("abc")).ReturnsAsync(sponsorInvite);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 5 });
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(10)).ReturnsAsync(0);

        // Act
        // Mock a Nop base model dynamic
        var productDetailsModel = new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 99 };
        productDetailsModel.ProductAttributes.Add(new Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeModel
        {
            Id = 14,
            Name = AIInterviewDefaults.InterviewDifficultyAttributeName,
            TextPrompt = AIInterviewDefaults.InterviewDifficultyAttributeName,
            AttributeControlType = Nop.Core.Domain.Catalog.AttributeControlType.RadioList,
            Values = new List<Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeValueModel>
            {
                new() { Id = 101, Name = "Easy" },
                new() { Id = 102, Name = "Medium", IsPreSelected = true }
            }
        });
        var result = await component.InvokeAsync("productdetails_before_collateral", productDetailsModel);

        // Assert
        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        var viewResult = (Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult)result;
        Assert.That(viewResult.ViewName, Is.EqualTo("~/Plugins/Misc.AIInterview/Views/Shared/Components/AIInterviewProductDetails/Default.cshtml"));

        // Assert viewbags
        Assert.That(component.ViewBag.HasSponsorCredits, Is.True);
        Assert.That(component.ViewBag.ProductId, Is.EqualTo(99));
        Assert.That(component.ViewBag.SponsorToken, Is.EqualTo("abc"));
        Assert.That(component.ViewBag.CreditPurchasePageUrl, Is.EqualTo("/buy-credits"));
        Assert.That(component.ViewBag.IsAuthenticated, Is.True);
    }

    [Test]
    public async Task WidgetView_GuestCustomerRecord_IsNotAuthenticated_And_DoesNotCreateWallet()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        var jobProductAccessService = new Mock<IJobProductAccessService>();
        _productService.Setup(x => x.GetProductByIdAsync(103))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 103, ProductTemplateId = 7 });
        jobProductAccessService.Setup(x => x.CanAcceptJobApplicationsAsync(It.IsAny<Product>())).ReturnsAsync(true);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(7))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 7,
                ViewPath = AIInterviewDefaults.JobProductTemplateViewPath
            });
        var component = new Nop.Plugin.Misc.AIInterview.Components.AIInterviewProductDetailsViewComponent(
            _creditService.Object,
            _workContext.Object,
            _customerService.Object,
            productAttributeService.Object,
            jobInterviewExperienceService.Object,
            _productService.Object,
            jobProductAccessService.Object,
            productTemplateService.Object,
            _applicationService.Object,
            _sessionService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/buy-credits" },
            _jobRequirementService.Object,
            _inviteService.Object,
            _downloadService.Object);

        component.ViewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext
        {
            ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext { HttpContext = new DefaultHttpContext() }
        };

        var guest = new Customer { Id = 88, Email = null };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(guest);
        _customerService.Setup(x => x.IsRegisteredAsync(guest, true)).ReturnsAsync(false);

        var productDetailsModel = new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 103 };
        productDetailsModel.ProductAttributes.Add(new Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeModel
        {
            Id = 14,
            Name = AIInterviewDefaults.InterviewDifficultyAttributeName,
            TextPrompt = AIInterviewDefaults.InterviewDifficultyAttributeName,
            AttributeControlType = Nop.Core.Domain.Catalog.AttributeControlType.RadioList
        });

        var result = await component.InvokeAsync("productdetails_before_collateral", productDetailsModel);

        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        Assert.That(component.ViewBag.IsAuthenticated, Is.False);
        _creditService.Verify(x => x.GetOrCreateWalletAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task WidgetView_DoesNotShowSponsorCredits_WhenInviteInactive()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        var jobProductAccessService = new Mock<IJobProductAccessService>();
        _productService.Setup(x => x.GetProductByIdAsync(100))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 100, ProductTemplateId = 7 });
        jobProductAccessService.Setup(x => x.CanAcceptJobApplicationsAsync(It.IsAny<Product>())).ReturnsAsync(true);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(7))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 7,
                ViewPath = AIInterviewDefaults.JobProductTemplateViewPath
            });
        var component = new Nop.Plugin.Misc.AIInterview.Components.AIInterviewProductDetailsViewComponent(
            _creditService.Object,
            _workContext.Object,
            _customerService.Object,
            productAttributeService.Object,
            jobInterviewExperienceService.Object,
            _productService.Object,
            jobProductAccessService.Object,
            productTemplateService.Object,
            _applicationService.Object,
            _sessionService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/buy-credits" },
            _jobRequirementService.Object,
            _inviteService.Object,
            _downloadService.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?sponsorToken=inactive-token");
        var viewEngineMock = new Mock<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>();
        var requestServicesMock = new Mock<IServiceProvider>();
        requestServicesMock.Setup(s => s.GetService(typeof(ISponsorInviteService))).Returns(_inviteService.Object);
        requestServicesMock.Setup(s => s.GetService(typeof(Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine))).Returns(viewEngineMock.Object);
        httpContext.RequestServices = requestServicesMock.Object;

        component.ViewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext
        {
            ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext { HttpContext = httpContext }
        };

        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(customer.Id)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("inactive-token"))
            .ReturnsAsync(new SponsorInvite
            {
                Id = 15,
                SponsorId = 2,
                Email = "test@example.com",
                InviteCode = "inactive-token",
                ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
                IsActive = false
            });

        var productDetailsModel = new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 100 };
        productDetailsModel.ProductAttributes.Add(new Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeModel
        {
            Id = 14,
            Name = AIInterviewDefaults.InterviewDifficultyAttributeName,
            TextPrompt = AIInterviewDefaults.InterviewDifficultyAttributeName,
            AttributeControlType = Nop.Core.Domain.Catalog.AttributeControlType.RadioList,
            Values = new List<Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeValueModel>
            {
                new() { Id = 101, Name = "Easy" },
                new() { Id = 102, Name = "Medium", IsPreSelected = true }
            }
        });

        var result = await component.InvokeAsync("productdetails_before_collateral", productDetailsModel);

        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        Assert.That(component.ViewBag.HasSponsorCredits, Is.False);
    }

    [Test]
    public async Task WidgetView_DoesNotShowSponsorCredits_WhenInviteAttemptsAreExhausted()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        var jobProductAccessService = new Mock<IJobProductAccessService>();
        _productService.Setup(x => x.GetProductByIdAsync(101))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 101, ProductTemplateId = 7 });
        jobProductAccessService.Setup(x => x.CanAcceptJobApplicationsAsync(It.IsAny<Product>())).ReturnsAsync(true);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(7))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 7,
                ViewPath = AIInterviewDefaults.JobProductTemplateViewPath
            });
        var component = new Nop.Plugin.Misc.AIInterview.Components.AIInterviewProductDetailsViewComponent(
            _creditService.Object,
            _workContext.Object,
            _customerService.Object,
            productAttributeService.Object,
            jobInterviewExperienceService.Object,
            _productService.Object,
            jobProductAccessService.Object,
            productTemplateService.Object,
            _applicationService.Object,
            _sessionService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/buy-credits" },
            _jobRequirementService.Object,
            _inviteService.Object,
            _downloadService.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?sponsorToken=exhausted-token");
        var viewEngineMock = new Mock<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>();
        var requestServicesMock = new Mock<IServiceProvider>();
        requestServicesMock.Setup(s => s.GetService(typeof(ISponsorInviteService))).Returns(_inviteService.Object);
        requestServicesMock.Setup(s => s.GetService(typeof(Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine))).Returns(viewEngineMock.Object);
        httpContext.RequestServices = requestServicesMock.Object;

        component.ViewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext
        {
            ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext { HttpContext = httpContext }
        };

        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(customer.Id)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _creditService.Setup(x => x.GetOrCreateWalletAsync(2)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(customer.Id)).ReturnsAsync(new List<InterviewSession>());
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(16)).ReturnsAsync(2);
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("exhausted-token"))
            .ReturnsAsync(new SponsorInvite
            {
                Id = 16,
                SponsorId = 2,
                ProductId = 101,
                Email = "test@example.com",
                InviteCode = "exhausted-token",
                ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
                IsActive = true,
                MaxAttempts = 2
            });

        var productDetailsModel = new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 101 };
        productDetailsModel.ProductAttributes.Add(new Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeModel
        {
            Id = 14,
            Name = AIInterviewDefaults.InterviewDifficultyAttributeName,
            TextPrompt = AIInterviewDefaults.InterviewDifficultyAttributeName,
            AttributeControlType = Nop.Core.Domain.Catalog.AttributeControlType.RadioList,
            Values = new List<Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeValueModel>
            {
                new() { Id = 101, Name = "Easy" },
                new() { Id = 102, Name = "Medium", IsPreSelected = true }
            }
        });

        var result = await component.InvokeAsync("productdetails_before_collateral", productDetailsModel);

        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        Assert.That(component.ViewBag.HasSponsorCredits, Is.False);
    }

    [Test]
    public async Task WidgetView_DoesNotShowSponsorCredits_WhenInviteIsForDifferentProduct()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        var jobProductAccessService = new Mock<IJobProductAccessService>();
        _productService.Setup(x => x.GetProductByIdAsync(102))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 102, ProductTemplateId = 7 });
        jobProductAccessService.Setup(x => x.CanAcceptJobApplicationsAsync(It.IsAny<Product>())).ReturnsAsync(true);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(7))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 7,
                ViewPath = AIInterviewDefaults.JobProductTemplateViewPath
            });
        var component = new Nop.Plugin.Misc.AIInterview.Components.AIInterviewProductDetailsViewComponent(
            _creditService.Object,
            _workContext.Object,
            _customerService.Object,
            productAttributeService.Object,
            jobInterviewExperienceService.Object,
            _productService.Object,
            jobProductAccessService.Object,
            productTemplateService.Object,
            _applicationService.Object,
            _sessionService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/buy-credits" },
            _jobRequirementService.Object,
            _inviteService.Object,
            _downloadService.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString("?sponsorToken=mismatch-token");
        var viewEngineMock = new Mock<Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine>();
        var requestServicesMock = new Mock<IServiceProvider>();
        requestServicesMock.Setup(s => s.GetService(typeof(ISponsorInviteService))).Returns(_inviteService.Object);
        requestServicesMock.Setup(s => s.GetService(typeof(Microsoft.AspNetCore.Mvc.ViewEngines.ICompositeViewEngine))).Returns(viewEngineMock.Object);
        httpContext.RequestServices = requestServicesMock.Object;

        component.ViewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext
        {
            ViewContext = new Microsoft.AspNetCore.Mvc.Rendering.ViewContext { HttpContext = httpContext }
        };

        var customer = new Customer { Id = 1, Email = "test@example.com" };
        _workContext.Setup(x => x.GetCurrentCustomerAsync()).ReturnsAsync(customer);
        _creditService.Setup(x => x.GetOrCreateWalletAsync(customer.Id)).ReturnsAsync(new CreditWallet { Balance = 10 });
        _inviteService.Setup(x => x.GetSponsorInviteByCodeAsync("mismatch-token"))
            .ReturnsAsync(new SponsorInvite
            {
                Id = 17,
                SponsorId = 2,
                ProductId = 999,
                Email = "test@example.com",
                InviteCode = "mismatch-token",
                ExpiryDateUtc = DateTime.UtcNow.AddDays(1),
                IsActive = true,
                MaxAttempts = 2
            });
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(17)).ReturnsAsync(0);

        var productDetailsModel = new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 102 };
        productDetailsModel.ProductAttributes.Add(new Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeModel
        {
            Id = 14,
            Name = AIInterviewDefaults.InterviewDifficultyAttributeName,
            TextPrompt = AIInterviewDefaults.InterviewDifficultyAttributeName,
            AttributeControlType = Nop.Core.Domain.Catalog.AttributeControlType.RadioList,
            Values = new List<Nop.Web.Models.Catalog.ProductDetailsModel.ProductAttributeValueModel>
            {
                new() { Id = 101, Name = "Easy" },
                new() { Id = 102, Name = "Medium", IsPreSelected = true }
            }
        });

        var result = await component.InvokeAsync("productdetails_before_collateral", productDetailsModel);

        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        Assert.That(component.ViewBag.HasSponsorCredits, Is.False);
    }

    [Test]
    public async Task WidgetView_DoesNotRenderForOrdinaryProductTemplate()
    {
        var productTemplateService = new Mock<IProductTemplateService>();
        var productAttributeService = new Mock<IProductAttributeService>();
        var jobInterviewExperienceService = new Mock<IJobInterviewExperienceService>();
        var jobProductAccessService = new Mock<IJobProductAccessService>();
        _productService.Setup(x => x.GetProductByIdAsync(99))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.Product { Id = 99, ProductTemplateId = 1 });
        jobProductAccessService.Setup(x => x.CanAcceptJobApplicationsAsync(It.IsAny<Product>())).ReturnsAsync(true);
        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(1))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 1,
                ViewPath = "ProductTemplate.Simple"
            });
        var component = new Nop.Plugin.Misc.AIInterview.Components.AIInterviewProductDetailsViewComponent(
            _creditService.Object,
            _workContext.Object,
            _customerService.Object,
            productAttributeService.Object,
            jobInterviewExperienceService.Object,
            _productService.Object,
            jobProductAccessService.Object,
            productTemplateService.Object,
            _applicationService.Object,
            _sessionService.Object,
            new AIInterviewSettings { CreditPurchasePageUrl = "/pricing" },
            _jobRequirementService.Object,
            _inviteService.Object,
            _downloadService.Object);

        var result = await component.InvokeAsync(
            "productdetails_before_collateral",
            new Nop.Web.Models.Catalog.ProductDetailsModel { Id = 99 });

        Assert.That(result.GetType().Name, Is.EqualTo("ContentViewComponentResult"));
    }
}
