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
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Messages;
using Microsoft.Extensions.Logging;
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
    private Mock<Nop.Services.Helpers.IDateTimeHelper> _dateTimeHelper;
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
        _dateTimeHelper = new Mock<Nop.Services.Helpers.IDateTimeHelper>();

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
            _webHelper.Object,
            _dateTimeHelper.Object);
    }

    [Test]
    public async Task CanInsertJobApplication()
    {
        var application = new JobApplication { JobTitle = "Software Engineer" };
        await _applicationService.InsertJobApplicationAsync(application);
        _applicationRepository.Verify(r => r.InsertAsync(application, true), Times.Once);
    }

    [Test]
    public async Task SendApplicationSubmittedNotificationAsync_DoesNotSend_WhenCustomerEmailMissing()
    {
        var application = new JobApplication { CustomerId = 77, JobTitle = "Platform Engineer" };
        var store = new Nop.Core.Domain.Stores.Store { Id = 11 };
        var template = new Nop.Core.Domain.Messages.MessageTemplate { Name = "AIInterview.ApplicationSubmitted", EmailAccountId = 0, IsActive = true };

        _storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);
        _messageTemplateService.Setup(x => x.GetMessageTemplatesByNameAsync("AIInterview.ApplicationSubmitted", 11))
            .ReturnsAsync(new List<Nop.Core.Domain.Messages.MessageTemplate> { template });
        _customerRepository.Setup(x => x.GetByIdAsync(77))
            .ReturnsAsync(new Customer { Id = 77, Email = " " });

        await _applicationService.SendApplicationSubmittedNotificationAsync(application, 1);

        _workflowMessageService.Verify(x => x.SendNotificationAsync(
            It.IsAny<Nop.Core.Domain.Messages.MessageTemplate>(),
            It.IsAny<Nop.Core.Domain.Messages.EmailAccount>(),
            It.IsAny<int>(),
            It.IsAny<IList<Nop.Services.Messages.Token>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>()), Times.Never);
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
                    tokens.Any(token => token.Key == "AIInterview.InviteUrl" && token.Value.ToString().Contains("/mockaiinterview/start?productId=11")) &&
                    tokens.Any(token => token.Key == "AIInterview.InviteUrl" && token.Value.ToString().Contains("sponsorToken=")) &&
                    tokens.Any(token => token.Key == "AIInterview.InviteUrl" && !token.Value.ToString().Contains("/aiinterview/mock/start")) &&
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
    public async Task SponsorInviteService_CreateInviteAsync_Builds_MockStartRoute_Url()
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

        var product = new Product { Id = 77, Name = "QA Engineer", VendorId = 3 };
        var sponsor = new Customer { Id = 2, VendorId = 3, Email = "sponsor@example.com" };
        var emailAccount = new Nop.Core.Domain.Messages.EmailAccount { Id = 7, Email = "store@example.com", DisplayName = "Store" };
        var template = new Nop.Core.Domain.Messages.MessageTemplate { Name = "AIInterview.SponsorInviteCreated", EmailAccountId = 0, IsActive = true };
        var store = new Nop.Core.Domain.Stores.Store { Id = 22, DefaultLanguageId = 5 };

        productService.Setup(x => x.GetProductByIdAsync(77)).ReturnsAsync(product);
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
                It.IsAny<IList<Nop.Services.Messages.Token>>(),
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

        await service.CreateInviteAsync(2, "candidate@example.com", 77, 3, DateTime.UtcNow.AddDays(3));

        workflowMessageService.Verify(x => x.SendNotificationAsync(
            It.IsAny<Nop.Core.Domain.Messages.MessageTemplate>(),
            It.IsAny<Nop.Core.Domain.Messages.EmailAccount>(),
            It.IsAny<int>(),
            It.Is<IList<Nop.Services.Messages.Token>>(tokens =>
                tokens.Any(token => token.Key == "AIInterview.InviteUrl" &&
                    token.Value.ToString().Contains("/mockaiinterview/start?productId=77")) &&
                tokens.Any(token => token.Key == "AIInterview.InviteUrl" &&
                    token.Value.ToString().Contains("sponsorToken=")) &&
                tokens.Any(token => token.Key == "AIInterview.InviteUrl" &&
                    !token.Value.ToString().Contains("/aiinterview/mock/start"))),
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
            vendorService.Object,
            _dateTimeHelper.Object);

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
    public async Task InterviewSessionService_EnsureRecordingShareTokenAsync_GeneratesPersistentToken()
    {
        var sessions = new List<InterviewSession>
        {
            new() { Id = 1, RecordingUrl = "https://storage.example.com/container/one.webm" }
        };
        _sessionRepository.SetupGet(x => x.Table).Returns(() => sessions.AsQueryable());
        _sessionRepository.Setup(x => x.UpdateAsync(It.IsAny<InterviewSession>(), true))
            .Returns(Task.CompletedTask);

        var service = new InterviewSessionService(
            _sessionRepository.Object,
            new Mock<Nop.Services.Customers.ICustomerService>().Object,
            new Mock<IApplicationService>().Object,
            new Mock<Nop.Services.Catalog.IProductService>().Object,
            _workflowMessageService.Object,
            _messageTemplateService.Object,
            _emailAccountService.Object,
            _messageTokenProvider.Object,
            _emailAccountSettings,
            _storeContext.Object,
            _webHelper.Object,
            new Mock<Nop.Services.Vendors.IVendorService>().Object,
            _dateTimeHelper.Object);

        var firstToken = await service.EnsureRecordingShareTokenAsync(sessions[0]);
        var secondToken = await service.EnsureRecordingShareTokenAsync(sessions[0]);

        Assert.That(firstToken, Is.Not.Null.And.Not.Empty);
        Assert.That(secondToken, Is.EqualTo(firstToken));
        Assert.That(firstToken, Is.Not.EqualTo("1"));
        Assert.That(firstToken, Does.Not.Contain("one.webm"));
        Assert.That(sessions[0].RecordingShareEnabled, Is.True);
        Assert.That(sessions[0].RecordingShareCreatedOnUtc, Is.Not.Null);
        _sessionRepository.Verify(x => x.UpdateAsync(It.Is<InterviewSession>(session =>
            session.RecordingShareToken == firstToken && session.RecordingShareEnabled), true), Times.Once);
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
    public async Task CreditActivityService_BuildCreditActivityModel_Aggregates_CurrentCustomer_Ledger()
    {
        var customer = new Customer { Id = 7 };
        var createdUtc = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var wallets = new List<CreditWallet>
        {
            new() { Id = 1, CustomerId = 7, Balance = 4 },
            new() { Id = 2, CustomerId = 7, Balance = 2 },
            new() { Id = 9, CustomerId = 99, Balance = 100 }
        };
        var ledgers = new List<CreditLedgerEntry>
        {
            new() { Id = 1, CreditWalletId = 1, Amount = 10, TransactionType = "Deposit", LedgerSource = CreditLedgerSources.Order, ProductId = 101, OrderId = 500, CreatedOnUtc = createdUtc },
            new() { Id = 2, CreditWalletId = 2, Amount = 5, TransactionType = "Deposit", Remarks = "internal admin note", CreatedOnUtc = createdUtc.AddHours(1) },
            new() { Id = 3, CreditWalletId = 1, Amount = -1, TransactionType = "Withdrawal", LedgerSource = CreditLedgerSources.InterviewUsage, ProductId = 201, CreatedOnUtc = createdUtc.AddHours(2) },
            new() { Id = 4, CreditWalletId = 2, Amount = -2, TransactionType = "Withdrawal", LedgerSource = CreditLedgerSources.SponsorInterviewUsage, ProductId = 202, SponsorInviteId = 33, CreatedOnUtc = createdUtc.AddHours(3) },
            new() { Id = 5, CreditWalletId = 9, Amount = 100, TransactionType = "Deposit", ProductId = 999, CreatedOnUtc = createdUtc.AddHours(4) }
        };
        var service = CreateCreditActivityService(wallets, ledgers,
            products: new List<Product>
            {
                new() { Id = 101, Name = "Credit Pack 10" },
                new() { Id = 201, Name = "Backend Engineer" },
                new() { Id = 202, Name = "Data Analyst" },
                new() { Id = 999, Name = "Other Customer Product" }
            });

        var model = await service.BuildCreditActivityModelAsync(customer, page: 1, pageSize: 10);

        Assert.That(model.CurrentBalance, Is.EqualTo(6));
        Assert.That(model.CurrentBalanceDisplay, Is.EqualTo("6"));
        Assert.That(model.TotalDeposited, Is.EqualTo(15));
        Assert.That(model.TotalWithdrawn, Is.EqualTo(3));
        Assert.That(model.Entries.Count, Is.EqualTo(4));

        Assert.That(model.Entries.Select(entry => entry.JobProduct), Does.Not.Contain("Other Customer Product"));
        Assert.That(model.Entries.Select(entry => entry.CreditsDisplay), Is.EqualTo(new[] { "-2", "-1", "+5", "+10" }));
        Assert.That(model.Entries.Select(entry => entry.BalanceAfterDisplay), Is.EqualTo(new[] { "12", "14", "15", "10" }));

        var sponsored = model.Entries[0];
        Assert.That(sponsored.Source, Is.EqualTo(CreditLedgerSources.SponsorInterviewUsage));
        Assert.That(sponsored.Description, Is.EqualTo("Sponsored interview started"));
        Assert.That(sponsored.JobProduct, Is.EqualTo("Data Analyst"));
        Assert.That(sponsored.CreatedOn, Is.EqualTo(new DateTime(2026, 7, 20, 8, 30, 0)));

        var interview = model.Entries[1];
        Assert.That(interview.Type, Is.EqualTo("Withdrawal"));
        Assert.That(interview.Source, Is.EqualTo(CreditLedgerSources.InterviewUsage));
        Assert.That(interview.Description, Is.EqualTo("Interview started"));
        Assert.That(interview.JobProduct, Is.EqualTo("Backend Engineer"));

        var adminTopUp = model.Entries[2];
        Assert.That(adminTopUp.Type, Is.EqualTo("Deposit"));
        Assert.That(adminTopUp.Source, Is.EqualTo(CreditLedgerSources.AdminTopUp));
        Assert.That(adminTopUp.Description, Is.EqualTo("Admin credit top-up"));
        Assert.That(adminTopUp.JobProduct, Is.EqualTo("Credit top-up"));

        var orderDeposit = model.Entries[3];
        Assert.That(orderDeposit.Source, Is.EqualTo(CreditLedgerSources.Order));
        Assert.That(orderDeposit.Description, Is.EqualTo("Credit pack purchase"));
        Assert.That(orderDeposit.JobProduct, Is.EqualTo("Credit Pack 10"));
    }

    [Test]
    public async Task CreditActivityService_BuildCreditActivityModel_Resolves_Historical_Grant_And_Session_Metadata()
    {
        var customer = new Customer { Id = 8 };
        var createdUtc = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
        var service = CreateCreditActivityService(
            wallets: new List<CreditWallet> { new() { Id = 8, CustomerId = 8, Balance = 4 } },
            ledgers: new List<CreditLedgerEntry>
            {
                new() { Id = 10, CreditWalletId = 8, Amount = 5, TransactionType = "Deposit", Remarks = "raw order #123", CreatedOnUtc = createdUtc },
                new() { Id = 11, CreditWalletId = 8, Amount = -1, TransactionType = "Withdrawal", Remarks = "Interview Start Charge", CreatedOnUtc = createdUtc.AddMinutes(1) }
            },
            grants: new List<CreditPurchaseGrant>
            {
                new() { Id = 1, CustomerId = 8, ProductId = 301, CreditsGranted = 5, CreatedOnUtc = createdUtc.AddMinutes(-1) }
            },
            sessions: new List<InterviewSession>
            {
                new() { Id = 1, CustomerId = 8, ProductId = 401, CreatedOnUtc = createdUtc.AddMinutes(2) }
            },
            products: new List<Product>
            {
                new() { Id = 301, Name = "Starter Credits" },
                new() { Id = 401, Name = "QA Engineer" }
            });

        var model = await service.BuildCreditActivityModelAsync(customer, page: 1, pageSize: 10);

        Assert.That(model.Entries[0].Source, Is.EqualTo(CreditLedgerSources.InterviewUsage));
        Assert.That(model.Entries[0].JobProduct, Is.EqualTo("QA Engineer"));
        Assert.That(model.Entries[0].Description, Is.EqualTo("Interview started"));
        Assert.That(model.Entries[1].Source, Is.EqualTo(CreditLedgerSources.Order));
        Assert.That(model.Entries[1].JobProduct, Is.EqualTo("Starter Credits"));
        Assert.That(model.Entries[1].Description, Is.EqualTo("Credit pack purchase"));
    }

    [Test]
    public async Task CreditActivityService_BuildCreditActivityModel_Returns_Empty_State_Model()
    {
        var service = CreateCreditActivityService(new List<CreditWallet>(), new List<CreditLedgerEntry>());

        var model = await service.BuildCreditActivityModelAsync(new Customer { Id = 10 }, page: 1, pageSize: 5);

        Assert.That(model.CurrentBalanceDisplay, Is.EqualTo("0"));
        Assert.That(model.TotalDepositedDisplay, Is.EqualTo("0"));
        Assert.That(model.TotalWithdrawnDisplay, Is.EqualTo("0"));
        Assert.That(model.TotalCount, Is.EqualTo(0));
        Assert.That(model.TotalPages, Is.EqualTo(0));
        Assert.That(model.Entries, Is.Empty);
    }

    [TestCase(CreditDepositSources.ViaOrder)]
    [TestCase(CreditDepositSources.ViaAdminTopUp)]
    public async Task CreditDepositNotificationService_Sends_Deposit_Email_With_Credit_Tokens(string source)
    {
        var customerService = new Mock<ICustomerService>();
        var walletRepository = new Mock<IRepository<CreditWallet>>();
        var ledgerRepository = new Mock<IRepository<CreditLedgerEntry>>();
        var workflowMessageService = new Mock<IWorkflowMessageService>();
        var messageTemplateService = new Mock<IMessageTemplateService>();
        var emailAccountService = new Mock<IEmailAccountService>();
        var messageTokenProvider = new Mock<IMessageTokenProvider>();
        var storeContext = new Mock<Nop.Core.IStoreContext>();
        var webHelper = new Mock<IWebHelper>();
        var logger = new Mock<ILogger<CreditDepositNotificationService>>();
        var emailAccountSettings = new Nop.Core.Domain.Messages.EmailAccountSettings { DefaultEmailAccountId = 7 };
        var customer = new Customer { Id = 5, Email = "candidate@example.com", FirstName = "Asha", LastName = "Rao" };
        var wallets = new List<CreditWallet>
        {
            new() { Id = 11, CustomerId = 5, Balance = 15 },
            new() { Id = 12, CustomerId = 5, Balance = 2.5m }
        };
        var ledgerEntries = new List<CreditLedgerEntry>
        {
            new() { CreditWalletId = 11, Amount = 20, TransactionType = "Deposit" },
            new() { CreditWalletId = 11, Amount = -3, TransactionType = "Withdrawal" },
            new() { CreditWalletId = 12, Amount = -1.25m, TransactionType = "Withdrawal" },
            new() { CreditWalletId = 12, Amount = -9, TransactionType = "Adjustment" },
            new() { CreditWalletId = 99, Amount = -100, TransactionType = "Withdrawal" }
        };
        var template = new Nop.Core.Domain.Messages.MessageTemplate { Name = CreditDepositNotificationService.TemplateName, IsActive = true };
        var emailAccount = new Nop.Core.Domain.Messages.EmailAccount { Id = 7, Email = "store@example.com" };
        var store = new Nop.Core.Domain.Stores.Store { Id = 4, DefaultLanguageId = 9 };

        customerService.Setup(x => x.GetCustomerByIdAsync(5)).ReturnsAsync(customer);
        walletRepository.Setup(x => x.GetAllAsync(
                It.IsAny<Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>>>(),
                It.IsAny<Func<Nop.Core.Caching.ICacheKeyService, Nop.Core.Caching.CacheKey>>(),
                true))
            .ReturnsAsync((Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>> func, Func<Nop.Core.Caching.ICacheKeyService, Nop.Core.Caching.CacheKey> _, bool __) =>
                func == null ? wallets.ToList() : func(wallets.AsQueryable()).ToList());
        ledgerRepository.SetupGet(x => x.Table).Returns(() => ledgerEntries.AsQueryable());
        storeContext.Setup(x => x.GetCurrentStoreAsync()).ReturnsAsync(store);
        messageTemplateService.Setup(x => x.GetMessageTemplatesByNameAsync(CreditDepositNotificationService.TemplateName, 4))
            .ReturnsAsync(new List<Nop.Core.Domain.Messages.MessageTemplate> { template });
        emailAccountService.Setup(x => x.GetEmailAccountByIdAsync(7)).ReturnsAsync(emailAccount);
        messageTokenProvider.Setup(x => x.AddCustomerTokensAsync(It.IsAny<IList<Token>>(), customer))
            .Returns(Task.CompletedTask);
        webHelper.Setup(x => x.GetStoreLocation(It.IsAny<bool?>())).Returns("https://store.example/");
        workflowMessageService.Setup(x => x.SendNotificationAsync(
                template,
                emailAccount,
                9,
                It.Is<IList<Token>>(tokens =>
                    tokens.Any(token => token.Key == "AIInterview.CreditsDeposited" && Equals(token.Value, "10")) &&
                    tokens.Any(token => token.Key == "AIInterview.DepositSource" && Equals(token.Value, source)) &&
                    tokens.Any(token => token.Key == "AIInterview.TotalCredits" && Equals(token.Value, "17.5")) &&
                    tokens.Any(token => token.Key == "AIInterview.WithdrawnCredits" && Equals(token.Value, "4.25")) &&
                    tokens.Any(token => token.Key == "AIInterview.CreditPageUrl" && Equals(token.Value, "https://store.example/pricing")) &&
                    tokens.Any(token => token.Key == "AIInterview.OrderId" && Equals(token.Value, "77")) &&
                    tokens.Any(token => token.Key == "AIInterview.DepositRemarks" && Equals(token.Value, "test deposit"))),
                "candidate@example.com",
                "Asha Rao",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                true))
            .ReturnsAsync(1);

        var service = new CreditDepositNotificationService(
            customerService.Object,
            walletRepository.Object,
            ledgerRepository.Object,
            workflowMessageService.Object,
            messageTemplateService.Object,
            emailAccountService.Object,
            messageTokenProvider.Object,
            emailAccountSettings,
            storeContext.Object,
            webHelper.Object,
            logger.Object);

        await service.SendCreditDepositedNotificationAsync(new CreditDepositNotificationRequest
        {
            CustomerId = 5,
            CreditsDeposited = 10,
            DepositSource = source,
            OrderId = 77,
            Remarks = "test deposit"
        });

        workflowMessageService.Verify(x => x.SendNotificationAsync(
            template,
            emailAccount,
            9,
            It.IsAny<IList<Token>>(),
            "candidate@example.com",
            "Asha Rao",
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
    public async Task CreditDepositNotificationService_Skips_Missing_Email_Without_Failing()
    {
        var customerService = new Mock<ICustomerService>();
        var walletRepository = new Mock<IRepository<CreditWallet>>();
        var ledgerRepository = new Mock<IRepository<CreditLedgerEntry>>();
        var workflowMessageService = new Mock<IWorkflowMessageService>();
        var service = new CreditDepositNotificationService(
            customerService.Object,
            walletRepository.Object,
            ledgerRepository.Object,
            workflowMessageService.Object,
            new Mock<IMessageTemplateService>().Object,
            new Mock<IEmailAccountService>().Object,
            new Mock<IMessageTokenProvider>().Object,
            new Nop.Core.Domain.Messages.EmailAccountSettings(),
            new Mock<Nop.Core.IStoreContext>().Object,
            new Mock<IWebHelper>().Object,
            new Mock<ILogger<CreditDepositNotificationService>>().Object);

        customerService.Setup(x => x.GetCustomerByIdAsync(5)).ReturnsAsync(new Customer { Id = 5, Email = " " });

        await service.SendCreditDepositedNotificationAsync(new CreditDepositNotificationRequest
        {
            CustomerId = 5,
            CreditsDeposited = 10,
            DepositSource = CreditDepositSources.ViaAdminTopUp
        });

        workflowMessageService.Verify(x => x.SendNotificationAsync(
            It.IsAny<Nop.Core.Domain.Messages.MessageTemplate>(),
            It.IsAny<Nop.Core.Domain.Messages.EmailAccount>(),
            It.IsAny<int>(),
            It.IsAny<IList<Token>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>()), Times.Never);
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

        await service.SaveRequirementsAsync(product, true, false, 87.5m, 5);

        genericAttributeService.Verify(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobResumeRequiredAttributeName, true, 0), Times.Once);
        genericAttributeService.Verify(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobInterviewRequiredAttributeName, false, 0), Times.Once);
        genericAttributeService.Verify(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobMinimumScoreAttributeName, 87.5m, 0), Times.Once);
        genericAttributeService.Verify(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobQuestionCountAttributeName, 5, 0), Times.Once);
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

    private static CreditActivityService CreateCreditActivityService(
        List<CreditWallet> wallets,
        List<CreditLedgerEntry> ledgers,
        List<CreditPurchaseGrant> grants = null,
        List<InterviewSession> sessions = null,
        List<JobApplication> applications = null,
        List<SponsorInvite> invites = null,
        List<Product> products = null)
    {
        grants ??= new List<CreditPurchaseGrant>();
        sessions ??= new List<InterviewSession>();
        applications ??= new List<JobApplication>();
        invites ??= new List<SponsorInvite>();
        products ??= new List<Product>();

        var walletRepository = new Mock<IRepository<CreditWallet>>();
        var ledgerRepository = new Mock<IRepository<CreditLedgerEntry>>();
        var grantRepository = new Mock<IRepository<CreditPurchaseGrant>>();
        var sessionRepository = new Mock<IRepository<InterviewSession>>();
        var applicationRepository = new Mock<IRepository<JobApplication>>();
        var inviteRepository = new Mock<IRepository<SponsorInvite>>();
        var productService = new Mock<IProductService>();
        var dateTimeHelper = new Mock<IDateTimeHelper>();
        var indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

        walletRepository.Setup(x => x.GetAllAsync(
                It.IsAny<Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>>>(),
                It.IsAny<Func<ICacheKeyService, CacheKey>>(),
                true))
            .ReturnsAsync((Func<IQueryable<CreditWallet>, IQueryable<CreditWallet>> func, Func<ICacheKeyService, CacheKey> _, bool __) =>
                func == null ? wallets.ToList() : func(wallets.AsQueryable()).ToList());
        ledgerRepository.SetupGet(x => x.Table).Returns(() => ledgers.AsQueryable());
        grantRepository.SetupGet(x => x.Table).Returns(() => grants.AsQueryable());
        sessionRepository.SetupGet(x => x.Table).Returns(() => sessions.AsQueryable());
        inviteRepository.SetupGet(x => x.Table).Returns(() => invites.AsQueryable());
        applicationRepository.Setup(x => x.GetByIdAsync(It.IsAny<int?>(), It.IsAny<Func<ICacheKeyService, CacheKey>>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((int? id, Func<ICacheKeyService, CacheKey> _, bool __, bool ___) => applications.FirstOrDefault(application => application.Id == id));
        productService.Setup(x => x.GetProductByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => products.FirstOrDefault(product => product.Id == id));
        dateTimeHelper.Setup(x => x.GetCustomerTimeZoneAsync(It.IsAny<Customer>())).ReturnsAsync(indiaTimeZone);
        dateTimeHelper.Setup(x => x.ConvertToUserTime(It.IsAny<DateTime>(), It.IsAny<TimeZoneInfo>(), It.IsAny<TimeZoneInfo>()))
            .Returns((DateTime value, TimeZoneInfo sourceTimeZone, TimeZoneInfo destinationTimeZone) =>
                TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(value, DateTimeKind.Utc), sourceTimeZone, destinationTimeZone));

        return new CreditActivityService(
            walletRepository.Object,
            ledgerRepository.Object,
            grantRepository.Object,
            sessionRepository.Object,
            applicationRepository.Object,
            inviteRepository.Object,
            productService.Object,
            dateTimeHelper.Object);
    }
}
