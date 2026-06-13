using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Plugin.Misc.AIInterview.Components;
using Nop.Plugin.Misc.AIInterview.Controllers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Infrastructure;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Plugins;
using Nop.Services.Vendors;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Areas.Admin.Models.Catalog;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class AdminBaselineTests
{
    private Mock<ICreditService> _creditService;
    private Mock<ISponsorInviteService> _inviteService;
    private Mock<IApplicationService> _applicationService;
    private Mock<IInterviewSessionService> _sessionService;
    private Mock<ICustomerService> _customerService;
    private Mock<IProductService> _productService;
    private Mock<IVendorService> _vendorService;
    private Mock<ILocalizationService> _localizationService;
    private Mock<INotificationService> _notificationService;
    private Mock<ILogger<AIInterviewAdminController>> _logger;
    private Mock<IWorkContext> _workContext;
    private Mock<ISettingService> _settingService;
    private Mock<IRepository<CreditWallet>> _walletRepository;
    private Mock<IRepository<CreditLedgerEntry>> _ledgerRepository;
    private AIInterviewSettings _aiInterviewSettings;
    private MockAIInterviewSettings _mockAIInterviewSettings;
    private AIInterviewAdminController _controller;
    private MockAiInterviewAdminController _legacyController;

    [SetUp]
    public void SetUp()
    {
        _creditService = new Mock<ICreditService>();
        _inviteService = new Mock<ISponsorInviteService>();
        _applicationService = new Mock<IApplicationService>();
        _sessionService = new Mock<IInterviewSessionService>();
        _customerService = new Mock<ICustomerService>();
        _productService = new Mock<IProductService>();
        _vendorService = new Mock<IVendorService>();
        _localizationService = new Mock<ILocalizationService>();
        _notificationService = new Mock<INotificationService>();
        _logger = new Mock<ILogger<AIInterviewAdminController>>();
        _workContext = new Mock<IWorkContext>();
        _settingService = new Mock<ISettingService>();
        _walletRepository = new Mock<IRepository<CreditWallet>>();
        _ledgerRepository = new Mock<IRepository<CreditLedgerEntry>>();
        _aiInterviewSettings = new AIInterviewSettings
        {
            Enabled = true,
            MinimumScore = 10,
            Provider = "keep-provider",
            Model = "keep-model",
            Prompt = "keep-prompt",
            ServiceSettings = "keep-service",
            ApiKey = "keep-api",
            CreditProductSkuMappingsJson = "{\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10,\"AI-CREDIT-20\":20}",
            AzureOpenAiEndpointUrl = "keep-endpoint",
            AzureOpenAiApiKey = "keep-aoai-key",
            AzureOpenAiDeploymentOrModel = "keep-deploy",
            AzureSpeechKey = "keep-speech",
            AzureSpeechRegion = "keep-region",
            AzureBlobStorageContainerUrl = "keep-container",
            AzureBlobStorageSasToken = "keep-sas",
            CreditPurchasePageUrl = "keep-credits"
        };
        _mockAIInterviewSettings = new MockAIInterviewSettings { UseMockResponses = true };

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key);

        _workContext.Setup(x => x.GetCurrentCustomerAsync())
            .ReturnsAsync(new Customer { Id = 42, VendorId = 0, Email = "admin@example.com", FirstName = "Admin" });

        _controller = new AIInterviewAdminController(
            _creditService.Object,
            _inviteService.Object,
            _applicationService.Object,
            _sessionService.Object,
            _customerService.Object,
            _productService.Object,
            _vendorService.Object,
            _localizationService.Object,
            _notificationService.Object,
            _logger.Object,
            _workContext.Object,
            _settingService.Object,
            _walletRepository.Object,
            _ledgerRepository.Object,
            _aiInterviewSettings,
            _mockAIInterviewSettings);

        var defaultUrlHelper = new Mock<IUrlHelper>();
        defaultUrlHelper.Setup(x => x.Action(It.IsAny<UrlActionContext>()))
            .Returns("/admin/mock");
        defaultUrlHelper.Setup(x => x.RouteUrl(It.IsAny<UrlRouteContext>()))
            .Returns("/aiinterview/report/mock");
        _controller.Url = defaultUrlHelper.Object;

        _legacyController = new MockAiInterviewAdminController(
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
    public void AdminControllers_Are_Admin_Only()
    {
        Assert.That(Attribute.IsDefined(typeof(AIInterviewAdminController), typeof(AuthorizeAdminAttribute)), Is.True);
        Assert.That(Attribute.IsDefined(typeof(MockAiInterviewAdminController), typeof(AuthorizeAdminAttribute)), Is.True);
    }

    [Test]
    public async Task AdminMenu_Contains_Parent_And_Submenus()
    {
        var plugin = new Mock<IPlugin>();
        plugin.SetupGet(x => x.PluginDescriptor).Returns(new PluginDescriptor
        {
            SystemName = AIInterviewDefaults.SystemName,
            FriendlyName = "AI Interview"
        });
        plugin.Setup(x => x.GetConfigurationPageUrl()).Returns("/admin/AIInterview/Configure");

        var pluginManager = new Mock<IPluginManager<IPlugin>>();
        pluginManager.Setup(x => x.LoadPluginBySystemNameAsync(AIInterviewDefaults.SystemName)).ReturnsAsync(plugin.Object);
        pluginManager.Setup(x => x.IsPluginActive(plugin.Object, It.IsAny<List<string>>())).Returns(true);

        var urlHelper = new Mock<INopUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string routeName, object values, string protocol, string host, string fragment) => "/" + routeName);

        var consumer = new AdminMenuCreatedEventConsumer(pluginManager.Object, _localizationService.Object, urlHelper.Object);
        var root = new AdminMenuItem
        {
            ChildNodes = new List<AdminMenuItem>
            {
                new() { SystemName = "Configuration" }
            }
        };
        var eventMessage = new AdminMenuCreatedEvent(Mock.Of<IAdminMenu>(), root);

        await consumer.HandleEventAsync(eventMessage);

        var parent = root.ChildNodes.FirstOrDefault(item => item.SystemName == AIInterviewDefaults.AdminMenuSystemName);
        Assert.That(parent, Is.Not.Null);
        Assert.That(string.IsNullOrWhiteSpace(parent.Url), Is.True);
        Assert.That(parent.ChildNodes.Count, Is.EqualTo(6));
        Assert.That(parent.ChildNodes.Select(x => x.SystemName), Is.EquivalentTo(new[]
        {
            AIInterviewDefaults.AdminConfigureMenuSystemName,
            AIInterviewDefaults.AdminAiServiceMenuSystemName,
            AIInterviewDefaults.AdminSponsorInvitesMenuSystemName,
            AIInterviewDefaults.AdminVendorCreditsMenuSystemName,
            AIInterviewDefaults.AdminApplicantCreditsMenuSystemName,
            AIInterviewDefaults.AdminScoreboardMenuSystemName
        }));
    }

    [Test]
    public async Task Configure_Saves_Enabled_Only()
    {
        var model = new ConfigurationModel { Enabled = false };

        await _legacyController.Configure(model);

        Assert.That(_aiInterviewSettings.Enabled, Is.False);
        Assert.That(_aiInterviewSettings.MinimumScore, Is.EqualTo(10));
        Assert.That(_aiInterviewSettings.Provider, Is.EqualTo("keep-provider"));
        _settingService.Verify(x => x.SaveSettingAsync(It.IsAny<AIInterviewSettings>()), Times.Once);
    }

    [Test]
    public void General_Page_Is_No_Longer_Exposed()
    {
        Assert.That(typeof(AIInterviewAdminController).GetMethod("General"), Is.Null);
        Assert.That(typeof(MockAiInterviewAdminController).GetMethod("General"), Is.Null);
    }

    [Test]
    public void ProductDetailsViewComponent_Has_One_Public_Constructor()
    {
        var constructors = typeof(AIInterviewProductDetailsViewComponent).GetConstructors();
        Assert.That(constructors.Length, Is.EqualTo(1));
    }

    [Test]
    public void ProductRequirements_Partial_Contains_NopCard_Hide_Attributes()
    {
        var text = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Admin", "_ProductJobRequirements.cshtml"));

        Assert.That(text, Does.Contain("asp-hide-block-attribute-name=\"aiinterview-job-requirements\""));
        Assert.That(text, Does.Contain("asp-hide=\"false\""));
    }

    [Test]
    public async Task AiService_Page_Saves_And_Reloads()
    {
        _settingService.Setup(x => x.LoadSettingAsync<AIInterviewSettings>(0)).ReturnsAsync(_aiInterviewSettings);
        _settingService.Setup(x => x.LoadSettingAsync<MockAIInterviewSettings>(0)).ReturnsAsync(_mockAIInterviewSettings);

        var getResult = await _controller.AiService();
        var getModel = (AiServiceSettingsModel)((ViewResult)getResult).Model;
        Assert.That(getModel.UseMockResponses, Is.True);
        Assert.That(getModel.AvailableProviders, Has.Count.EqualTo(1));
        Assert.That(getModel.AvailableProviders.Single().Value, Is.EqualTo("Azure OpenAI"));
        Assert.That(getModel.AzureOpenAiEndpointUrl, Is.EqualTo("keep-endpoint"));
        Assert.That(getModel.CreditProductSkuMappingsJson, Does.Contain("AI-CREDIT-1"));
        Assert.That(getModel.CreditPurchasePageUrl, Is.EqualTo("keep-credits"));
        Assert.That(getModel.AzureBlobStorageContainerUrl, Is.EqualTo("keep-container"));
        Assert.That(getModel.AzureBlobStorageSasToken, Is.EqualTo("keep-sas"));

        var postResult = await _controller.AiService(new AiServiceSettingsModel
        {
            UseMockResponses = false,
            Provider = "Azure OpenAI",
            ApiKey = "key",
            Model = "gpt-4",
            Prompt = "prompt",
            ServiceSettings = "svc",
            CreditProductSkuMappingsJson = "{\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10,\"AI-CREDIT-20\":20}",
            CreditPurchasePageUrl = "/credits",
            AzureOpenAiEndpointUrl = "https://endpoint",
            AzureOpenAiApiKey = "aoai-key",
            AzureOpenAiDeploymentOrModel = "deployment",
            AzureSpeechKey = "speech",
            AzureSpeechRegion = "eastus",
            AzureBlobStorageContainerUrl = "",
            AzureBlobStorageSasToken = ""
        });

        Assert.That(postResult, Is.InstanceOf<RedirectToRouteResult>());
        Assert.That(((RedirectToRouteResult)postResult).RouteName, Is.EqualTo(AIInterviewDefaults.AdminAiServiceRouteName));

        var refreshed = await _controller.AiService();
        var refreshedModel = (AiServiceSettingsModel)((ViewResult)refreshed).Model;

        Assert.That(_mockAIInterviewSettings.UseMockResponses, Is.False);
        Assert.That(_aiInterviewSettings.Provider, Is.EqualTo("Azure OpenAI"));
        Assert.That(_aiInterviewSettings.AzureSpeechRegion, Is.EqualTo("eastus"));
        Assert.That(_aiInterviewSettings.AzureBlobStorageContainerUrl, Is.EqualTo(string.Empty));
        Assert.That(_aiInterviewSettings.AzureBlobStorageSasToken, Is.EqualTo(string.Empty));
        Assert.That(_aiInterviewSettings.CreditProductSkuMappingsJson, Does.Contain("AI-CREDIT-10"));
        Assert.That(_aiInterviewSettings.CreditPurchasePageUrl, Is.EqualTo("/credits"));
        Assert.That(refreshedModel.AzureOpenAiEndpointUrl, Is.EqualTo("https://endpoint"));
        Assert.That(refreshedModel.AzureOpenAiApiKey, Is.EqualTo("aoai-key"));
        Assert.That(refreshedModel.AzureOpenAiDeploymentOrModel, Is.EqualTo("deployment"));
        Assert.That(refreshedModel.AzureSpeechKey, Is.EqualTo("speech"));
        Assert.That(refreshedModel.AzureSpeechRegion, Is.EqualTo("eastus"));
        Assert.That(refreshedModel.AzureBlobStorageContainerUrl, Is.Empty);
        Assert.That(refreshedModel.AzureBlobStorageSasToken, Is.Empty);
        _settingService.Verify(x => x.SaveSettingAsync(It.IsAny<AIInterviewSettings>()), Times.AtLeastOnce);
        _settingService.Verify(x => x.SaveSettingAsync(It.IsAny<MockAIInterviewSettings>()), Times.Once);
    }

    [Test]
    public async Task AiService_Save_Exception_Returns_View_And_Shows_Error()
    {
        _settingService.Setup(x => x.SaveSettingAsync(It.IsAny<MockAIInterviewSettings>()))
            .Returns(Task.CompletedTask);
        _settingService.Setup(x => x.SaveSettingAsync(It.IsAny<AIInterviewSettings>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _controller.AiService(new AiServiceSettingsModel
        {
            UseMockResponses = true,
            Provider = "Azure OpenAI",
            ApiKey = "key",
            Model = "gpt-4",
            Prompt = "prompt",
            ServiceSettings = "svc",
            CreditProductSkuMappingsJson = "{\"AI-CREDIT-1\":1}",
            CreditPurchasePageUrl = "/credits",
            AzureOpenAiEndpointUrl = "https://endpoint",
            AzureOpenAiApiKey = "aoai-key",
            AzureOpenAiDeploymentOrModel = "deployment",
            AzureSpeechKey = "speech",
            AzureSpeechRegion = "eastus",
            AzureBlobStorageContainerUrl = "container",
            AzureBlobStorageSasToken = "sas"
        });

        Assert.That(result, Is.InstanceOf<ViewResult>());
        Assert.That(_controller.ModelState.IsValid, Is.False);
        _notificationService.Verify(x => x.ErrorNotification("Unable to save AI Interview service settings. Please check the values and try again."), Times.Once);
        _logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((_, _) => true),
            It.Is<Exception>(ex => ex.Message == "boom"),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
    }

    [Test]
    public void Upgrade_Locale_Resources_Include_AzureBlob_Keys()
    {
        var method = typeof(AIInterviewPlugin).GetMethod("GetUpgradeLocaleResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        var resources = (Dictionary<string, string>)method.Invoke(null, null);

        Assert.That(resources.ContainsKey("Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl"), Is.True);
        Assert.That(resources.ContainsKey("Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken"), Is.True);
        Assert.That(resources.ContainsKey("Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl.Hint"), Is.True);
        Assert.That(resources.ContainsKey("Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken.Hint"), Is.True);
    }

    [Test]
    public void AiService_View_Uses_Named_Route()
    {
        var text = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Admin", "AiService.cshtml"));

        Assert.That(text, Does.Contain("asp-route=\"@AIInterviewDefaults.AdminAiServiceRouteName\""));
        Assert.That(text, Does.Contain("Development mock mode is enabled. Azure OpenAI is bypassed."));
    }

    [Test]
    public void Configure_View_Uses_Named_Route()
    {
        var text = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Configure.cshtml"));

        Assert.That(text, Does.Contain("asp-route=\"@AIInterviewDefaults.ConfigurationRouteName\""));
    }

    [Test]
    public async Task Configure_Post_Returns_Redirect_To_Configuration_Route()
    {
        var result = await _legacyController.Configure(new ConfigurationModel { Enabled = true });

        Assert.That(result, Is.InstanceOf<RedirectToRouteResult>());
        Assert.That(((RedirectToRouteResult)result).RouteName, Is.EqualTo(AIInterviewDefaults.ConfigurationRouteName));
    }

    [Test]
    public async Task SponsorInvites_Page_Populates_Dropdowns_And_Row_Links()
    {
        var product = new Product { Id = 55, Name = "Senior Backend Engineer", VendorId = 17 };
        var vendor = new Vendor { Id = 17, Name = "Acme Hiring", Email = "vendor@example.com", PmCustomerId = 42 };
        _inviteService.Setup(x => x.GetSponsorInvitesAsync(0)).ReturnsAsync(new List<SponsorInvite>
        {
            new() { Id = 1, SponsorId = 42, ProductId = 55, Email = "candidate@example.com", IsActive = true, CreatedOnUtc = DateTime.UtcNow }
        });
        _productService.Setup(x => x.SearchProductsAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<IList<int>>(),
                It.IsAny<IList<int>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<ProductType?>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<decimal?>(),
                It.IsAny<decimal?>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<IList<SpecificationAttributeOption>>(),
                It.IsAny<ProductSortingEnum>(),
                It.IsAny<bool>(),
                It.IsAny<bool?>()))
            .ReturnsAsync(new Nop.Core.PagedList<Product>(new List<Product> { product }, 0, 1, 1));
        _productService.Setup(x => x.GetProductsByIdsAsync(It.IsAny<int[]>()))
            .ReturnsAsync(new List<Product> { product });
        _vendorService.Setup(x => x.GetAllVendorsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new Nop.Core.PagedList<Vendor>(new List<Vendor> { vendor }, 0, 1, 1));

        var result = await _controller.SponsorInvites();
        var model = (SponsorInviteAdminModel)((ViewResult)result).Model;

        Assert.That(model.AvailableProducts, Has.Count.EqualTo(1));
        Assert.That(model.AvailableSponsors, Has.Count.EqualTo(1));
        Assert.That(model.Invites.Single().ProductName, Is.EqualTo("Senior Backend Engineer"));
        Assert.That(model.Invites.Single().VendorName, Is.EqualTo("Acme Hiring"));
        Assert.That(model.Invites.Single().ProductAdminUrl, Is.Not.Empty);
        Assert.That(model.Invites.Single().VendorAdminUrl, Is.Not.Empty);
    }

    [Test]
    public async Task AiService_Invalid_Json_Shows_Error_And_Does_Not_Save()
    {
        var result = await _controller.AiService(new AiServiceSettingsModel
        {
            UseMockResponses = false,
            Provider = "OpenAI",
            ApiKey = "key",
            Model = "gpt-4",
            Prompt = "prompt",
            ServiceSettings = "svc",
            CreditProductSkuMappingsJson = "{invalid json",
            CreditPurchasePageUrl = "/credits",
            AzureOpenAiEndpointUrl = "https://endpoint",
            AzureOpenAiApiKey = "aoai-key",
            AzureOpenAiDeploymentOrModel = "deployment",
            AzureSpeechKey = "speech",
            AzureSpeechRegion = "eastus",
            AzureBlobStorageContainerUrl = "container",
            AzureBlobStorageSasToken = "sas"
        });

        Assert.That(result, Is.InstanceOf<ViewResult>());
        Assert.That(_controller.ModelState.IsValid, Is.False);
        _notificationService.Verify(x => x.ErrorNotification(It.IsAny<string>()), Times.Once);
        _settingService.Verify(x => x.SaveSettingAsync(It.IsAny<AIInterviewSettings>()), Times.Never);
    }

    [Test]
    public async Task SaveProductRequirements_Saves_Job_Flags_For_Existing_Product()
    {
        var jobRequirementService = new Mock<IJobRequirementService>();
        jobRequirementService.Setup(x => x.SaveRequirementsAsync(It.IsAny<Product>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<decimal>()))
            .Returns(Task.CompletedTask);
        _productService.Setup(x => x.GetProductByIdAsync(55))
            .ReturnsAsync(new Product { Id = 55, ProductTemplateId = 7 });

        var controller = new AIInterviewAdminController(
            _creditService.Object,
            _inviteService.Object,
            _applicationService.Object,
            _sessionService.Object,
            _customerService.Object,
            _productService.Object,
            _vendorService.Object,
            _localizationService.Object,
            _notificationService.Object,
            _logger.Object,
            _workContext.Object,
            _settingService.Object,
            _walletRepository.Object,
            _ledgerRepository.Object,
            _aiInterviewSettings,
            _mockAIInterviewSettings,
            jobRequirementService.Object);

        var result = await controller.SaveProductRequirements(new JobRequirementsModel
        {
            ProductId = 55,
            ResumeRequired = true,
            InterviewRequired = false,
            MinimumScore = 88
        });

        Assert.That(result, Is.TypeOf<JsonResult>());
        jobRequirementService.Verify(x => x.SaveRequirementsAsync(It.Is<Product>(product => product.Id == 55), true, false, 88m), Times.Once);
    }

    [Test]
    public async Task ProductRequirementsWidget_Reads_MinimumScore_From_Product_Attributes()
    {
        var product = new Product { Id = 60, ProductTemplateId = 7 };
        _productService.Setup(x => x.GetProductByIdAsync(60)).ReturnsAsync(product);
        var jobRequirementService = new Mock<IJobRequirementService>();
        jobRequirementService.Setup(x => x.GetRequirementsAsync(product))
            .ReturnsAsync(new JobRequirementsModel
            {
                IsJobProduct = true,
                ResumeRequired = true,
                InterviewRequired = false,
                MinimumScore = 64.5m
            });

        var component = new AIInterviewAdminProductRequirementsViewComponent(_productService.Object, new Mock<IProductTemplateService>().Object, jobRequirementService.Object);
        var result = await component.InvokeAsync("", new ProductModel { Id = 60 });

        Assert.That(result, Is.TypeOf<Microsoft.AspNetCore.Mvc.ViewComponents.ViewViewComponentResult>());
        Assert.That(component.ViewBag.MinimumScore, Is.EqualTo(64.5m));
    }

    [Test]
    public async Task ProductRequirementsEventConsumer_Saves_Posted_Flags_On_Insert_And_Update()
    {
        var jobRequirementService = new Mock<IJobRequirementService>();
        jobRequirementService.Setup(x => x.IsJobProductAsync(It.IsAny<Product>())).ReturnsAsync(true);
        jobRequirementService.Setup(x => x.SaveRequirementsAsync(It.IsAny<Product>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<decimal>()))
            .Returns(Task.CompletedTask);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Features.Set<IFormFeature>(new FormFeature(new FormCollection(new Dictionary<string, StringValues>
            {
                ["AIInterviewJobResumeRequired"] = new StringValues(new[] { "false", "true" }),
                ["AIInterviewJobInterviewRequired"] = new StringValues("false"),
                ["AIInterviewJobMinimumScore"] = new StringValues("77.5")
            })));

        var consumer = new ProductRequirementsEventConsumer(new HttpContextAccessor { HttpContext = context }, jobRequirementService.Object);
        var product = new Product { Id = 77, ProductTemplateId = 7 };

        await consumer.HandleEventAsync(new Nop.Core.Events.EntityInsertedEvent<Product>(product));
        await consumer.HandleEventAsync(new Nop.Core.Events.EntityUpdatedEvent<Product>(product));

        jobRequirementService.Verify(x => x.SaveRequirementsAsync(product, true, false, 77.5m), Times.Exactly(2));
    }

    [Test]
    public async Task SponsorInvites_Bulk_Parsing_Creates_Valid_Invites_And_Rejects_Invalid()
    {
        _inviteService.Setup(x => x.GetSponsorInvitesAsync(0)).ReturnsAsync(new List<SponsorInvite>());
        _customerService.Setup(x => x.IsAdminAsync(It.IsAny<Customer>())).ReturnsAsync(true);
        _customerService.Setup(x => x.GetCustomerByIdAsync(42)).ReturnsAsync(new Customer { Id = 42, VendorId = 0 });
        _productService.Setup(x => x.GetProductByIdAsync(99)).ReturnsAsync(new Product { Id = 99, VendorId = 0 });
        _inviteService.Setup(x => x.CreateInviteAsync(42, "first@example.com", 99, 3, null)).Returns(Task.CompletedTask);
        _inviteService.Setup(x => x.CreateInviteAsync(42, "second@example.com", 99, 3, null)).Returns(Task.CompletedTask);

        var result = await _controller.SponsorInvites(new SponsorInviteAdminModel
        {
            BulkEmails = "first@example.com;invalid-email\nsecond@example.com",
            ProductId = 99,
            MaxAttempts = 3
        });

        Assert.That(result, Is.InstanceOf<ViewResult>());
        _inviteService.Verify(x => x.CreateInviteAsync(42, "first@example.com", 99, 3, null), Times.Once);
        _inviteService.Verify(x => x.CreateInviteAsync(42, "second@example.com", 99, 3, null), Times.Once);
    }

    [Test]
    public async Task SponsorInvites_Show_Active_Expired_And_Inactive_Statuses()
    {
        var invites = new List<SponsorInvite>
        {
            new() { Id = 1, ProductId = 55, Email = "active@example.com", IsActive = true, IsAccepted = false, MaxAttempts = 2, ExpiryDateUtc = DateTime.UtcNow.AddDays(1), CreatedOnUtc = DateTime.UtcNow.AddHours(-1) },
            new() { Id = 2, ProductId = 55, Email = "expired@example.com", IsActive = true, IsAccepted = false, MaxAttempts = 2, ExpiryDateUtc = DateTime.UtcNow.AddDays(-1), CreatedOnUtc = DateTime.UtcNow.AddHours(-2) },
            new() { Id = 3, ProductId = 55, Email = "inactive@example.com", IsActive = false, IsAccepted = false, MaxAttempts = 2, ExpiryDateUtc = DateTime.UtcNow.AddDays(1), CreatedOnUtc = DateTime.UtcNow.AddHours(-3) },
            new() { Id = 4, ProductId = 55, Email = "accepted@example.com", IsActive = true, IsAccepted = true, MaxAttempts = 1, ExpiryDateUtc = DateTime.UtcNow.AddDays(1), CreatedOnUtc = DateTime.UtcNow.AddHours(-4) },
            new() { Id = 5, ProductId = 55, Email = "accepted2@example.com", IsActive = true, IsAccepted = false, MaxAttempts = 3, ExpiryDateUtc = DateTime.UtcNow.AddDays(1), CreatedOnUtc = DateTime.UtcNow.AddHours(-5) }
        };

        _inviteService.Setup(x => x.GetSponsorInvitesAsync(0)).ReturnsAsync(invites);
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(1)).ReturnsAsync(0);
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(2)).ReturnsAsync(0);
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(3)).ReturnsAsync(0);
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(4)).ReturnsAsync(1);
        _sessionService.Setup(x => x.GetSponsorInviteAttemptCountAsync(5)).ReturnsAsync(1);

        var result = await _controller.SponsorInvites();

        var model = (SponsorInviteAdminModel)((ViewResult)result).Model;
        Assert.That(model.Invites.Single(x => x.Id == 1).StatusText, Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Active"));
        Assert.That(model.Invites.Single(x => x.Id == 2).StatusText, Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Expired"));
        Assert.That(model.Invites.Single(x => x.Id == 3).StatusText, Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Inactive"));
        Assert.That(model.Invites.Single(x => x.Id == 4).StatusText, Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Exhausted"));
        Assert.That(model.Invites.Single(x => x.Id == 5).StatusText, Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Accepted"));
    }

    [Test]
    public async Task VendorCredits_TopUp_Calls_AddCredit()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(101))
            .ReturnsAsync(new Customer { Id = 101, VendorId = 11 });

        var result = await _controller.VendorCredits(new CreditManagementModel
        {
            CustomerId = 101,
            Amount = 25
        });

        Assert.That(result, Is.InstanceOf<ViewResult>());
        _creditService.Verify(x => x.AddCreditAsync(101, 25, "Plugins.Misc.AIInterview.Admin.TopUp.Remarks"), Times.Once);
    }

    [Test]
    public async Task VendorCredits_Get_Populates_Vendor_Dropdown()
    {
        _vendorService.Setup(x => x.GetAllVendorsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .ReturnsAsync(new Nop.Core.PagedList<Vendor>(new List<Vendor>
            {
                new() { Id = 11, Name = "Vendor One", Email = "vendor1@example.com", PmCustomerId = 101 }
            }, 0, 1, 1));

        var result = await _controller.VendorCredits();
        var model = (CreditManagementModel)((ViewResult)result).Model;

        Assert.That(model.AvailableCustomers, Has.Count.EqualTo(1));
        Assert.That(model.AvailableCustomers.Single().Value, Is.EqualTo("101"));
    }

    [Test]
    public async Task VendorCredits_Rejects_ApplicantCustomer()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(201))
            .ReturnsAsync(new Customer { Id = 201, VendorId = 0 });

        await _controller.VendorCredits(new CreditManagementModel
        {
            CustomerId = 201,
            Amount = 25
        });

        _notificationService.Verify(x => x.ErrorNotification("The selected customer is not a vendor account."), Times.Once);
        _creditService.Verify(x => x.AddCreditAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        _creditService.Verify(x => x.GetOrCreateWalletAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task ApplicantCredits_Rejects_VendorCustomer()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(301))
            .ReturnsAsync(new Customer { Id = 301, VendorId = 12 });

        await _controller.ApplicantCredits(new CreditManagementModel
        {
            CustomerId = 301,
            Amount = 25
        });

        _notificationService.Verify(x => x.ErrorNotification("The selected customer is not an applicant account."), Times.Once);
        _creditService.Verify(x => x.AddCreditAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        _creditService.Verify(x => x.GetOrCreateWalletAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task ApplicantCredits_Valid_TopUp_Calls_AddCredit()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(202))
            .ReturnsAsync(new Customer { Id = 202, VendorId = 0 });

        var result = await _controller.ApplicantCredits(new CreditManagementModel
        {
            CustomerId = 202,
            Amount = 15
        });

        Assert.That(result, Is.InstanceOf<ViewResult>());
        _creditService.Verify(x => x.AddCreditAsync(202, 15, "Plugins.Misc.AIInterview.Admin.TopUp.Remarks"), Times.Once);
    }

    [Test]
    public async Task ApplicantCredits_Get_Populates_Customer_Dropdown()
    {
        _customerService.Setup(x => x.GetAllCustomersAsync(
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new Nop.Core.PagedList<Customer>(new List<Customer>
            {
                new() { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 }
            }, 0, 1, 1));

        var result = await _controller.ApplicantCredits();
        var model = (CreditManagementModel)((ViewResult)result).Model;

        Assert.That(model.AvailableCustomers, Has.Count.EqualTo(1));
        Assert.That(model.AvailableCustomers.Single().Value, Is.EqualTo("202"));
    }

    [Test]
    public async Task ApplicantCredits_InvalidAmount_Returns_Error()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(202))
            .ReturnsAsync(new Customer { Id = 202, VendorId = 0 });

        await _controller.ApplicantCredits(new CreditManagementModel
        {
            CustomerId = 202,
            Amount = 0
        });

        _notificationService.Verify(x => x.ErrorNotification("Invalid top-up amount."), Times.Once);
        _creditService.Verify(x => x.GetOrCreateWalletAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task VendorCredits_MissingCustomer_DoesNotCreateWallet()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(404)).ReturnsAsync((Customer)null);

        await _controller.VendorCredits(new CreditManagementModel
        {
            CustomerId = 404,
            Amount = 25
        });

        _notificationService.Verify(x => x.ErrorNotification("Customer is required."), Times.Once);
        _creditService.Verify(x => x.AddCreditAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        _creditService.Verify(x => x.GetOrCreateWalletAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task Scoreboard_Export_Works()
    {
        var customer = new Customer { Id = 5, FirstName = "Casey", LastName = "Jones", Email = "casey@example.com" };
        var product = new Product { Id = 9, Name = "Platform Engineer", VendorId = 3 };
        var vendor = new Vendor { Id = 3, Name = "Example Vendor" };
        var application = new JobApplication { Id = 1, CustomerId = 5, ProductId = 9, JobTitle = "Platform Engineer", Status = "Reviewed", CreatedOnUtc = DateTime.UtcNow.AddDays(-1) };
        var session = new InterviewSession { Id = 77, CustomerId = 5, ProductId = 9, JobApplicationId = 1, CompletedOnUtc = DateTime.UtcNow, Score = 88 };

        _applicationService.Setup(x => x.GetApplicationsAsync(null, null, null, null, null, null, 0, 0, 0, int.MaxValue, false))
            .ReturnsAsync(new Nop.Core.PagedList<JobApplication>(new List<JobApplication> { application }, 0, 1, 1));
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(5)).ReturnsAsync(new List<InterviewSession> { session });
        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.Is<int[]>(ids => ids.Contains(5)))).ReturnsAsync(new List<Customer> { customer });
        _productService.Setup(x => x.GetProductsByIdsAsync(It.Is<int[]>(ids => ids.Contains(9)))).ReturnsAsync(new List<Product> { product });
        _vendorService.Setup(x => x.GetVendorByIdAsync(3)).ReturnsAsync(vendor);
        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(x => x.RouteUrl(It.IsAny<UrlRouteContext>())).Returns("/aiinterview/report/77");
        _controller.Url = urlHelper.Object;

        var result = await _controller.ScoreboardExportCsv(new ScoreboardFilterModel());

        Assert.That(result, Is.InstanceOf<FileContentResult>());
        var file = (FileContentResult)result;
        var text = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.That(text, Does.Contain("Candidate"));
        Assert.That(text, Does.Contain("Casey Jones"));
        Assert.That(text, Does.Contain("Example Vendor"));
    }

    [Test]
    public async Task Scoreboard_Page_Populates_Status_Dropdown_And_Admin_Links()
    {
        var customer = new Customer { Id = 5, FirstName = "Casey", LastName = "Jones", Email = "casey@example.com" };
        var product = new Product { Id = 9, Name = "Platform Engineer", VendorId = 3 };
        var vendor = new Vendor { Id = 3, Name = "Example Vendor", PmCustomerId = 88 };
        var application = new JobApplication { Id = 1, CustomerId = 5, ProductId = 9, JobTitle = "Platform Engineer", Status = "Reviewed", CreatedOnUtc = DateTime.UtcNow.AddDays(-1) };
        var session = new InterviewSession { Id = 77, CustomerId = 5, ProductId = 9, JobApplicationId = 1, CompletedOnUtc = DateTime.UtcNow, Score = 88 };

        _applicationService.Setup(x => x.GetApplicationsAsync(null, null, null, null, null, null, 0, 0, 0, int.MaxValue, false))
            .ReturnsAsync(new Nop.Core.PagedList<JobApplication>(new List<JobApplication> { application }, 0, 1, 1));
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(5)).ReturnsAsync(new List<InterviewSession> { session });
        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.Is<int[]>(ids => ids.Contains(5)))).ReturnsAsync(new List<Customer> { customer });
        _productService.Setup(x => x.GetProductsByIdsAsync(It.Is<int[]>(ids => ids.Contains(9)))).ReturnsAsync(new List<Product> { product });
        _vendorService.Setup(x => x.GetVendorByIdAsync(3)).ReturnsAsync(vendor);

        var result = await _controller.Scoreboard(new ScoreboardFilterModel());
        var model = (ScoreboardFilterModel)((ViewResult)result).Model;

        Assert.That(model.AvailableStatuses, Has.Count.EqualTo(JobApplicationStatuses.All.Length + 1));
        Assert.That(model.Rows.Single().CandidateAdminUrl, Is.Not.Empty);
        Assert.That(model.Rows.Single().VendorAdminUrl, Is.Not.Empty);
        Assert.That(model.Rows.Single().ProductAdminUrl, Is.Not.Empty);
    }

    [Test]
    public async Task Scoreboard_Filters_By_Candidate_Vendor_Job_Status_And_Score()
    {
        var customer = new Customer { Id = 5, FirstName = "Casey", LastName = "Jones", Email = "casey@example.com" };
        var product = new Product { Id = 9, Name = "Platform Engineer", VendorId = 3 };
        var vendor = new Vendor { Id = 3, Name = "Example Vendor" };
        var matchingApplication = new JobApplication { Id = 1, CustomerId = 5, ProductId = 9, JobTitle = "Platform Engineer", Status = "Reviewed", CreatedOnUtc = DateTime.UtcNow.AddDays(-1) };
        var otherApplication = new JobApplication { Id = 2, CustomerId = 6, ProductId = 10, JobTitle = "Other Job", Status = "Rejected", CreatedOnUtc = DateTime.UtcNow.AddDays(-2) };
        var matchingSession = new InterviewSession { Id = 77, CustomerId = 5, ProductId = 9, JobApplicationId = 1, CompletedOnUtc = DateTime.UtcNow, Score = 88 };
        var otherSession = new InterviewSession { Id = 78, CustomerId = 6, ProductId = 10, JobApplicationId = 2, CompletedOnUtc = DateTime.UtcNow, Score = 40 };

        _applicationService.Setup(x => x.GetApplicationsAsync(null, null, null, null, null, null, 0, 0, 0, int.MaxValue, false))
            .ReturnsAsync(new Nop.Core.PagedList<JobApplication>(new List<JobApplication> { matchingApplication, otherApplication }, 0, 2, 2));
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(5)).ReturnsAsync(new List<InterviewSession> { matchingSession });
        _sessionService.Setup(x => x.GetSessionsByCustomerIdAsync(6)).ReturnsAsync(new List<InterviewSession> { otherSession });
        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.Is<int[]>(ids => ids.Contains(5) || ids.Contains(6)))).ReturnsAsync(new List<Customer> { customer, new Customer { Id = 6, FirstName = "Other", LastName = "User", Email = "other@example.com" } });
        _productService.Setup(x => x.GetProductsByIdsAsync(It.Is<int[]>(ids => ids.Contains(9) || ids.Contains(10)))).ReturnsAsync(new List<Product> { product, new Product { Id = 10, Name = "Other Job", VendorId = 4 } });
        _vendorService.Setup(x => x.GetVendorByIdAsync(3)).ReturnsAsync(vendor);
        _vendorService.Setup(x => x.GetVendorByIdAsync(4)).ReturnsAsync(new Vendor { Id = 4, Name = "Other Vendor" });

        var result = await _controller.Scoreboard(new ScoreboardFilterModel
        {
            Candidate = "Casey",
            Vendor = "Example",
            JobPosting = "Platform",
            Status = "Reviewed",
            MinScore = 80,
            MaxScore = 90,
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(1)
        });

        var model = (ScoreboardFilterModel)((ViewResult)result).Model;
        Assert.That(model.Rows, Has.Count.EqualTo(1));
        Assert.That(model.Rows.Single().CandidateName, Is.EqualTo("Casey Jones"));
        Assert.That(model.Rows.Single().VendorName, Is.EqualTo("Example Vendor"));
        Assert.That(model.Rows.Single().JobTitle, Is.EqualTo("Platform Engineer"));
    }
}
