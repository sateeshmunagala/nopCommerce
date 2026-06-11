using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Caching;
using Nop.Data;
using Nop.Plugin.Misc.AIInterview;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class ServiceTests
{
    private Mock<IRepository<JobApplication>> _applicationRepository;
    private Mock<IRepository<Customer>> _customerRepository;
    private Mock<IRepository<InterviewSession>> _sessionRepository;
    private Mock<IRepository<Product>> _productRepository;
    private Mock<Nop.Services.Messages.IWorkflowMessageService> _workflowMessageService;
    private Mock<Nop.Services.Messages.IMessageTemplateService> _messageTemplateService;
    private Mock<Nop.Services.Messages.IEmailAccountService> _emailAccountService;
    private Mock<Nop.Services.Messages.IMessageTokenProvider> _messageTokenProvider;
    private Nop.Core.Domain.Messages.EmailAccountSettings _emailAccountSettings;
    private Mock<Nop.Core.IStoreContext> _storeContext;
    private Mock<global::Nop.Services.Helpers.IWebHelper> _webHelper;
    private ApplicationService _applicationService;

    [SetUp]
    public void SetUp()
    {
        _applicationRepository = new Mock<IRepository<JobApplication>>();
        _customerRepository = new Mock<IRepository<Customer>>();
        _sessionRepository = new Mock<IRepository<InterviewSession>>();
        _productRepository = new Mock<IRepository<Product>>();
        _workflowMessageService = new Mock<Nop.Services.Messages.IWorkflowMessageService>();
        _messageTemplateService = new Mock<Nop.Services.Messages.IMessageTemplateService>();
        _emailAccountService = new Mock<Nop.Services.Messages.IEmailAccountService>();
        _messageTokenProvider = new Mock<Nop.Services.Messages.IMessageTokenProvider>();
        _emailAccountSettings = new Nop.Core.Domain.Messages.EmailAccountSettings();
        _storeContext = new Mock<Nop.Core.IStoreContext>();
        _webHelper = new Mock<global::Nop.Services.Helpers.IWebHelper>();

        _applicationService = new ApplicationService(
            _applicationRepository.Object,
            _customerRepository.Object,
            _sessionRepository.Object,
            _productRepository.Object,
            _workflowMessageService.Object,
            _messageTemplateService.Object,
            _emailAccountService.Object,
            _messageTokenProvider.Object,
            _emailAccountSettings,
            _storeContext.Object,
            _webHelper.Object);
    }

    [Test]
    public async Task CanInsertJobApplication()
    {
        var application = new JobApplication { JobTitle = "Software Engineer" };
        await _applicationService.InsertJobApplicationAsync(application);
        _applicationRepository.Verify(r => r.InsertAsync(application, true), Times.Once);
    }

    [Test]
    public async Task SponsorInviteService_CreateInviteAsync_QueuesNotificationEmail()
    {
        var inviteRepository = new Mock<IRepository<SponsorInvite>>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<Nop.Services.Customers.ICustomerService>();
        var localizationService = new Mock<Nop.Services.Localization.ILocalizationService>();
        var workflowMessageService = new Mock<Nop.Services.Messages.IWorkflowMessageService>();
        var messageTemplateService = new Mock<Nop.Services.Messages.IMessageTemplateService>();
        var emailAccountService = new Mock<Nop.Services.Messages.IEmailAccountService>();
        var storeContext = new Mock<Nop.Core.IStoreContext>();
        var webHelper = new Mock<global::Nop.Services.Helpers.IWebHelper>();
        var emailAccountSettings = new Nop.Core.Domain.Messages.EmailAccountSettings { DefaultEmailAccountId = 7 };

        var product = new Product { Id = 11, Name = "Senior Backend Engineer", VendorId = 3 };
        var sponsor = new Customer { Id = 2, VendorId = 3, Email = "sponsor@example.com" };
        var emailAccount = new Nop.Core.Domain.Messages.EmailAccount { Id = 7, Email = "store@example.com", DisplayName = "Store" };
        var template = new Nop.Core.Domain.Messages.MessageTemplate { Name = "AIInterview.SponsorInviteCreated", EmailAccountId = 0, IsActive = true };
        var store = new Nop.Core.Domain.Stores.Store { Id = 22, DefaultLanguageId = 5 };

        productService.Setup(x => x.GetProductByIdAsync(11)).ReturnsAsync(product);
        customerService.Setup(x => x.GetCustomerByIdAsync(2)).ReturnsAsync(sponsor);
        customerService.Setup(x => x.IsAdminAsync(sponsor)).ReturnsAsync(false);
        storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);
        messageTemplateService.Setup(x => x.GetMessageTemplatesByNameAsync("AIInterview.SponsorInviteCreated", 22))
            .ReturnsAsync(new List<Nop.Core.Domain.Messages.MessageTemplate> { template });
        emailAccountService.Setup(x => x.GetEmailAccountByIdAsync(7)).ReturnsAsync(emailAccount);
        emailAccountService.Setup(x => x.GetAllEmailAccountsAsync()).ReturnsAsync(new List<Nop.Core.Domain.Messages.EmailAccount> { emailAccount });
        webHelper.Setup(x => x.GetStoreLocation(It.IsAny<bool?>())).Returns("https://store.example/");
        inviteRepository.Setup(x => x.InsertAsync(It.IsAny<SponsorInvite>(), true))
            .Returns(Task.CompletedTask);
        workflowMessageService.Setup(x => x.SendNotificationAsync(
                It.IsAny<Nop.Core.Domain.Messages.MessageTemplate>(),
                It.IsAny<Nop.Core.Domain.Messages.EmailAccount>(),
                It.IsAny<int>(),
                It.Is<IList<Nop.Services.Messages.Token>>(tokens =>
                    tokens.Any(token => token.Key == "AIInterview.JobTitle" && Equals(token.Value, "Senior Backend Engineer")) &&
                    tokens.Any(token => token.Key == "AIInterview.InviteUrl" && token.Value.ToString().Contains("sponsorToken=")) &&
                    tokens.Any(token => token.Key == "AIInterview.InviteCode") &&
                    tokens.Any(token => token.Key == "AIInterview.MaxAttempts" && Equals(token.Value, 3)) &&
                    tokens.Any(token => token.Key == "AIInterview.ExpiryDate")),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>()))
            .ReturnsAsync(1);

        var service = new SponsorInviteService(
            inviteRepository.Object,
            productService.Object,
            customerService.Object,
            localizationService.Object,
            workflowMessageService.Object,
            messageTemplateService.Object,
            emailAccountService.Object,
            emailAccountSettings,
            storeContext.Object,
            webHelper.Object);

        await service.CreateInviteAsync(2, "candidate@example.com", 11, 3, DateTime.UtcNow.AddDays(3));

        inviteRepository.Verify(x => x.InsertAsync(It.IsAny<SponsorInvite>(), true), Times.Once);
        workflowMessageService.Verify(x => x.SendNotificationAsync(
            It.IsAny<Nop.Core.Domain.Messages.MessageTemplate>(),
            It.IsAny<Nop.Core.Domain.Messages.EmailAccount>(),
            It.IsAny<int>(),
            It.IsAny<IList<Nop.Services.Messages.Token>>(),
            "candidate@example.com",
            "candidate@example.com",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            true), Times.Once);
    }

    [Test]
    public async Task SponsorInviteService_CreateInviteAsync_FallsBack_To_AnyEmailAccount()
    {
        var inviteRepository = new Mock<IRepository<SponsorInvite>>();
        var productService = new Mock<IProductService>();
        var customerService = new Mock<Nop.Services.Customers.ICustomerService>();
        var localizationService = new Mock<Nop.Services.Localization.ILocalizationService>();
        var workflowMessageService = new Mock<Nop.Services.Messages.IWorkflowMessageService>();
        var messageTemplateService = new Mock<Nop.Services.Messages.IMessageTemplateService>();
        var emailAccountService = new Mock<Nop.Services.Messages.IEmailAccountService>();
        var storeContext = new Mock<Nop.Core.IStoreContext>();
        var webHelper = new Mock<global::Nop.Services.Helpers.IWebHelper>();
        var emailAccountSettings = new Nop.Core.Domain.Messages.EmailAccountSettings { DefaultEmailAccountId = 99 };

        var product = new Product { Id = 12, Name = "Platform Engineer", VendorId = 4 };
        var sponsor = new Customer { Id = 3, VendorId = 4, Email = "sponsor2@example.com" };
        var fallbackEmailAccount = new Nop.Core.Domain.Messages.EmailAccount { Id = 55, Email = "fallback@example.com", DisplayName = "Fallback" };
        var template = new Nop.Core.Domain.Messages.MessageTemplate { Name = "AIInterview.SponsorInviteCreated", EmailAccountId = 0, IsActive = true };
        var store = new Nop.Core.Domain.Stores.Store { Id = 31, DefaultLanguageId = 8 };

        productService.Setup(x => x.GetProductByIdAsync(12)).ReturnsAsync(product);
        customerService.Setup(x => x.GetCustomerByIdAsync(3)).ReturnsAsync(sponsor);
        customerService.Setup(x => x.IsAdminAsync(sponsor)).ReturnsAsync(false);
        storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);
        messageTemplateService.Setup(x => x.GetMessageTemplatesByNameAsync("AIInterview.SponsorInviteCreated", 31))
            .ReturnsAsync(new List<Nop.Core.Domain.Messages.MessageTemplate> { template });
        emailAccountService.Setup(x => x.GetEmailAccountByIdAsync(99)).ReturnsAsync((Nop.Core.Domain.Messages.EmailAccount)null);
        emailAccountService.Setup(x => x.GetAllEmailAccountsAsync()).ReturnsAsync(new List<Nop.Core.Domain.Messages.EmailAccount> { fallbackEmailAccount });
        webHelper.Setup(x => x.GetStoreLocation(It.IsAny<bool?>())).Returns("https://store.example/");
        inviteRepository.Setup(x => x.InsertAsync(It.IsAny<SponsorInvite>(), true))
            .Returns(Task.CompletedTask);
        workflowMessageService.Setup(x => x.SendNotificationAsync(
                It.IsAny<Nop.Core.Domain.Messages.MessageTemplate>(),
                fallbackEmailAccount,
                8,
                It.IsAny<IList<Nop.Services.Messages.Token>>(),
                "candidate2@example.com",
                "candidate2@example.com",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                true))
            .ReturnsAsync(1);

        var service = new SponsorInviteService(
            inviteRepository.Object,
            productService.Object,
            customerService.Object,
            localizationService.Object,
            workflowMessageService.Object,
            messageTemplateService.Object,
            emailAccountService.Object,
            emailAccountSettings,
            storeContext.Object,
            webHelper.Object);

        await service.CreateInviteAsync(3, "candidate2@example.com", 12, 2, DateTime.UtcNow.AddDays(2));

        workflowMessageService.Verify(x => x.SendNotificationAsync(
            It.IsAny<Nop.Core.Domain.Messages.MessageTemplate>(),
            fallbackEmailAccount,
            8,
            It.IsAny<IList<Nop.Services.Messages.Token>>(),
            "candidate2@example.com",
            "candidate2@example.com",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            true), Times.Once);
    }

    [Test]
    public async Task InterviewCompletion_NotifiesApplicantAndVendor_WithReportLinks()
    {
        var customerService = new Mock<Nop.Services.Customers.ICustomerService>();
        var productService = new Mock<Nop.Services.Catalog.IProductService>();
        var vendorService = new Mock<Nop.Services.Vendors.IVendorService>();
        var applicationService = new Mock<IApplicationService>();
        var customer = new Customer { Id = 5, Email = "candidate@example.com", FirstName = "Casey", LastName = "Jones" };
        var product = new Product { Id = 9, Name = "Platform Engineer", VendorId = 3 };
        var vendor = new Nop.Core.Domain.Vendors.Vendor { Id = 3, Name = "Example Vendor", Email = "vendor@example.com" };
        var store = new Nop.Core.Domain.Stores.Store { Id = 1 };
        var applicantTemplate = new Nop.Core.Domain.Messages.MessageTemplate { Name = "AIInterview.ApplicantInterviewCompletion" };
        var vendorTemplate = new Nop.Core.Domain.Messages.MessageTemplate { Name = "AIInterview.VendorInterviewCompletion" };
        var emailAccount = new Nop.Core.Domain.Messages.EmailAccount { Id = 1, Email = "store@example.com" };

        customerService.Setup(x => x.GetCustomerByIdAsync(5)).ReturnsAsync(customer);
        productService.Setup(x => x.GetProductByIdAsync(9)).ReturnsAsync(product);
        vendorService.Setup(x => x.GetVendorByIdAsync(3)).ReturnsAsync(vendor);
        _storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);
        _messageTemplateService.Setup(x => x.GetMessageTemplatesByNameAsync("AIInterview.ApplicantInterviewCompletion", 1))
            .ReturnsAsync(new List<Nop.Core.Domain.Messages.MessageTemplate> { applicantTemplate });
        _messageTemplateService.Setup(x => x.GetMessageTemplatesByNameAsync("AIInterview.VendorInterviewCompletion", 1))
            .ReturnsAsync(new List<Nop.Core.Domain.Messages.MessageTemplate> { vendorTemplate });
        _emailAccountService.Setup(x => x.GetEmailAccountByIdAsync(It.IsAny<int>())).ReturnsAsync(emailAccount);
        _webHelper.Setup(x => x.GetStoreLocation(false)).Returns("https://store.example/");

        var service = new InterviewSessionService(
            _sessionRepository.Object,
            customerService.Object,
            applicationService.Object,
            productService.Object,
            _workflowMessageService.Object,
            _messageTemplateService.Object,
            _emailAccountService.Object,
            _messageTokenProvider.Object,
            _emailAccountSettings,
            _storeContext.Object,
            _webHelper.Object,
            vendorService.Object);

        await service.SendInterviewCompletionNotificationAsync(new InterviewSession
        {
            Id = 12,
            CustomerId = 5,
            ProductId = 9,
            Score = 91,
            QuestionScores = "[90,92]",
            CompletedOnUtc = DateTime.UtcNow
        }, 1);

        _workflowMessageService.Verify(x => x.SendNotificationAsync(
            It.IsAny<Nop.Core.Domain.Messages.MessageTemplate>(),
            emailAccount,
            1,
            It.Is<IList<Nop.Services.Messages.Token>>(tokens =>
                tokens.Any(token => token.Key == "AIInterview.OverallScore") &&
                tokens.Any(token => token.Key.Contains("ReportUrl"))),
            It.IsAny<string>(),
            It.IsAny<string>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false), Times.Exactly(2));
    }

    [Test]
    public async Task CreditService_AddCreditAsync_CreatesWalletAndLedger()
    {
        var walletRepository = new Mock<IRepository<CreditWallet>>();
        var ledgerRepository = new Mock<IRepository<CreditLedgerEntry>>();

        walletRepository.Setup(x => x.GetAllAsync(It.IsAny<Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>>>(), It.IsAny<Func<ICacheKeyService, CacheKey>>(), true))
            .ReturnsAsync(new List<CreditWallet>());
        walletRepository.Setup(x => x.InsertAsync(It.IsAny<CreditWallet>(), true))
            .Callback<CreditWallet, bool>((wallet, publishEvent) => wallet.Id = 17)
            .Returns(Task.CompletedTask);
        walletRepository.Setup(x => x.UpdateAsync(It.IsAny<CreditWallet>(), true))
            .Returns(Task.CompletedTask);
        ledgerRepository.Setup(x => x.InsertAsync(It.IsAny<CreditLedgerEntry>(), true))
            .Returns(Task.CompletedTask);

        var service = new CreditService(walletRepository.Object, ledgerRepository.Object);

        await service.AddCreditAsync(55, 25, "Admin top-up");

        walletRepository.Verify(x => x.InsertAsync(It.Is<CreditWallet>(wallet => wallet.CustomerId == 55 && wallet.Balance == 25), true), Times.Once);
        walletRepository.Verify(x => x.UpdateAsync(It.Is<CreditWallet>(wallet => wallet.CustomerId == 55 && wallet.Balance == 25), true), Times.Once);
        ledgerRepository.Verify(x => x.InsertAsync(It.Is<CreditLedgerEntry>(entry => entry.CreditWalletId == 17 && entry.Amount == 25 && entry.Remarks == "Admin top-up"), true), Times.Once);
    }

    [Test]
    public async Task JobRequirementService_DefaultsToFalse_WhenAttributesMissing()
    {
        var genericAttributeService = new Mock<IGenericAttributeService>();
        var productService = new Mock<IProductService>();
        var productTemplateService = new Mock<IProductTemplateService>();

        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(7))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 7,
                ViewPath = AIInterviewDefaults.JobProductTemplateViewPath
            });

        var service = new JobRequirementService(genericAttributeService.Object, productService.Object, productTemplateService.Object, new AIInterviewSettings { MinimumScore = 42 });
        var product = new Product { Id = 5, ProductTemplateId = 7 };
        genericAttributeService.Setup(x => x.GetAttributeAsync<decimal>(product, AIInterviewDefaults.JobMinimumScoreAttributeName, 0, 42m))
            .ReturnsAsync(42m);

        var result = await service.GetRequirementsAsync(product);

        Assert.That(result.IsJobProduct, Is.True);
        Assert.That(result.ResumeRequired, Is.False);
        Assert.That(result.InterviewRequired, Is.False);
        Assert.That(result.MinimumScore, Is.EqualTo(42));
    }

    [Test]
    public async Task JobRequirementService_SavesFlags_AndMinimumScore_ForJobProduct()
    {
        var genericAttributeService = new Mock<IGenericAttributeService>();
        var productService = new Mock<IProductService>();
        var productTemplateService = new Mock<IProductTemplateService>();

        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(7))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 7,
                ViewPath = AIInterviewDefaults.JobProductTemplateViewPath
            });

        var service = new JobRequirementService(genericAttributeService.Object, productService.Object, productTemplateService.Object);
        var product = new Product { Id = 5, ProductTemplateId = 7 };

        genericAttributeService.Setup(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobResumeRequiredAttributeName, true, 0))
            .Returns(Task.CompletedTask);
        genericAttributeService.Setup(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobInterviewRequiredAttributeName, false, 0))
            .Returns(Task.CompletedTask);
        genericAttributeService.Setup(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobMinimumScoreAttributeName, 87.5m, 0))
            .Returns(Task.CompletedTask);

        await service.SaveRequirementsAsync(product, true, false, 87.5m);

        genericAttributeService.Verify(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobResumeRequiredAttributeName, true, 0), Times.Once);
        genericAttributeService.Verify(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobInterviewRequiredAttributeName, false, 0), Times.Once);
        genericAttributeService.Verify(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobMinimumScoreAttributeName, 87.5m, 0), Times.Once);
    }

    [Test]
    public async Task JobRequirementService_Reads_MinimumScore_From_ProductAttribute()
    {
        var genericAttributeService = new Mock<IGenericAttributeService>();
        var productService = new Mock<IProductService>();
        var productTemplateService = new Mock<IProductTemplateService>();

        productTemplateService.Setup(x => x.GetProductTemplateByIdAsync(7))
            .ReturnsAsync(new Nop.Core.Domain.Catalog.ProductTemplate
            {
                Id = 7,
                ViewPath = AIInterviewDefaults.JobProductTemplateViewPath
            });

        var product = new Product { Id = 5, ProductTemplateId = 7 };
        genericAttributeService.Setup(x => x.GetAttributeAsync<bool>(product, AIInterviewDefaults.JobResumeRequiredAttributeName, 0, false))
            .ReturnsAsync(false);
        genericAttributeService.Setup(x => x.GetAttributeAsync<bool>(product, AIInterviewDefaults.JobInterviewRequiredAttributeName, 0, true))
            .ReturnsAsync(false);
        genericAttributeService.Setup(x => x.GetAttributeAsync<decimal>(product, AIInterviewDefaults.JobMinimumScoreAttributeName, 0, 0m))
            .ReturnsAsync(73.25m);

        var service = new JobRequirementService(genericAttributeService.Object, productService.Object, productTemplateService.Object);

        var result = await service.GetRequirementsAsync(product);

        Assert.That(result.ResumeRequired, Is.False);
        Assert.That(result.InterviewRequired, Is.False);
        Assert.That(result.MinimumScore, Is.EqualTo(73.25m));
    }
}
