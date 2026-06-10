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

        var service = new JobRequirementService(genericAttributeService.Object, productService.Object, productTemplateService.Object);
        var product = new Product { Id = 5, ProductTemplateId = 7 };

        var result = await service.GetRequirementsAsync(product);

        Assert.That(result.IsJobProduct, Is.True);
        Assert.That(result.ResumeRequired, Is.False);
        Assert.That(result.InterviewRequired, Is.False);
    }

    [Test]
    public async Task JobRequirementService_SavesFlags_ForJobProduct()
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

        await service.SaveRequirementsAsync(product, true, false);

        genericAttributeService.Verify(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobResumeRequiredAttributeName, true, 0), Times.Once);
        genericAttributeService.Verify(x => x.SaveAttributeAsync(product, AIInterviewDefaults.JobInterviewRequiredAttributeName, false, 0), Times.Once);
    }
}
