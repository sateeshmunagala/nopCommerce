using Moq;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Services;
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
}
