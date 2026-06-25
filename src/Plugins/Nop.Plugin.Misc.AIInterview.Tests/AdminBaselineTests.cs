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
    private Mock<IRepository<Customer>> _customerRepository;
    private Mock<IRepository<CreditWallet>> _walletRepository;
    private Mock<IRepository<CreditLedgerEntry>> _ledgerRepository;
    private Mock<IRepository<CreditPurchaseGrant>> _creditPurchaseGrantRepository;
    private AIInterviewSettings _aiInterviewSettings;
    private MockAIInterviewSettings _mockAIInterviewSettings;
    private AIInterviewAdminController _controller;
    private MockAiInterviewAdminController _legacyController;
    private List<Customer> _customers;
    private List<CreditWallet> _wallets;
    private List<CreditLedgerEntry> _ledgerEntries;
    private List<CreditPurchaseGrant> _creditPurchaseGrants;

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
        _customerRepository = new Mock<IRepository<Customer>>();
        _walletRepository = new Mock<IRepository<CreditWallet>>();
        _ledgerRepository = new Mock<IRepository<CreditLedgerEntry>>();
        _creditPurchaseGrantRepository = new Mock<IRepository<CreditPurchaseGrant>>();
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

        _customers = new List<Customer>();
        _wallets = new List<CreditWallet>();
        _ledgerEntries = new List<CreditLedgerEntry>();
        _creditPurchaseGrants = new List<CreditPurchaseGrant>();
        _customerRepository.SetupGet(x => x.Table).Returns(() => _customers.AsQueryable());
        _walletRepository.SetupGet(x => x.Table).Returns(() => _wallets.AsQueryable());
        _ledgerRepository.SetupGet(x => x.Table).Returns(() => _ledgerEntries.AsQueryable());
        _creditPurchaseGrantRepository.SetupGet(x => x.Table).Returns(() => _creditPurchaseGrants.AsQueryable());
        _walletRepository.Setup(x => x.GetAllAsync(
                It.IsAny<Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>>>(),
                It.IsAny<Func<ICacheKeyService, CacheKey>>(),
                true))
            .ReturnsAsync((Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>> func, Func<ICacheKeyService, CacheKey> _, bool __) =>
                func == null ? _wallets.ToList() : func(_wallets.AsQueryable()).ToList());
        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.IsAny<int[]>()))
            .ReturnsAsync(new List<Customer>());

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
            _customerRepository.Object,
            _walletRepository.Object,
            _ledgerRepository.Object,
            _creditPurchaseGrantRepository.Object,
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
        Assert.That(getModel.ApiKey, Is.EqualTo("keep-api"));
        Assert.That(getModel.AzureOpenAiApiKey, Is.EqualTo("keep-aoai-key"));
        Assert.That(getModel.AzureSpeechKey, Is.EqualTo("keep-speech"));
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
            AzureOpenAiApiKey = "",
            AzureOpenAiDeploymentOrModel = "deployment",
            AzureSpeechKey = "",
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
        Assert.That(_aiInterviewSettings.ApiKey, Is.EqualTo("key"));
        Assert.That(_aiInterviewSettings.AzureOpenAiApiKey, Is.EqualTo("keep-aoai-key"));
        Assert.That(_aiInterviewSettings.AzureSpeechKey, Is.EqualTo("keep-speech"));
        Assert.That(_aiInterviewSettings.AzureBlobStorageSasToken, Is.EqualTo("keep-sas"));
        Assert.That(_aiInterviewSettings.CreditProductSkuMappingsJson, Does.Contain("AI-CREDIT-10"));
        Assert.That(_aiInterviewSettings.CreditPurchasePageUrl, Is.EqualTo("/credits"));
        Assert.That(refreshedModel.AzureOpenAiEndpointUrl, Is.EqualTo("https://endpoint"));
        Assert.That(refreshedModel.ApiKey, Is.EqualTo("key"));
        Assert.That(refreshedModel.AzureOpenAiApiKey, Is.EqualTo("keep-aoai-key"));
        Assert.That(refreshedModel.AzureOpenAiDeploymentOrModel, Is.EqualTo("deployment"));
        Assert.That(refreshedModel.AzureSpeechKey, Is.EqualTo("keep-speech"));
        Assert.That(refreshedModel.AzureSpeechRegion, Is.EqualTo("eastus"));
        Assert.That(refreshedModel.AzureBlobStorageContainerUrl, Is.Empty);
        Assert.That(refreshedModel.AzureBlobStorageSasToken, Is.EqualTo("keep-sas"));
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
    public void AiService_View_Uses_Plain_Text_Inputs_For_Development_Secrets()
    {
        var text = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Admin", "AiService.cshtml"));

        Assert.That(text, Does.Contain("asp-for=\"ApiKey\""));
        Assert.That(text, Does.Contain("asp-for=\"AzureOpenAiApiKey\""));
        Assert.That(text, Does.Contain("asp-for=\"AzureSpeechKey\""));
        Assert.That(text, Does.Contain("asp-for=\"AzureBlobStorageSasToken\""));
        Assert.That(text, Does.Contain("type=\"text\""));
        Assert.That(text, Does.Not.Contain("type=\"password\""));
        Assert.That(text, Does.Not.Contain("placeholder=\"********\""));
    }

    [Test]
    public void Scoreboard_View_Uses_Nopcommerce_DataTables_Helper()
    {
        var text = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Admin", "Scoreboard.cshtml"));

        Assert.That(text, Does.Contain("@await Html.PartialAsync(\"Table\", new DataTablesModel"));
        Assert.That(text, Does.Contain("UrlRead = new DataUrl(\"ScoreboardList\", \"AIInterviewAdmin\", null)"));
        Assert.That(text, Does.Contain("ColumnCollection = new List<ColumnProperty>"));
    }

    [Test]
    public void Admin_Mock_Report_Placeholder_Is_Removed()
    {
        Assert.That(typeof(AIInterviewDefaults).GetProperty("AdminMockReportRouteName"), Is.Null);
        Assert.That(typeof(MockAiInterviewAdminController).GetMethod("Report"), Is.Null);
        Assert.That(File.Exists(TestFilePathHelper.GetPluginFilePath("Views", "MockAiInterviewAdmin", "Report.cshtml")), Is.False);
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
        jobRequirementService.Setup(x => x.SaveRequirementsAsync(It.IsAny<Product>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<decimal>(), It.IsAny<int>()))
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
            _customerRepository.Object,
            _walletRepository.Object,
            _ledgerRepository.Object,
            _creditPurchaseGrantRepository.Object,
            _aiInterviewSettings,
            _mockAIInterviewSettings,
            jobRequirementService.Object);

        var result = await controller.SaveProductRequirements(new JobRequirementsModel
        {
            ProductId = 55,
            ResumeRequired = true,
            InterviewRequired = false,
            MinimumScore = 88,
            QuestionCount = 4
        });

        Assert.That(result, Is.TypeOf<JsonResult>());
        jobRequirementService.Verify(x => x.SaveRequirementsAsync(It.Is<Product>(product => product.Id == 55), true, false, 88m, 4), Times.Once);
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
        jobRequirementService.Setup(x => x.SaveRequirementsAsync(It.IsAny<Product>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<decimal>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Features.Set<IFormFeature>(new FormFeature(new FormCollection(new Dictionary<string, StringValues>
            {
                ["AIInterviewJobResumeRequired"] = new StringValues(new[] { "false", "true" }),
                ["AIInterviewJobInterviewRequired"] = new StringValues("false"),
                ["AIInterviewJobMinimumScore"] = new StringValues("77.5"),
                ["AIInterviewJobQuestionCount"] = new StringValues("6")
            })));

        var consumer = new ProductRequirementsEventConsumer(new HttpContextAccessor { HttpContext = context }, jobRequirementService.Object);
        var product = new Product { Id = 77, ProductTemplateId = 7 };

        await consumer.HandleEventAsync(new Nop.Core.Events.EntityInsertedEvent<Product>(product));
        await consumer.HandleEventAsync(new Nop.Core.Events.EntityUpdatedEvent<Product>(product));

        jobRequirementService.Verify(x => x.SaveRequirementsAsync(product, true, false, 77.5m, 6), Times.Exactly(2));
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

        Assert.That(model.AvailableCustomers, Has.Count.EqualTo(2));
        Assert.That(model.AvailableCustomers.First().Value, Is.EqualTo("0"));
        Assert.That(model.AvailableCustomers.First().Text, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Credits.SelectVendor"));
        Assert.That(model.AvailableCustomers.Last().Value, Is.EqualTo("101"));
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
    public async Task ApplicantCredits_Post_With_Deleted_Applicant_Does_Not_Call_AddCredit()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(202))
            .ReturnsAsync(new Customer { Id = 202, VendorId = 0, Deleted = true });

        await _controller.ApplicantCredits(new CreditManagementModel
        {
            CustomerId = 202,
            Amount = 15
        });

        _notificationService.Verify(x => x.ErrorNotification("Customer is required."), Times.Once);
        _creditService.Verify(x => x.AddCreditAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task VendorCredits_Post_With_Deleted_Vendor_Does_Not_Call_AddCredit()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(101))
            .ReturnsAsync(new Customer { Id = 101, VendorId = 5, Deleted = true });

        await _controller.VendorCredits(new CreditManagementModel
        {
            CustomerId = 101,
            Amount = 15
        });

        _notificationService.Verify(x => x.ErrorNotification("Customer is required."), Times.Once);
        _creditService.Verify(x => x.AddCreditAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ApplicantCredits_Get_Populates_Customer_Dropdown()
    {
        var result = await _controller.ApplicantCredits();
        var model = (CreditManagementModel)((ViewResult)result).Model;

        Assert.That(model.AvailableCustomers, Has.Count.EqualTo(1));
        Assert.That(model.AvailableCustomers.First().Value, Is.EqualTo("0"));
        Assert.That(model.AvailableCustomers.First().Text, Is.EqualTo("Plugins.Misc.AIInterview.Admin.Credits.SelectApplicant"));
    }

    [Test]
    public async Task ApplicantCredits_Initial_Get_Shows_Credit_Active_Applicants()
    {
        _customers.Add(new Customer { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 });
        _wallets.Add(new CreditWallet { Id = 9, CustomerId = 202, Balance = 4 });
        _ledgerEntries.Add(new CreditLedgerEntry
        {
            Id = 1,
            CreditWalletId = 9,
            Amount = 4,
            TransactionType = "Deposit",
            Remarks = "Purchased credit pack: order #1002, SKU AI-CREDIT-1, credits 4",
            CreatedOnUtc = new DateTime(2026, 6, 14, 8, 0, 0, DateTimeKind.Utc)
        });

        var result = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { Start = 0, Length = 10, Draw = "1" });
        var model = (ApplicantCreditActivityListModel)((JsonResult)result).Value;

        Assert.That(model.Data, Has.Count.EqualTo(1));
        Assert.That(model.Data.Single().CustomerId, Is.EqualTo(202));
        Assert.That(model.Data.Single().WalletBalance, Is.EqualTo(4));
    }

    [Test]
    public async Task ApplicantCreditActivityList_Excludes_ZeroBalance_Wallet_With_No_History()
    {
        _customers.Add(new Customer { Id = 303, FirstName = "No", LastName = "History", Email = "none@example.com", VendorId = 0 });
        _wallets.Add(new CreditWallet { Id = 15, CustomerId = 303, Balance = 0 });

        var result = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { Start = 0, Length = 10, Draw = "1" });
        var model = (ApplicantCreditActivityListModel)((JsonResult)result).Value;

        Assert.That(model.Data, Is.Empty);
    }

    [Test]
    public async Task ApplicantCredits_Get_WithCustomerId_Loads_Selected_Wallet_And_Ledger()
    {
        _wallets.Add(new CreditWallet { Id = 12, CustomerId = 202, Balance = 3 });
        _ledgerEntries.Add(new CreditLedgerEntry
        {
            Id = 1,
            CreditWalletId = 12,
            Amount = 5,
            TransactionType = "Deposit",
            Remarks = "Purchased credit pack: order #1010, SKU AI-CREDIT-1, credits 5",
            CreatedOnUtc = new DateTime(2026, 6, 14, 8, 0, 0, DateTimeKind.Utc)
        });
        _ledgerEntries.Add(new CreditLedgerEntry
        {
            Id = 2,
            CreditWalletId = 12,
            Amount = -2,
            TransactionType = "Withdrawal",
            Remarks = "Interview charge",
            CreatedOnUtc = new DateTime(2026, 6, 14, 9, 0, 0, DateTimeKind.Utc)
        });
        _customerService.Setup(x => x.GetCustomerByIdAsync(202))
            .ReturnsAsync(new Customer { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 });
        _customerService.Setup(x => x.GetCustomersByIdsAsync(It.Is<int[]>(ids => ids.Contains(202))))
            .ReturnsAsync(new List<Customer>
            {
                new() { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 }
            });

        var result = await _controller.ApplicantCredits(202);
        var model = (CreditManagementModel)((ViewResult)result).Model;

        Assert.That(model.CustomerId, Is.EqualTo(202));
        Assert.That(model.CustomerName, Is.EqualTo("Jane Doe"));
        Assert.That(model.AvailableCustomers.Last().Text, Does.Contain("Jane Doe"));
        Assert.That(model.AvailableCustomers.Last().Text, Does.Contain("jane@example.com"));
        Assert.That(model.WalletBalance, Is.EqualTo(3));
        Assert.That(model.LedgerEntries, Has.Count.EqualTo(2));
        Assert.That(model.LedgerEntries.First().TransactionType, Is.EqualTo("Withdrawal"));
    }

    [Test]
    public async Task ApplicantCredits_Get_With_New_Applicant_Loads_Selected_Applicant_Without_Creating_Wallet()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(202))
            .ReturnsAsync(new Customer { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 });

        var result = await _controller.ApplicantCredits(202);
        var model = (CreditManagementModel)((ViewResult)result).Model;

        Assert.That(model.CustomerId, Is.EqualTo(202));
        Assert.That(model.CustomerName, Is.EqualTo("Jane Doe"));
        Assert.That(model.WalletBalance, Is.Zero);
        Assert.That(model.LedgerEntries, Is.Empty);
        _creditService.Verify(x => x.GetOrCreateWalletAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task ApplicantCredits_Get_With_VendorCustomer_Does_Not_Load_Selected_Data()
    {
        _wallets.Add(new CreditWallet { Id = 77, CustomerId = 301, Balance = 8 });
        _customerService.Setup(x => x.GetCustomerByIdAsync(301))
            .ReturnsAsync(new Customer { Id = 301, FirstName = "Vendor", LastName = "Owner", Email = "vendor@example.com", VendorId = 12 });

        var result = await _controller.ApplicantCredits(301);
        var model = (CreditManagementModel)((ViewResult)result).Model;

        Assert.That(model.CustomerId, Is.Zero);
        Assert.That(model.CustomerName, Is.Null.Or.Empty);
        Assert.That(model.WalletBalance, Is.Zero);
        Assert.That(model.LedgerEntries, Is.Empty);
    }

    [Test]
    public async Task ApplicantCredits_Get_With_DeletedCustomer_Does_Not_Load_Selected_Data()
    {
        _wallets.Add(new CreditWallet { Id = 78, CustomerId = 302, Balance = 8 });
        _customerService.Setup(x => x.GetCustomerByIdAsync(302))
            .ReturnsAsync(new Customer { Id = 302, FirstName = "Deleted", LastName = "Applicant", Email = "deleted@example.com", VendorId = 0, Deleted = true });

        var result = await _controller.ApplicantCredits(302);
        var model = (CreditManagementModel)((ViewResult)result).Model;

        Assert.That(model.CustomerId, Is.Zero);
        Assert.That(model.CustomerName, Is.Null.Or.Empty);
        Assert.That(model.WalletBalance, Is.Zero);
        Assert.That(model.LedgerEntries, Is.Empty);
    }

    [Test]
    public async Task ApplicantCredits_Activity_Does_Not_Show_Vendor_Customers()
    {
        _customers.Add(new Customer { Id = 301, FirstName = "Vendor", LastName = "Owner", Email = "vendor@example.com", VendorId = 55 });
        _wallets.Add(new CreditWallet { Id = 50, CustomerId = 301, Balance = 7 });
        _ledgerEntries.Add(new CreditLedgerEntry
        {
            Id = 1,
            CreditWalletId = 50,
            Amount = 7,
            TransactionType = "Deposit",
            Remarks = "Vendor credit row should stay out of applicant page",
            CreatedOnUtc = DateTime.UtcNow
        });

        var result = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { Start = 0, Length = 10, Draw = "1" });
        var model = (ApplicantCreditActivityListModel)((JsonResult)result).Value;

        Assert.That(model.Data, Is.Empty);
    }

    [Test]
    public async Task ApplicantCreditActivityList_Excludes_Deleted_Customers()
    {
        _customers.Add(new Customer { Id = 304, FirstName = "Deleted", LastName = "Customer", Email = "deleted@example.com", VendorId = 0, Deleted = true });
        _wallets.Add(new CreditWallet { Id = 51, CustomerId = 304, Balance = 3 });
        _ledgerEntries.Add(new CreditLedgerEntry
        {
            Id = 1,
            CreditWalletId = 51,
            Amount = 3,
            TransactionType = "Deposit",
            Remarks = "Should stay hidden",
            CreatedOnUtc = DateTime.UtcNow
        });

        var result = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { Start = 0, Length = 10, Draw = "1" });
        var model = (ApplicantCreditActivityListModel)((JsonResult)result).Value;

        Assert.That(model.Data, Is.Empty);
    }

    [Test]
    public async Task ApplicantCredits_Zero_Balance_Customer_Remains_Discoverable()
    {
        _customers.Add(new Customer { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 });
        _wallets.Add(new CreditWallet { Id = 61, CustomerId = 202, Balance = 0 });
        _ledgerEntries.Add(new CreditLedgerEntry
        {
            Id = 1,
            CreditWalletId = 61,
            Amount = 5,
            TransactionType = "Deposit",
            Remarks = "Initial grant",
            CreatedOnUtc = new DateTime(2026, 6, 13, 8, 0, 0, DateTimeKind.Utc)
        });
        _ledgerEntries.Add(new CreditLedgerEntry
        {
            Id = 2,
            CreditWalletId = 61,
            Amount = -5,
            TransactionType = "Withdrawal",
            Remarks = "Spent all credits",
            CreatedOnUtc = new DateTime(2026, 6, 13, 9, 0, 0, DateTimeKind.Utc)
        });

        var result = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { Start = 0, Length = 10, Draw = "1" });
        var model = (ApplicantCreditActivityListModel)((JsonResult)result).Value;

        Assert.That(model.Data, Has.Count.EqualTo(1));
        Assert.That(model.Data.Single().WalletBalance, Is.Zero);
        Assert.That(model.Data.Single().TotalWithdrawn, Is.EqualTo(5));
    }

    [Test]
    public async Task ApplicantCreditActivityList_Supports_Paging_And_Filtering()
    {
        _customers.AddRange(new[]
        {
            new Customer { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 },
            new Customer { Id = 203, FirstName = "John", LastName = "Smith", Email = "john@example.com", VendorId = 0 }
        });
        _wallets.AddRange(new[]
        {
            new CreditWallet { Id = 1, CustomerId = 202, Balance = 5 },
            new CreditWallet { Id = 2, CustomerId = 203, Balance = 1 }
        });
        _ledgerEntries.AddRange(new[]
        {
            new CreditLedgerEntry { Id = 1, CreditWalletId = 1, Amount = 5, TransactionType = "Deposit", Remarks = "A", CreatedOnUtc = new DateTime(2026, 6, 14, 10, 0, 0, DateTimeKind.Utc) },
            new CreditLedgerEntry { Id = 2, CreditWalletId = 2, Amount = 1, TransactionType = "Deposit", Remarks = "B", CreatedOnUtc = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc) }
        });

        var result = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel
        {
            SearchKeyword = "jane",
            Start = 0,
            Length = 1,
            Draw = "2"
        });
        var model = (ApplicantCreditActivityListModel)((JsonResult)result).Value;

        Assert.That(model.RecordsTotal, Is.EqualTo(1));
        Assert.That(model.Data, Has.Count.EqualTo(1));
        Assert.That(model.Data.Single().CustomerId, Is.EqualTo(202));
    }

    [Test]
    public async Task ApplicantCreditActivityList_Duplicate_Wallets_Aggregate_Balance_Deterministically()
    {
        _customers.Add(new Customer { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 });
        _wallets.AddRange(new[]
        {
            new CreditWallet { Id = 1, CustomerId = 202, Balance = 2 },
            new CreditWallet { Id = 2, CustomerId = 202, Balance = 3 }
        });

        var result = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { Start = 0, Length = 10, Draw = "1" });
        var model = (ApplicantCreditActivityListModel)((JsonResult)result).Value;

        Assert.That(model.Data.Single().WalletBalance, Is.EqualTo(5));
    }

    [Test]
    public async Task CreditService_AddCreditAsync_Uses_Primary_Wallet_When_Duplicate_Wallets_Exist()
    {
        var wallets = new List<CreditWallet>
        {
            new() { Id = 1, CustomerId = 202, Balance = 2 },
            new() { Id = 2, CustomerId = 202, Balance = 3 }
        };
        var ledgerEntries = new List<CreditLedgerEntry>();
        var walletRepository = new Mock<IRepository<CreditWallet>>();
        var ledgerRepository = new Mock<IRepository<CreditLedgerEntry>>();

        walletRepository.Setup(x => x.GetAllAsync(It.IsAny<Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>>>(), It.IsAny<Func<ICacheKeyService, CacheKey>>(), true))
            .ReturnsAsync((Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>> func, Func<ICacheKeyService, CacheKey> _, bool __) =>
                func == null ? wallets.ToList() : func(wallets.AsQueryable()).ToList());
        walletRepository.Setup(x => x.UpdateAsync(It.IsAny<CreditWallet>(), true))
            .Callback<CreditWallet, bool>((wallet, _) =>
            {
                var existing = wallets.Single(item => item.Id == wallet.Id);
                existing.Balance = wallet.Balance;
            })
            .Returns(Task.CompletedTask);
        walletRepository.Setup(x => x.InsertAsync(It.IsAny<CreditWallet>(), true)).Returns(Task.CompletedTask);
        ledgerRepository.Setup(x => x.InsertAsync(It.IsAny<CreditLedgerEntry>(), true))
            .Callback<CreditLedgerEntry, bool>((entry, _) => ledgerEntries.Add(entry))
            .Returns(Task.CompletedTask);

        var service = new CreditService(walletRepository.Object, ledgerRepository.Object);

        await service.AddCreditAsync(202, 4, "topup");

        Assert.That(wallets.Single(item => item.Id == 1).Balance, Is.EqualTo(6));
        Assert.That(wallets.Single(item => item.Id == 2).Balance, Is.EqualTo(3));
        Assert.That(ledgerEntries.Single().CreditWalletId, Is.EqualTo(1));
    }

    [Test]
    public async Task ApplicantCreditActivityList_Keyword_Search_Is_Null_Safe_And_Matches_Email_First_And_Last_Name()
    {
        _customers.AddRange(new[]
        {
            new Customer { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 },
            new Customer { Id = 203, FirstName = null, LastName = "Nullname", Email = null, VendorId = 0 },
            new Customer { Id = 204, FirstName = "Alice", LastName = "Johnson", Email = "alice@example.com", VendorId = 0 }
        });
        _wallets.AddRange(new[]
        {
            new CreditWallet { Id = 1, CustomerId = 202, Balance = 5 },
            new CreditWallet { Id = 2, CustomerId = 203, Balance = 2 },
            new CreditWallet { Id = 3, CustomerId = 204, Balance = 1 }
        });

        var emailResult = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { SearchKeyword = "alice@", Start = 0, Length = 10, Draw = "1" });
        var emailModel = (ApplicantCreditActivityListModel)((JsonResult)emailResult).Value;
        Assert.That(emailModel.Data.Single().CustomerId, Is.EqualTo(204));

        var firstNameResult = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { SearchKeyword = "Jane", Start = 0, Length = 10, Draw = "2" });
        var firstNameModel = (ApplicantCreditActivityListModel)((JsonResult)firstNameResult).Value;
        Assert.That(firstNameModel.Data.Single().CustomerId, Is.EqualTo(202));

        var lastNameResult = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { SearchKeyword = "Nullname", Start = 0, Length = 10, Draw = "3" });
        var lastNameModel = (ApplicantCreditActivityListModel)((JsonResult)lastNameResult).Value;
        Assert.That(lastNameModel.Data.Single().CustomerId, Is.EqualTo(203));
    }

    [Test]
    public async Task ApplicantCreditActivityList_Keyword_Search_Matches_Full_Name()
    {
        _customers.AddRange(new[]
        {
            new Customer { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 },
            new Customer { Id = 203, FirstName = "John", LastName = "Smith", Email = "john@example.com", VendorId = 0 }
        });
        _wallets.AddRange(new[]
        {
            new CreditWallet { Id = 1, CustomerId = 202, Balance = 1 },
            new CreditWallet { Id = 2, CustomerId = 203, Balance = 6 }
        });

        var result = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { SearchKeyword = "Jane Doe", Start = 0, Length = 10, Draw = "1" });
        var model = (ApplicantCreditActivityListModel)((JsonResult)result).Value;

        Assert.That(model.RecordsTotal, Is.EqualTo(1));
        Assert.That(model.Data.Single().CustomerId, Is.EqualTo(202));
    }

    [Test]
    public async Task ApplicantCreditActivityList_Orders_By_Most_Recent_Activity()
    {
        _customers.AddRange(new[]
        {
            new Customer { Id = 202, FirstName = "Jane", LastName = "Doe", Email = "jane@example.com", VendorId = 0 },
            new Customer { Id = 203, FirstName = "John", LastName = "Smith", Email = "john@example.com", VendorId = 0 }
        });
        _wallets.AddRange(new[]
        {
            new CreditWallet { Id = 1, CustomerId = 202, Balance = 5 },
            new CreditWallet { Id = 2, CustomerId = 203, Balance = 4 }
        });
        _ledgerEntries.AddRange(new[]
        {
            new CreditLedgerEntry
            {
                Id = 1,
                CreditWalletId = 1,
                Amount = 5,
                TransactionType = "Deposit",
                Remarks = "older activity",
                CreatedOnUtc = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc)
            },
            new CreditLedgerEntry
            {
                Id = 2,
                CreditWalletId = 2,
                Amount = 4,
                TransactionType = "Deposit",
                Remarks = "newer activity",
                CreatedOnUtc = new DateTime(2026, 6, 14, 10, 0, 0, DateTimeKind.Utc)
            }
        });

        var result = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel { Start = 0, Length = 10, Draw = "1" });
        var model = (ApplicantCreditActivityListModel)((JsonResult)result).Value;

        Assert.That(model.Data, Has.Count.EqualTo(2));
        Assert.That(model.Data.First().CustomerId, Is.EqualTo(203));
    }

    [Test]
    public async Task ApplicantCreditActivityList_Paid_Mapped_Order_Flow_Creates_Grant_Wallet_Ledger_And_Shows_In_Grid()
    {
        var customer = new Customer { Id = 404, FirstName = "Alice", LastName = "Applicant", Email = "alice@example.com", VendorId = 0 };
        _customers.Add(customer);

        var walletRepository = new Mock<IRepository<CreditWallet>>();
        var ledgerRepository = new Mock<IRepository<CreditLedgerEntry>>();
        var grantRepository = new Mock<IRepository<CreditPurchaseGrant>>();
        var wallets = new List<CreditWallet>();
        var ledgers = new List<CreditLedgerEntry>();
        var grants = new List<CreditPurchaseGrant>();

        walletRepository.SetupGet(x => x.Table).Returns(() => wallets.AsQueryable());
        walletRepository.Setup(x => x.GetAllAsync(It.IsAny<Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>>>(), It.IsAny<Func<ICacheKeyService, CacheKey>>(), true))
            .ReturnsAsync((Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>> func, Func<ICacheKeyService, CacheKey> _, bool __) =>
                func == null ? wallets.ToList() : func(wallets.AsQueryable()).ToList());
        walletRepository.Setup(x => x.InsertAsync(It.IsAny<CreditWallet>(), true))
            .Callback<CreditWallet, bool>((wallet, _) =>
            {
                wallet.Id = wallets.Count + 1;
                wallets.Add(wallet);
            })
            .Returns(Task.CompletedTask);
        walletRepository.Setup(x => x.UpdateAsync(It.IsAny<CreditWallet>(), true))
            .Returns(Task.CompletedTask);

        ledgerRepository.SetupGet(x => x.Table).Returns(() => ledgers.AsQueryable());
        ledgerRepository.Setup(x => x.InsertAsync(It.IsAny<CreditLedgerEntry>(), true))
            .Callback<CreditLedgerEntry, bool>((entry, _) =>
            {
                entry.Id = ledgers.Count + 1;
                ledgers.Add(entry);
            })
            .Returns(Task.CompletedTask);

        grantRepository.SetupGet(x => x.Table).Returns(() => grants.AsQueryable());
        grantRepository.Setup(x => x.GetAllAsync(It.IsAny<Func<IQueryable<CreditPurchaseGrant>, IQueryable<CreditPurchaseGrant>>>(), It.IsAny<Func<ICacheKeyService, CacheKey>>(), true))
            .ReturnsAsync((Func<IQueryable<CreditPurchaseGrant>, IQueryable<CreditPurchaseGrant>> func, Func<ICacheKeyService, CacheKey> _, bool __) =>
                func == null ? grants.ToList() : func(grants.AsQueryable()).ToList());
        grantRepository.Setup(x => x.InsertAsync(It.IsAny<CreditPurchaseGrant>(), true))
            .Callback<CreditPurchaseGrant, bool>((grant, _) =>
            {
                grant.Id = grants.Count + 1;
                grants.Add(grant);
            })
            .Returns(Task.CompletedTask);

        var realCreditService = new CreditService(walletRepository.Object, ledgerRepository.Object);
        var orderService = new Mock<Nop.Services.Orders.IOrderService>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<ICustomerService>();
        customerService.Setup(x => x.GetCustomerByIdAsync(404)).ReturnsAsync(customer);
        customerService.Setup(x => x.IsRegisteredAsync(It.IsAny<Customer>(), true)).ReturnsAsync(true);
        orderService.Setup(x => x.GetOrderItemsAsync(9001, null, null, 0)).ReturnsAsync(new List<Nop.Core.Domain.Orders.OrderItem>
        {
            new() { Id = 88, OrderId = 9001, ProductId = 300, Quantity = 1 }
        });
        orderService.Setup(x => x.GetProductByOrderItemIdAsync(88)).ReturnsAsync(new Product { Id = 300, Sku = "AI-CREDIT-10" });

        var purchaseService = new CreditPurchaseService(
            grantRepository.Object,
            orderService.Object,
            productService.Object,
            customerService.Object,
            realCreditService,
            new AIInterviewSettings { CreditProductSkuMappingsJson = "{\"AI-CREDIT-10\":10}" },
            Mock.Of<ILogger<CreditPurchaseService>>());

        await purchaseService.GrantCreditsForPaidOrderAsync(new Nop.Core.Domain.Orders.Order { Id = 9001, CustomerId = 404 });

        Assert.That(grants, Has.Count.EqualTo(1));
        Assert.That(wallets, Has.Count.EqualTo(1));
        Assert.That(ledgers, Has.Count.EqualTo(1));

        _wallets.AddRange(wallets);
        _ledgerEntries.AddRange(ledgers);
        _creditPurchaseGrants.AddRange(grants);

        var result = await _controller.ApplicantCreditActivityList(new ApplicantCreditActivitySearchModel
        {
            SearchKeyword = "alice@example.com",
            Start = 0,
            Length = 10,
            Draw = "3"
        });
        var model = (ApplicantCreditActivityListModel)((JsonResult)result).Value;

        Assert.That(model.Data, Has.Count.EqualTo(1));
        Assert.That(model.Data.Single().CustomerId, Is.EqualTo(404));
        Assert.That(model.Data.Single().WalletBalance, Is.EqualTo(10));
    }

    [Test]
    public void ApplicantCredits_View_Uses_Local_Encoder_And_Localized_Ledger_Headers()
    {
        var text = File.ReadAllText(TestFilePathHelper.GetPluginFilePath("Views", "Admin", "ApplicantCredits.cshtml"));

        Assert.That(text, Does.Contain("function applicantCreditEncodeHtml"));
        Assert.That(text, Does.Contain("applicantCreditEncodeUrl"));
        Assert.That(text, Does.Not.Contain("htmlEncode("));
        Assert.That(text, Does.Contain("Plugins.Misc.AIInterview.Admin.Credits.Ledger.Customer"));
        Assert.That(text, Does.Contain("Plugins.Misc.AIInterview.Admin.Credits.Ledger.Amount"));
        Assert.That(text, Does.Contain("Plugins.Misc.AIInterview.Admin.Credits.Ledger.Type"));
        Assert.That(text, Does.Contain("Plugins.Misc.AIInterview.Admin.Credits.Ledger.Remarks"));
        Assert.That(text, Does.Contain("Plugins.Misc.AIInterview.Admin.Credits.Ledger.Utc"));
        Assert.That(text, Does.Not.Contain("asp-for=\"LoadCustomerId\""));
        Assert.That(text, Does.Not.Contain("asp-for=\"LoadCustomerEmail\""));
        Assert.That(text, Does.Not.Contain("asp-for=\"CustomerId\""));
        Assert.That(text, Does.Not.Contain("Load Applicant"));
        Assert.That(text, Does.Not.Contain("SearchCustomerId"));
        Assert.That(text, Does.Not.Contain("SearchHasPositiveBalanceOnly"));
        Assert.That(text, Does.Contain("Plugins.Misc.AIInterview.Admin.Credits.Activity.SearchKeyword"));
        Assert.That(text, Does.Contain("Plugins.Misc.AIInterview.Admin.Credits.Credits"));
        Assert.That(text, Does.Contain("card-search"));
        Assert.That(text, Does.Contain("applicant-credits-grid"));
        Assert.That(text, Does.Contain("id=\"SelectedCustomerId\""));
        Assert.That(text, Does.Contain("name=\"CustomerId\""));
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
