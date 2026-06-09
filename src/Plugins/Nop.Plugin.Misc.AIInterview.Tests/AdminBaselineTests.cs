using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Vendors;
using Nop.Data;
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
        _workContext = new Mock<IWorkContext>();
        _settingService = new Mock<ISettingService>();
        _walletRepository = new Mock<IRepository<CreditWallet>>();
        _ledgerRepository = new Mock<IRepository<CreditLedgerEntry>>();
        _aiInterviewSettings = new AIInterviewSettings
        {
            Enabled = true,
            ResumeRequired = true,
            InterviewRequired = true,
            MinimumScore = 10,
            Provider = "keep-provider",
            Model = "keep-model",
            Prompt = "keep-prompt",
            ServiceSettings = "keep-service",
            ApiKey = "keep-api",
            AzureOpenAiEndpointUrl = "keep-endpoint",
            AzureOpenAiApiKey = "keep-aoai-key",
            AzureOpenAiDeploymentOrModel = "keep-deploy",
            AgoraAppId = "keep-agora",
            AgoraTokenServiceUrl = "keep-agora-url",
            AzureSpeechKey = "keep-speech",
            AzureSpeechRegion = "keep-region"
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
            _workContext.Object,
            _settingService.Object,
            _walletRepository.Object,
            _ledgerRepository.Object,
            _aiInterviewSettings,
            _mockAIInterviewSettings);

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
        Assert.That(parent.ChildNodes.Count, Is.EqualTo(7));
        Assert.That(parent.ChildNodes.Select(x => x.SystemName), Is.EquivalentTo(new[]
        {
            AIInterviewDefaults.AdminConfigureMenuSystemName,
            AIInterviewDefaults.AdminGeneralMenuSystemName,
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
        Assert.That(_aiInterviewSettings.ResumeRequired, Is.True);
        Assert.That(_aiInterviewSettings.InterviewRequired, Is.True);
        Assert.That(_aiInterviewSettings.MinimumScore, Is.EqualTo(10));
        Assert.That(_aiInterviewSettings.Provider, Is.EqualTo("keep-provider"));
        _settingService.Verify(x => x.SaveSettingAsync(It.IsAny<AIInterviewSettings>()), Times.Once);
    }

    [Test]
    public async Task General_Page_Saves_And_Reloads()
    {
        var getResult = _controller.General();
        var getModel = (GeneralSettingsModel)((ViewResult)getResult).Model;
        Assert.That(getModel.ResumeRequired, Is.True);
        Assert.That(getModel.MinimumScore, Is.EqualTo(10));

        await _controller.General(new GeneralSettingsModel
        {
            ResumeRequired = false,
            InterviewRequired = false,
            MinimumScore = 77
        });

        Assert.That(_aiInterviewSettings.ResumeRequired, Is.False);
        Assert.That(_aiInterviewSettings.InterviewRequired, Is.False);
        Assert.That(_aiInterviewSettings.MinimumScore, Is.EqualTo(77));
    }

    [Test]
    public async Task AiService_Page_Saves_And_Reloads()
    {
        var getResult = _controller.AiService();
        var getModel = (AiServiceSettingsModel)((ViewResult)getResult).Model;
        Assert.That(getModel.UseMockResponses, Is.True);
        Assert.That(getModel.AzureOpenAiEndpointUrl, Is.EqualTo("keep-endpoint"));

        await _controller.AiService(new AiServiceSettingsModel
        {
            UseMockResponses = false,
            Provider = "OpenAI",
            ApiKey = "key",
            Model = "gpt-4",
            Prompt = "prompt",
            ServiceSettings = "svc",
            AzureOpenAiEndpointUrl = "https://endpoint",
            AzureOpenAiApiKey = "aoai-key",
            AzureOpenAiDeploymentOrModel = "deployment",
            AgoraAppId = "agora",
            AgoraTokenServiceUrl = "https://token",
            AzureSpeechKey = "speech",
            AzureSpeechRegion = "eastus"
        });

        Assert.That(_mockAIInterviewSettings.UseMockResponses, Is.False);
        Assert.That(_aiInterviewSettings.Provider, Is.EqualTo("OpenAI"));
        Assert.That(_aiInterviewSettings.AzureSpeechRegion, Is.EqualTo("eastus"));
        _settingService.Verify(x => x.SaveSettingAsync(It.IsAny<AIInterviewSettings>()), Times.AtLeastOnce);
        _settingService.Verify(x => x.SaveSettingAsync(It.IsAny<MockAIInterviewSettings>()), Times.Once);
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
            new() { Id = 1, Email = "active@example.com", IsActive = true, IsAccepted = false, ExpiryDateUtc = DateTime.UtcNow.AddDays(1), CreatedOnUtc = DateTime.UtcNow.AddHours(-1) },
            new() { Id = 2, Email = "expired@example.com", IsActive = true, IsAccepted = false, ExpiryDateUtc = DateTime.UtcNow.AddDays(-1), CreatedOnUtc = DateTime.UtcNow.AddHours(-2) },
            new() { Id = 3, Email = "inactive@example.com", IsActive = false, IsAccepted = false, ExpiryDateUtc = DateTime.UtcNow.AddDays(1), CreatedOnUtc = DateTime.UtcNow.AddHours(-3) },
            new() { Id = 4, Email = "accepted@example.com", IsActive = true, IsAccepted = true, ExpiryDateUtc = DateTime.UtcNow.AddDays(1), CreatedOnUtc = DateTime.UtcNow.AddHours(-4) }
        };

        _inviteService.Setup(x => x.GetSponsorInvitesAsync(0)).ReturnsAsync(invites);

        var result = await _controller.SponsorInvites();

        var model = (SponsorInviteAdminModel)((ViewResult)result).Model;
        Assert.That(model.Invites.Single(x => x.Id == 1).Status, Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Active"));
        Assert.That(model.Invites.Single(x => x.Id == 2).Status, Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Expired"));
        Assert.That(model.Invites.Single(x => x.Id == 3).Status, Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Inactive"));
        Assert.That(model.Invites.Single(x => x.Id == 4).Status, Is.EqualTo("Plugins.Misc.AIInterview.Employer.Invite.Accepted"));
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
    public async Task VendorCredits_Rejects_ApplicantCustomer()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(201))
            .ReturnsAsync(new Customer { Id = 201, VendorId = 0 });

        await _controller.VendorCredits(new CreditManagementModel
        {
            CustomerId = 201,
            Amount = 25
        });

        _notificationService.Verify(x => x.ErrorNotification("Plugins.Misc.AIInterview.Admin.Credits.InvalidVendorScope"), Times.Once);
        _creditService.Verify(x => x.AddCreditAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
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

        _notificationService.Verify(x => x.ErrorNotification("Plugins.Misc.AIInterview.Admin.Credits.InvalidApplicantScope"), Times.Once);
        _creditService.Verify(x => x.AddCreditAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
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
    public async Task ApplicantCredits_InvalidAmount_Returns_Error()
    {
        _customerService.Setup(x => x.GetCustomerByIdAsync(202))
            .ReturnsAsync(new Customer { Id = 202, VendorId = 0 });

        await _controller.ApplicantCredits(new CreditManagementModel
        {
            CustomerId = 202,
            Amount = 0
        });

        _notificationService.Verify(x => x.ErrorNotification("Plugins.Misc.AIInterview.Admin.TopUp.InvalidAmount"), Times.Once);
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
}
