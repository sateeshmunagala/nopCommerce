using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Vendors;
using Nop.Services.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nop.Services.Common;
using Nop.Services.Messages;
using System.Globalization;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class ApplicationService : IApplicationService
{
    private readonly IRepository<JobApplication> _applicationRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<InterviewSession> _sessionRepository;
    private readonly IRepository<Product> _productRepository;
    private readonly Nop.Services.Messages.IWorkflowMessageService _workflowMessageService;
    private readonly Nop.Services.Messages.IMessageTemplateService _messageTemplateService;
    private readonly Nop.Services.Messages.IEmailAccountService _emailAccountService;
    private readonly Nop.Services.Messages.IMessageTokenProvider _messageTokenProvider;
    private readonly EmailAccountSettings _emailAccountSettings;
    private readonly IStoreContext _storeContext;
    private readonly IWebHelper _webHelper;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApplicationService> _logger;

    public ApplicationService(IRepository<JobApplication> applicationRepository,
        IRepository<Customer> customerRepository,
        IRepository<InterviewSession> sessionRepository,
        IRepository<Product> productRepository,
        Nop.Services.Messages.IWorkflowMessageService workflowMessageService,
        Nop.Services.Messages.IMessageTemplateService messageTemplateService,
        Nop.Services.Messages.IEmailAccountService emailAccountService,
        Nop.Services.Messages.IMessageTokenProvider messageTokenProvider,
        EmailAccountSettings emailAccountSettings,
        IStoreContext storeContext,
        IWebHelper webHelper,
        IDateTimeHelper dateTimeHelper,
        IServiceProvider serviceProvider = null,
        ILogger<ApplicationService> logger = null)
    {
        _applicationRepository = applicationRepository;
        _customerRepository = customerRepository;
        _sessionRepository = sessionRepository;
        _productRepository = productRepository;
        _workflowMessageService = workflowMessageService;
        _messageTemplateService = messageTemplateService;
        _emailAccountService = emailAccountService;
        _messageTokenProvider = messageTokenProvider;
        _emailAccountSettings = emailAccountSettings;
        _storeContext = storeContext;
        _webHelper = webHelper;
        _dateTimeHelper = dateTimeHelper;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task SendApplicationSubmittedNotificationAsync(JobApplication application, int languageId)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        if (store == null) return;
        var templates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.ApplicationSubmitted", store.Id);
        var template = templates.FirstOrDefault();
        if (template == null) return;

        var customer = await _customerRepository.GetByIdAsync(application.CustomerId);
        if (customer == null) return;
        if (string.IsNullOrWhiteSpace(customer.Email)) return;

        var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(template.EmailAccountId > 0 ? template.EmailAccountId : _emailAccountSettings.DefaultEmailAccountId);
        if (emailAccount == null) emailAccount = (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();
        if (emailAccount == null) return;

        var tokens = new List<Nop.Services.Messages.Token>();
        await _messageTokenProvider.AddCustomerTokensAsync(tokens, customer);
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.JobTitle", application.JobTitle));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.MyApplicationsUrl", $"{_webHelper.GetStoreLocation()}aiinterview/my-applications"));

        await _workflowMessageService.SendNotificationAsync(template, emailAccount, languageId, tokens, customer.Email, customer.FirstName + " " + customer.LastName);
    }

    public async Task SendApplicationStatusUpdateNotificationAsync(JobApplication application, int languageId)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        if (store == null) return;
        var customer = await _customerRepository.GetByIdAsync(application.CustomerId);
        if (customer == null) return;

        var tokens = new List<Nop.Services.Messages.Token>();
        await _messageTokenProvider.AddCustomerTokensAsync(tokens, customer);
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.JobTitle", application.JobTitle));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.NewStatus", application.Status));
        var customerTimeZone = await _dateTimeHelper.GetCustomerTimeZoneAsync(customer);
        var updateTimestamp = _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, TimeZoneInfo.Utc, customerTimeZone);
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.UpdateTimestamp", updateTimestamp.ToString("g")));
        var storeLocation = (_webHelper.GetStoreLocation() ?? string.Empty).TrimEnd('/');
        var candidateDashboardUrl = $"{storeLocation}/my-activity";
        var session = await _sessionRepository.Table
            .Where(item => item.JobApplicationId == application.Id && item.CompletedOnUtc.HasValue)
            .OrderByDescending(item => item.CompletedOnUtc)
            .FirstOrDefaultAsync();
        var completionTimestamp = session?.CompletedOnUtc.HasValue == true
            ? _dateTimeHelper.ConvertToUserTime(session.CompletedOnUtc.Value, TimeZoneInfo.Utc, customerTimeZone).ToString("g")
            : string.Empty;
        var reportUrl = session != null ? $"{storeLocation}/aiinterview/report/{session.Id}" : string.Empty;
        var applicantName = string.Join(" ", new[] { customer.FirstName, customer.LastName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        tokens.Add(new Nop.Services.Messages.Token("AIInterview.InterviewName", application.JobTitle ?? string.Empty));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.ApplicantName", applicantName));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.CompletionTimestamp", completionTimestamp));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.ReportUrl", reportUrl));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.CandidateDashboardUrl", candidateDashboardUrl));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.MyApplicationsUrl", $"{storeLocation}/aiinterview/my-applications"));

        var templates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.ApplicationStatusUpdate", store.Id);
        var template = templates.FirstOrDefault();
        if (template != null && !string.IsNullOrWhiteSpace(customer.Email))
        {
            var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(template.EmailAccountId > 0 ? template.EmailAccountId : _emailAccountSettings.DefaultEmailAccountId);
            if (emailAccount == null)
                emailAccount = (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();

            if (emailAccount != null)
                await _workflowMessageService.SendNotificationAsync(template, emailAccount, languageId, tokens, customer.Email, applicantName);
        }

        var whatsAppService = ResolveWhatsAppService();
        if (whatsAppService == null || string.IsNullOrWhiteSpace(customer.Phone))
            return;

        await TrySendWhatsAppNotificationAsync(
            whatsAppService,
            new AIInterviewWhatsAppNotificationRequest
            {
                CustomerId = customer.Id,
                PhoneNumber = customer.Phone,
                MessageType = string.IsNullOrWhiteSpace(reportUrl)
                    ? "AIInterview.ApplicationStatusChanged"
                    : "AIInterview.ReportSharing",
                MessageBody = $"Hi {applicantName}, your {application.JobTitle} application status is now {application.Status}. View your dashboard: {candidateDashboardUrl}",
                TemplateParameters = new List<string>
                {
                    application.JobTitle ?? string.Empty,
                    applicantName,
                    completionTimestamp,
                    reportUrl,
                    candidateDashboardUrl,
                    application.Status ?? string.Empty
                },
                Tokens = new Dictionary<string, string>
                {
                    ["InterviewName"] = application.JobTitle ?? string.Empty,
                    ["ApplicantName"] = applicantName,
                    ["CompletionTimestamp"] = completionTimestamp,
                    ["ReportUrl"] = reportUrl,
                    ["CandidateDashboardUrl"] = candidateDashboardUrl,
                    ["NewStatus"] = application.Status ?? string.Empty
                }
            },
            application.Id);
    }

    protected virtual IOptionalWhatsAppNotificationService ResolveWhatsAppService()
    {
        try
        {
            var service = OptionalWhatsAppNotificationServiceResolver.Resolve(_serviceProvider);
            return service?.IsEnabled == true ? service : null;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Optional WhatsApp provider could not be resolved for an AIInterview application notification.");
            return null;
        }
    }

    protected virtual async Task TrySendWhatsAppNotificationAsync(
        IOptionalWhatsAppNotificationService whatsAppService,
        AIInterviewWhatsAppNotificationRequest request,
        int applicationId)
    {
        try
        {
            if (whatsAppService?.IsEnabled != true)
                return;

            if (!await whatsAppService.SendNotificationAsync(request))
                _logger?.LogWarning("Optional WhatsApp application status notification was not accepted for application {ApplicationId}.", applicationId);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Optional WhatsApp application status notification failed for application {ApplicationId}.", applicationId);
        }
    }

    public async Task InsertJobApplicationAsync(JobApplication application)
    {
        await _applicationRepository.InsertAsync(application);
    }

    public async Task<JobApplication> GetJobApplicationByIdAsync(int applicationId)
    {
        return await _applicationRepository.GetByIdAsync(applicationId);
    }

    public async Task<IList<JobApplication>> GetJobApplicationsByCustomerIdAsync(int customerId)
    {
        return await _applicationRepository.GetAllAsync(query => query.Where(a => a.CustomerId == customerId));
    }

    public async Task<IList<JobApplication>> GetJobApplicationsByCustomerIdAndJobTitleAsync(int customerId, string jobTitle)
    {
        return await _applicationRepository.GetAllAsync(query => query.Where(a => a.CustomerId == customerId && a.JobTitle == jobTitle));
    }

    public async Task<IList<JobApplication>> GetPreviousResumeSourceApplicationsAsync(int customerId)
    {
        return await _applicationRepository.GetAllAsync(query =>
        {
            var eligibleApplications = query.Where(application =>
                application.CustomerId == customerId &&
                application.ResumeDownloadId > 0);

            return eligibleApplications
                .Where(application => !eligibleApplications.Any(newerApplication =>
                    newerApplication.ResumeDownloadId == application.ResumeDownloadId &&
                    (newerApplication.CreatedOnUtc > application.CreatedOnUtc ||
                     (newerApplication.CreatedOnUtc == application.CreatedOnUtc && newerApplication.Id > application.Id))))
                .OrderByDescending(application => application.CreatedOnUtc)
                .ThenByDescending(application => application.Id)
                .Select(application => new JobApplication
                {
                    Id = application.Id,
                    ResumeDownloadId = application.ResumeDownloadId,
                    CreatedOnUtc = application.CreatedOnUtc
                })
                .Take(3);
        });
    }

    public async Task<IPagedList<JobApplication>> GetApplicationsAsync(string candidateNameOrEmail = null, string status = null, decimal? minScore = null, decimal? maxScore = null, DateTime? startDate = null, DateTime? endDate = null, int productId = 0, int vendorId = 0, int pageIndex = 0, int pageSize = int.MaxValue, bool sortByScore = false)
    {
        var query = _applicationRepository.Table;

        if (productId > 0)
            query = query.Where(a => a.ProductId == productId);

        if (vendorId > 0)
        {
            var productIds = _productRepository.Table.Where(p => p.VendorId == vendorId).Select(p => p.Id);
            query = query.Where(a => productIds.Contains(a.ProductId));
        }

        if (!string.IsNullOrEmpty(status))
            query = query.Where(a => a.Status == status);

        if (startDate.HasValue)
            query = query.Where(a => a.CreatedOnUtc >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.CreatedOnUtc <= endDate.Value);

        if (!string.IsNullOrEmpty(candidateNameOrEmail))
        {
            var customerIds = _customerRepository.Table
                .Where(c => c.Email.Contains(candidateNameOrEmail) || (c.FirstName + " " + c.LastName).Contains(candidateNameOrEmail))
                .Select(c => c.Id);
            query = query.Where(a => customerIds.Contains(a.CustomerId));
        }

        if (minScore.HasValue || maxScore.HasValue || sortByScore)
        {
            var sessionQuery = _sessionRepository.Table
                .GroupBy(s => s.JobApplicationId)
                .Select(g => new { JobApplicationId = g.Key, MaxScore = g.Max(s => s.Score) });

            if (minScore.HasValue)
                sessionQuery = sessionQuery.Where(s => s.MaxScore >= minScore.Value);

            if (maxScore.HasValue)
                sessionQuery = sessionQuery.Where(s => s.MaxScore <= maxScore.Value);

            query = from a in query
                    join s in sessionQuery on a.Id equals s.JobApplicationId into joinedSessions
                    from s in joinedSessions.DefaultIfEmpty()
                    orderby sortByScore ? (s != null ? s.MaxScore : 0) : 0 descending
                    select a;
        }
        else
        {
            query = query.OrderByDescending(a => a.CreatedOnUtc);
        }

        return await query.ToPagedListAsync(pageIndex, pageSize);
    }

    public async Task<int> GetApplicationCountAsync(int productId = 0, int vendorId = 0, string status = null)
    {
        var query = _applicationRepository.Table;

        if (productId > 0)
            query = query.Where(a => a.ProductId == productId);

        if (vendorId > 0)
        {
            var productIds = _productRepository.Table.Where(p => p.VendorId == vendorId).Select(p => p.Id);
            query = query.Where(a => productIds.Contains(a.ProductId));
        }

        if (!string.IsNullOrEmpty(status))
            query = query.Where(a => a.Status == status);

        return await query.CountAsync();
    }

    public async Task UpdateJobApplicationAsync(JobApplication application)
    {
        await _applicationRepository.UpdateAsync(application);
    }
}

public class InterviewSessionService : IInterviewSessionService
{
    public const string RuntimeFeedbackAdminNotificationTemplateName = "AIInterview.RuntimeFeedbackSubmitted.AdminNotification";

    private readonly IRepository<InterviewSession> _sessionRepository;
    private readonly ICustomerService _customerService;
    private readonly IApplicationService _applicationService;
    private readonly Nop.Services.Catalog.IProductService _productService;
    private readonly Nop.Services.Messages.IWorkflowMessageService _workflowMessageService;
    private readonly Nop.Services.Messages.IMessageTemplateService _messageTemplateService;
    private readonly Nop.Services.Messages.IEmailAccountService _emailAccountService;
    private readonly Nop.Services.Messages.IMessageTokenProvider _messageTokenProvider;
    private readonly EmailAccountSettings _emailAccountSettings;
    private readonly IStoreContext _storeContext;
    private readonly IWebHelper _webHelper;
    private readonly IVendorService _vendorService;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly ILogger<InterviewSessionService> _logger;
    private readonly ILocalizationService _localizationService;
    private readonly IRepository<JobApplication> _applicationRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<StoreMapping> _storeMappingRepository;
    private readonly IRepository<GenericAttribute> _genericAttributeRepository;
    private readonly IPictureService _pictureService;
    private readonly IAddressService _addressService;
    private readonly IServiceProvider _serviceProvider;

    public InterviewSessionService(IRepository<InterviewSession> sessionRepository,
        ICustomerService customerService,
        IApplicationService applicationService,
        Nop.Services.Catalog.IProductService productService,
        Nop.Services.Messages.IWorkflowMessageService workflowMessageService,
        Nop.Services.Messages.IMessageTemplateService messageTemplateService,
        Nop.Services.Messages.IEmailAccountService emailAccountService,
        Nop.Services.Messages.IMessageTokenProvider messageTokenProvider,
        EmailAccountSettings emailAccountSettings,
        IStoreContext storeContext,
        IWebHelper webHelper,
        IVendorService vendorService,
        IDateTimeHelper dateTimeHelper,
        ILogger<InterviewSessionService> logger = null,
        ILocalizationService localizationService = null,
        IRepository<JobApplication> applicationRepository = null,
        IRepository<Customer> customerRepository = null,
        IRepository<Product> productRepository = null,
        IRepository<StoreMapping> storeMappingRepository = null,
        IRepository<GenericAttribute> genericAttributeRepository = null,
        IPictureService pictureService = null,
        IAddressService addressService = null,
        IServiceProvider serviceProvider = null)
    {
        _sessionRepository = sessionRepository;
        _customerService = customerService;
        _applicationService = applicationService;
        _productService = productService;
        _workflowMessageService = workflowMessageService;
        _messageTemplateService = messageTemplateService;
        _emailAccountService = emailAccountService;
        _messageTokenProvider = messageTokenProvider;
        _emailAccountSettings = emailAccountSettings;
        _storeContext = storeContext;
        _webHelper = webHelper;
        _vendorService = vendorService;
        _dateTimeHelper = dateTimeHelper;
        _logger = logger;
        _localizationService = localizationService;
        _applicationRepository = applicationRepository;
        _customerRepository = customerRepository;
        _productRepository = productRepository;
        _storeMappingRepository = storeMappingRepository;
        _genericAttributeRepository = genericAttributeRepository;
        _pictureService = pictureService;
        _addressService = addressService;
        _serviceProvider = serviceProvider;
    }

    public async Task SendInterviewCompletionNotificationAsync(InterviewSession session, int languageId)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        if (store == null) return;
        var customer = await _customerService.GetCustomerByIdAsync(session.CustomerId);
        if (customer == null) return;

        var jobTitle = "Practice Interview";
        Vendor vendor = null;
        if (session.ProductId > 0)
        {
            var product = await _productService.GetProductByIdAsync(session.ProductId);
            if (product != null)
            {
                jobTitle = product.Name;
                if (product.VendorId > 0)
                {
                    vendor = await _vendorService.GetVendorByIdAsync(product.VendorId);
                }
            }
        }
        else if (session.JobApplicationId > 0)
        {
            var app = await _applicationService.GetJobApplicationByIdAsync(session.JobApplicationId);
            if (app != null)
            {
                jobTitle = app.JobTitle;
                var product = await _productService.GetProductByIdAsync(app.ProductId);
                if (product != null && product.VendorId > 0)
                {
                    vendor = await _vendorService.GetVendorByIdAsync(product.VendorId);
                }
            }
        }

        var storeLocation = (_webHelper.GetStoreLocation() ?? string.Empty).TrimEnd('/');
        var applicantName = string.Join(" ", new[] { customer.FirstName, customer.LastName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var customerTimeZone = await _dateTimeHelper.GetCustomerTimeZoneAsync(customer);
        var applicantCompletionTimestamp = session.CompletedOnUtc.HasValue
            ? _dateTimeHelper.ConvertToUserTime(session.CompletedOnUtc.Value, TimeZoneInfo.Utc, customerTimeZone).ToString("g")
            : string.Empty;
        var vendorCompletionTimestamp = session.CompletedOnUtc.HasValue
            ? _dateTimeHelper.ConvertToUserTime(session.CompletedOnUtc.Value, TimeZoneInfo.Utc, _dateTimeHelper.DefaultStoreTimeZone).ToString("g")
            : string.Empty;
        string reportShareToken = null;
        try
        {
            reportShareToken = await EnsureReportShareTokenAsync(session);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "AIInterview report-share token was unavailable for session {SessionId}; notification processing will continue.", session.Id);
        }
        var reportUrl = string.IsNullOrWhiteSpace(reportShareToken)
            ? $"{storeLocation}/aiinterview/report/{session.Id}"
            : $"{storeLocation}/aiinterview/report/share/{reportShareToken}";
        var candidateDashboardUrl = $"{storeLocation}/my-activity";

        // Applicant Notification
        var applicantTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.ApplicantInterviewCompletion", store.Id);
        var applicantTemplate = applicantTemplates.FirstOrDefault();
        if (applicantTemplate != null)
        {
            var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(applicantTemplate.EmailAccountId > 0 ? applicantTemplate.EmailAccountId : _emailAccountSettings.DefaultEmailAccountId);
            if (emailAccount == null) emailAccount = (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();
            if (emailAccount != null)
            {
                var tokens = new List<Nop.Services.Messages.Token>();
                await _messageTokenProvider.AddCustomerTokensAsync(tokens, customer);
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.JobTitle", jobTitle));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.OverallScore", session.Score.ToString("N2")));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.CompletionDate", applicantCompletionTimestamp));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.QuestionSummary", session.QuestionScores ?? ""));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.InterviewName", jobTitle));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.ApplicantName", applicantName));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.CompletionTimestamp", applicantCompletionTimestamp));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.ReportUrl", reportUrl));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.CandidateDashboardUrl", candidateDashboardUrl));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.MyApplicationsUrl", $"{storeLocation}/aiinterview/my-applications"));

                await _workflowMessageService.SendNotificationAsync(applicantTemplate, emailAccount, languageId, tokens, customer.Email, customer.FirstName + " " + customer.LastName);
            }
        }

        // Vendor Notification
        if (vendor != null)
        {
            var vendorTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.VendorInterviewCompletion", store.Id);
            var vendorTemplate = vendorTemplates.FirstOrDefault();
            if (vendorTemplate != null)
            {
                var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(vendorTemplate.EmailAccountId > 0 ? vendorTemplate.EmailAccountId : _emailAccountSettings.DefaultEmailAccountId);
                if (emailAccount == null) emailAccount = (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();
                if (emailAccount != null)
                {
                    var tokens = new List<Nop.Services.Messages.Token>();
                    await _messageTokenProvider.AddCustomerTokensAsync(tokens, customer);
                    tokens.Add(new Nop.Services.Messages.Token("Vendor.Name", vendor.Name));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.JobTitle", jobTitle));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.OverallScore", session.Score.ToString("N2")));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.CompletionDate", vendorCompletionTimestamp));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.QuestionSummary", session.QuestionScores ?? ""));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.InterviewName", jobTitle));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.ApplicantName", applicantName));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.CompletionTimestamp", vendorCompletionTimestamp));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.ReportUrl", reportUrl));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.CandidateReportUrl", reportUrl));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.CandidateDashboardUrl", candidateDashboardUrl));

                    await _workflowMessageService.SendNotificationAsync(vendorTemplate, emailAccount, languageId, tokens, vendor.Email, vendor.Name);
                }
            }
        }

        await SendWhatsAppCompletionNotificationsAsync(
            session,
            customer,
            vendor,
            jobTitle,
            applicantName,
            applicantCompletionTimestamp,
            vendorCompletionTimestamp,
            reportUrl,
            candidateDashboardUrl);
    }

    protected virtual async Task SendWhatsAppCompletionNotificationsAsync(
        InterviewSession session,
        Customer customer,
        Vendor vendor,
        string interviewName,
        string applicantName,
        string applicantCompletionTimestamp,
        string vendorCompletionTimestamp,
        string reportUrl,
        string candidateDashboardUrl)
    {
        var whatsAppService = ResolveWhatsAppService();
        if (whatsAppService == null)
            return;

        var commonTokens = new Dictionary<string, string>
        {
            ["InterviewName"] = interviewName ?? string.Empty,
            ["ApplicantName"] = applicantName ?? string.Empty,
            ["CompletionTimestamp"] = applicantCompletionTimestamp ?? string.Empty,
            ["ReportUrl"] = reportUrl ?? string.Empty,
            ["CandidateDashboardUrl"] = candidateDashboardUrl ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(customer.Phone))
        {
            await TrySendWhatsAppNotificationAsync(
                whatsAppService,
                new AIInterviewWhatsAppNotificationRequest
                {
                    CustomerId = customer.Id,
                    PhoneNumber = customer.Phone,
                    MessageType = "AIInterview.ApplicantCompletion",
                    MessageBody = $"Hi {applicantName}, your {interviewName} interview was completed on {applicantCompletionTimestamp}. Report: {reportUrl} Dashboard: {candidateDashboardUrl}",
                    TemplateParameters = new List<string>
                    {
                        interviewName ?? string.Empty,
                        applicantName ?? string.Empty,
                        applicantCompletionTimestamp ?? string.Empty,
                        reportUrl ?? string.Empty,
                        candidateDashboardUrl ?? string.Empty
                    },
                    Tokens = new Dictionary<string, string>(commonTokens)
                },
                session.Id,
                "applicant completion");
        }

        if (vendor != null)
        {
            try
            {
                var vendorAddress = _addressService != null && vendor.AddressId > 0
                    ? await _addressService.GetAddressByIdAsync(vendor.AddressId)
                    : null;
                var vendorCustomer = vendor.PmCustomerId.HasValue
                    ? await _customerService.GetCustomerByIdAsync(vendor.PmCustomerId.Value)
                    : null;
                var vendorPhone = vendorCustomer?.Phone ?? vendorAddress?.PhoneNumber;
                if (!string.IsNullOrWhiteSpace(vendorPhone))
                {
                    var vendorTokens = new Dictionary<string, string>(commonTokens)
                    {
                        ["CompletionTimestamp"] = vendorCompletionTimestamp ?? string.Empty
                    };
                    await TrySendWhatsAppNotificationAsync(
                        whatsAppService,
                        new AIInterviewWhatsAppNotificationRequest
                        {
                            CustomerId = vendorCustomer?.Id ?? 0,
                            PhoneNumber = vendorPhone,
                            MessageType = "AIInterview.VendorCompletion",
                            MessageBody = $"{applicantName} completed {interviewName} on {vendorCompletionTimestamp}. Report: {reportUrl} Candidate dashboard: {candidateDashboardUrl}",
                            TemplateParameters = new List<string>
                            {
                                interviewName ?? string.Empty,
                                applicantName ?? string.Empty,
                                vendorCompletionTimestamp ?? string.Empty,
                                reportUrl ?? string.Empty,
                                candidateDashboardUrl ?? string.Empty
                            },
                            Tokens = vendorTokens
                        },
                        session.Id,
                        "vendor completion");
                }
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "Optional WhatsApp vendor recipient resolution failed for session {SessionId}.", session.Id);
            }
        }
    }

    protected virtual IOptionalWhatsAppNotificationService ResolveWhatsAppService()
    {
        try
        {
            var service = OptionalWhatsAppNotificationServiceResolver.Resolve(_serviceProvider);
            return service?.IsEnabled == true ? service : null;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Optional WhatsApp provider could not be resolved for an AIInterview completion notification.");
            return null;
        }
    }

    protected virtual async Task TrySendWhatsAppNotificationAsync(
        IOptionalWhatsAppNotificationService whatsAppService,
        AIInterviewWhatsAppNotificationRequest request,
        int sessionId,
        string notificationKind)
    {
        try
        {
            if (whatsAppService?.IsEnabled != true)
                return;

            if (!await whatsAppService.SendNotificationAsync(request))
                _logger?.LogWarning("Optional WhatsApp {NotificationKind} notification was not accepted for session {SessionId}.", notificationKind, sessionId);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Optional WhatsApp {NotificationKind} notification failed for session {SessionId}.", notificationKind, sessionId);
        }
    }

    public async Task SendRuntimeFeedbackSubmittedAdminNotificationAsync(InterviewSession session, int languageId)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.CandidateFeedbackIssue))
            return;

        var store = await _storeContext.GetCurrentStoreAsync();
        var storeId = store?.Id ?? 0;
        var effectiveLanguageId = languageId > 0 ? languageId : store?.DefaultLanguageId ?? 0;
        var templates = await _messageTemplateService.GetMessageTemplatesByNameAsync(RuntimeFeedbackAdminNotificationTemplateName, storeId);
        var template = templates?.FirstOrDefault();
        if (template == null || !template.IsActive)
            return;

        var emailAccountId = template.EmailAccountId > 0 ? template.EmailAccountId : _emailAccountSettings.DefaultEmailAccountId;
        var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(emailAccountId);
        emailAccount ??= (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();
        if (emailAccount == null || string.IsNullOrWhiteSpace(emailAccount.Email))
            return;

        var customer = session.CustomerId > 0 ? await _customerService.GetCustomerByIdAsync(session.CustomerId) : null;
        var jobTitle = await ResolveSessionJobTitleAsync(session);
        var storeLocation = (_webHelper.GetStoreLocation() ?? string.Empty).TrimEnd('/');
        var submittedOn = session.CandidateFeedbackSubmittedOnUtc.HasValue
            ? _dateTimeHelper.ConvertToUserTime(session.CandidateFeedbackSubmittedOnUtc.Value, TimeZoneInfo.Utc, _dateTimeHelper.DefaultStoreTimeZone).ToString("g", CultureInfo.InvariantCulture)
            : string.Empty;

        var tokens = new List<Nop.Services.Messages.Token>();
        if (customer != null)
            await _messageTokenProvider.AddCustomerTokensAsync(tokens, customer);
        else
        {
            tokens.Add(new Nop.Services.Messages.Token("Customer.FullName", string.Empty));
            tokens.Add(new Nop.Services.Messages.Token("Customer.Email", string.Empty));
        }

        tokens.Add(new Nop.Services.Messages.Token("AIInterview.SessionId", session.Id.ToString(CultureInfo.InvariantCulture)));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.JobTitle", jobTitle ?? string.Empty));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.FeedbackIssue", session.CandidateFeedbackIssue ?? string.Empty));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.FeedbackHelpfulness", session.CandidateFeedbackHelpfulness ?? string.Empty));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.FeedbackComment", session.CandidateFeedbackComment ?? string.Empty));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.FeedbackSubmittedOn", submittedOn));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.FeedbackHasAttachment", session.CandidateFeedbackAttachmentDownloadId > 0 ? "Yes" : "No"));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.FeedbackReportsUrl", $"{storeLocation}/Admin/AIInterview/FeedbackReports"));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.CandidateDetailsUrl", $"{storeLocation}/Admin/AIInterviewAdmin/CandidateDetails?sessionId={session.Id}"));

        await _workflowMessageService.SendNotificationAsync(
            template,
            emailAccount,
            effectiveLanguageId,
            tokens,
            emailAccount.Email,
            emailAccount.DisplayName,
            ignoreDelayBeforeSend: true);
    }

    public async Task InsertInterviewSessionAsync(InterviewSession session)
    {
        await _sessionRepository.InsertAsync(session);
    }

    public async Task<InterviewSession> GetInterviewSessionByIdAsync(int sessionId)
    {
        return await _sessionRepository.GetByIdAsync(sessionId);
    }

    public async Task<InterviewSession> GetLatestCompletedSessionByCustomerIdAndProductIdAsync(int customerId, int productId)
    {
        var legacyApplicationIds = (await _applicationService.GetJobApplicationsByCustomerIdAsync(customerId))
            .Where(application => application.ProductId == productId)
            .Select(application => application.Id)
            .ToList();

        return (await _sessionRepository.GetAllAsync(query => query
            .Where(s => s.CustomerId == customerId &&
                !s.Deleted &&
                (s.ProductId == productId || (s.ProductId == 0 && legacyApplicationIds.Contains(s.JobApplicationId))) &&
                s.CompletedOnUtc.HasValue)
            .OrderByDescending(s => s.CompletedOnUtc)))
            .FirstOrDefault();
    }

    public async Task<decimal> GetHighestScoreByCustomerIdAndProductIdAsync(int customerId, int productId)
    {
        var legacyApplicationIds = (await _applicationService.GetJobApplicationsByCustomerIdAsync(customerId))
            .Where(application => application.ProductId == productId)
            .Select(application => application.Id)
            .ToList();
        var sessions = await _sessionRepository.Table
            .Where(s => s.CustomerId == customerId &&
                !s.Deleted &&
                (s.ProductId == productId || (s.ProductId == 0 && legacyApplicationIds.Contains(s.JobApplicationId))) &&
                s.CompletedOnUtc.HasValue)
            .ToListAsync();
        if (!sessions.Any())
            return 0;

        return sessions.Max(s => s.Score);
    }

    public async Task<int> GetSponsorInviteAttemptCountAsync(int inviteId)
    {
        if (inviteId <= 0)
            return 0;

        return await _sessionRepository.Table.CountAsync(session => session.SponsorInviteId == inviteId);
    }

    public async Task<InterviewSession> GetSessionBySessionKeyAsync(string sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey))
            return null;

        return (await _sessionRepository.GetAllAsync(query => query.Where(s => !s.Deleted && s.SessionKey == sessionKey))).FirstOrDefault();
    }

    public async Task<InterviewSession> GetSessionByTokenAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        return (await _sessionRepository.GetAllAsync(query => query.Where(s => !s.Deleted && s.Token == token))).FirstOrDefault();
    }

    public async Task<InterviewSession> GetSessionByRecordingShareTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return (await _sessionRepository.GetAllAsync(query => query.Where(session =>
            !session.Deleted &&
            session.RecordingShareEnabled &&
            session.RecordingShareToken == token))).FirstOrDefault();
    }

    public async Task<InterviewSession> GetSessionByReportShareTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return (await _sessionRepository.GetAllAsync(query => query.Where(session =>
            !session.Deleted &&
            session.ReportShareEnabled &&
            session.ReportShareToken == token))).FirstOrDefault();
    }

    public async Task<IList<InterviewSession>> GetSessionsByCustomerIdAsync(int customerId)
    {
        return await _sessionRepository.GetAllAsync(query => query
            .Where(s => s.CustomerId == customerId && !s.Deleted)
            .OrderByDescending(s => s.CreatedOnUtc));
    }

    public async Task<IList<InterviewSession>> GetReusableCompletedSessionsAsync(int customerId, int windowDays = AIInterviewDefaults.InterviewReuseWindowDays)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-windowDays);
        return await _sessionRepository.GetAllAsync(query => query
            .Where(s => s.CustomerId == customerId &&
                !s.Deleted &&
                s.CompletedOnUtc.HasValue &&
                s.CompletedOnUtc.Value >= cutoffUtc)
            .OrderByDescending(s => s.CompletedOnUtc));
    }

    public async Task<IList<InterviewSession>> GetPreviousResumeSourceSessionsAsync(int customerId)
    {
        return await _sessionRepository.GetAllAsync(query =>
        {
            var eligibleSessions = query.Where(session =>
                session.CustomerId == customerId &&
                !session.Deleted &&
                session.ResumeDownloadId > 0);

            // Keep only the newest row for each resume without materializing customer history.
            return eligibleSessions
                .Where(session => !eligibleSessions.Any(newerSession =>
                    newerSession.ResumeDownloadId == session.ResumeDownloadId &&
                    (newerSession.CreatedOnUtc > session.CreatedOnUtc ||
                     (newerSession.CreatedOnUtc == session.CreatedOnUtc && newerSession.Id > session.Id))))
                .OrderByDescending(session => session.CreatedOnUtc)
                .ThenByDescending(session => session.Id)
                .Select(session => new InterviewSession
                {
                    Id = session.Id,
                    ResumeDownloadId = session.ResumeDownloadId,
                    CreatedOnUtc = session.CreatedOnUtc
                })
                .Take(3);
        });
    }

    public async Task<IList<PreviousResumeSource>> GetPreviousResumeSourcesAsync(int customerId)
    {
        var applications = await _applicationService.GetPreviousResumeSourceApplicationsAsync(customerId) ?? new List<JobApplication>();
        var sessions = await GetPreviousResumeSourceSessionsAsync(customerId) ?? new List<InterviewSession>();

        return applications
            .Select(application => new PreviousResumeSource
            {
                SourceId = application.Id,
                ResumeDownloadId = application.ResumeDownloadId,
                CreatedOnUtc = application.CreatedOnUtc,
                DefaultLabel = "Application resume"
            })
            .Concat(sessions.Select(session => new PreviousResumeSource
            {
                SourceId = session.Id,
                ResumeDownloadId = session.ResumeDownloadId,
                CreatedOnUtc = session.CreatedOnUtc,
                DefaultLabel = "Practice resume"
            }))
            .OrderByDescending(source => source.CreatedOnUtc)
            .ThenByDescending(source => source.SourceId)
            .ThenBy(source => source.DefaultLabel, StringComparer.Ordinal)
            .ThenByDescending(source => source.ResumeDownloadId)
            .GroupBy(source => source.ResumeDownloadId)
            .Select(group => group.First())
            .Take(3)
            .ToList();
    }

    public async Task<IList<HomeTopPerformer>> GetHomepageTopPerformersAsync(int storeId, int maxCount = AIInterviewDefaults.HomepageTopPerformersMaxCount)
    {
        maxCount = Math.Clamp(maxCount, 1, AIInterviewDefaults.HomepageTopPerformersMaxCount);

        if (_applicationRepository == null ||
            _customerRepository == null ||
            _productRepository == null ||
            _storeMappingRepository == null)
        {
            return new List<HomeTopPerformer>();
        }

        var utcNow = DateTime.UtcNow;
        var completedFromUtc = utcNow.AddDays(-AIInterviewDefaults.HomepageTopPerformersFreshnessDays);
        var productEntityName = nameof(Product);

        var eligibleQuery =
            from session in _sessionRepository.Table
            join customer in _customerRepository.Table on session.CustomerId equals customer.Id
            join applicationJoin in _applicationRepository.Table on session.JobApplicationId equals applicationJoin.Id into applicationGroup
            from application in applicationGroup.DefaultIfEmpty()
            let productId = session.ProductId > 0
                ? session.ProductId
                : session.SourceProductId > 0
                    ? session.SourceProductId
                    : application != null ? application.ProductId : 0
            join product in _productRepository.Table on productId equals product.Id
            where session.CompletedOnUtc.HasValue &&
                !session.Deleted &&
                session.CompletedOnUtc.Value >= completedFromUtc &&
                session.CompletedOnUtc.Value <= utcNow &&
                customer.Active &&
                !customer.Deleted &&
                product.Published &&
                !product.Deleted &&
                product.VisibleIndividually &&
                (!product.AvailableStartDateTimeUtc.HasValue || product.AvailableStartDateTimeUtc.Value <= utcNow) &&
                (!product.AvailableEndDateTimeUtc.HasValue || product.AvailableEndDateTimeUtc.Value >= utcNow) &&
                (storeId <= 0 ||
                    !product.LimitedToStores ||
                    _storeMappingRepository.Table.Any(storeMapping =>
                        storeMapping.EntityName == productEntityName &&
                        storeMapping.EntityId == product.Id &&
                        storeMapping.StoreId == storeId))
            select new
            {
                SessionId = session.Id,
                session.CustomerId,
                session.Score,
                CompletedOnUtc = session.CompletedOnUtc.Value,
                customer.FirstName,
                customer.LastName,
                ResumeProfileJson = session.ResumeProfileJson != null && session.ResumeProfileJson != string.Empty
                    ? session.ResumeProfileJson
                    : application != null ? application.ResumeProfileJson : null
            };

        var bestScoreQuery = eligibleQuery
            .GroupBy(candidate => candidate.CustomerId)
            .Select(groupedRows => new
            {
                CustomerId = groupedRows.Key,
                Score = groupedRows.Max(candidate => candidate.Score)
            });

        var bestCompletionQuery =
            from candidate in eligibleQuery
            join bestScore in bestScoreQuery on new { candidate.CustomerId, candidate.Score } equals new { bestScore.CustomerId, bestScore.Score }
            group candidate by candidate.CustomerId into groupedRows
            select new
            {
                CustomerId = groupedRows.Key,
                CompletedOnUtc = groupedRows.Max(candidate => candidate.CompletedOnUtc)
            };

        var bestSessionQuery =
            from candidate in eligibleQuery
            join bestScore in bestScoreQuery on new { candidate.CustomerId, candidate.Score } equals new { bestScore.CustomerId, bestScore.Score }
            join bestCompletion in bestCompletionQuery on new { candidate.CustomerId, candidate.CompletedOnUtc } equals new { bestCompletion.CustomerId, bestCompletion.CompletedOnUtc }
            group candidate by candidate.CustomerId into groupedRows
            select new
            {
                CustomerId = groupedRows.Key,
                SessionId = groupedRows.Max(candidate => candidate.SessionId)
            };

        var winnersQuery =
            (from candidate in eligibleQuery
             join bestSession in bestSessionQuery on new { candidate.CustomerId, candidate.SessionId } equals new { bestSession.CustomerId, bestSession.SessionId }
             orderby candidate.Score descending, candidate.CompletedOnUtc descending, candidate.SessionId descending
             select candidate);

        var rows = await winnersQuery.ToListAsync();
        var rankedRows = rows
            .Select(row => new
            {
                Row = row,
                PrimarySkillText = ResolvePrimarySkillText(row.ResumeProfileJson)
            })
            .Where(item => HasSpecifiedPrimarySkill(item.PrimarySkillText))
            .OrderByDescending(item => item.Row.Score)
            .ThenByDescending(item => item.Row.CompletedOnUtc)
            .ThenByDescending(item => item.Row.SessionId)
            .Take(maxCount)
            .ToList();

        var avatarUrls = await ResolveAvatarUrlsAsync(rankedRows.Select(item => item.Row.CustomerId));
        var unknownCandidateText = _localizationService == null
            ? null
            : await _localizationService.GetResourceAsync(AIInterviewDefaults.HomepageTopPerformersUnknownCandidateResourceKey);
        if (string.IsNullOrWhiteSpace(unknownCandidateText))
            unknownCandidateText = "Unknown candidate";

        return rankedRows.Select(item =>
        {
            var row = item.Row;
            var fullName = $"{row.FirstName} {row.LastName}".Trim();
            avatarUrls.TryGetValue(row.CustomerId, out var avatarUrl);

            return new HomeTopPerformer
            {
                ImageUrl = string.IsNullOrWhiteSpace(avatarUrl) ? AIInterviewDefaults.DefaultAvatarImageUrl : avatarUrl,
                FullName = string.IsNullOrWhiteSpace(fullName) ? unknownCandidateText : fullName,
                PrimarySkillText = item.PrimarySkillText,
                Score = row.Score,
                ProfileLink = null,
                CustomerId = row.CustomerId,
                InterviewSessionId = row.SessionId,
                CompletedOnUtc = row.CompletedOnUtc
            };
        }).ToList();
    }

    protected static bool HasSpecifiedPrimarySkill(string primarySkillText)
    {
        return !string.IsNullOrWhiteSpace(primarySkillText) &&
            !string.Equals(primarySkillText.Trim(), "Not specified", StringComparison.OrdinalIgnoreCase);
    }

    protected virtual async Task<IDictionary<int, string>> ResolveAvatarUrlsAsync(IEnumerable<int> customerIds)
    {
        var distinctCustomerIds = customerIds?.Where(id => id > 0).Distinct().ToArray() ?? Array.Empty<int>();
        if (distinctCustomerIds.Length == 0 ||
            _genericAttributeRepository == null ||
            _pictureService == null)
        {
            return new Dictionary<int, string>();
        }

        var avatarAttributes = await _genericAttributeRepository.GetAllAsync(query => query
            .Where(attribute =>
                distinctCustomerIds.Contains(attribute.EntityId) &&
                attribute.KeyGroup == nameof(Customer) &&
                attribute.Key == NopCustomerDefaults.AvatarPictureIdAttribute &&
                attribute.StoreId == 0));
        var avatarPictureIdsByCustomer = avatarAttributes
            .Select(attribute => new
            {
                attribute.EntityId,
                PictureId = int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pictureId) ? pictureId : 0
            })
            .Where(attribute => attribute.PictureId > 0)
            .GroupBy(attribute => attribute.EntityId)
            .ToDictionary(group => group.Key, group => group.First().PictureId);

        var avatarUrls = new Dictionary<int, string>();
        foreach (var (customerId, pictureId) in avatarPictureIdsByCustomer)
        {
            var imageUrl = await _pictureService.GetPictureUrlAsync(pictureId, 128, false, defaultPictureType: PictureType.Avatar);
            if (!string.IsNullOrWhiteSpace(imageUrl))
                avatarUrls[customerId] = imageUrl;
        }

        return avatarUrls;
    }

    protected virtual string ResolvePrimarySkillText(string resumeProfileJson)
    {
        if (string.IsNullOrWhiteSpace(resumeProfileJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(resumeProfileJson);
            var root = document.RootElement;
            return GetFirstJsonString(root, "primarySkills", "PrimarySkills") ??
                GetJsonString(root, "primarySkill", "PrimarySkill") ??
                GetFirstJsonString(root, "skills", "Skills") ??
                GetJsonString(root, "skill", "Skill");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected static string GetFirstJsonString(JsonElement root, params string[] propertyNames)
    {
        if (!TryGetJsonProperty(root, out var property, propertyNames))
            return null;

        if (property.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;

                var value = item.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString()?.Trim() : null;
    }

    protected static string GetJsonString(JsonElement root, params string[] propertyNames)
    {
        if (!TryGetJsonProperty(root, out var property, propertyNames) || property.ValueKind != JsonValueKind.String)
            return null;

        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    protected static bool TryGetJsonProperty(JsonElement root, out JsonElement property, params string[] propertyNames)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out property))
                return true;
        }

        foreach (var item in root.EnumerateObject())
        {
            if (propertyNames.Any(propertyName => string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase)))
            {
                property = item.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    public async Task<IList<InterviewSession>> GetCompletionWorkSessionsAsync(DateTime staleProcessingBeforeUtc, int maxCount = 20)
    {
        maxCount = Math.Clamp(maxCount, 1, 100);
        var utcNow = DateTime.UtcNow;

        return await _sessionRepository.GetAllAsync(query => query
            .Where(session =>
                !session.Deleted &&
                (session.CompletionState == InterviewCompletionStates.Queued &&
                    (!session.CompletionNextAttemptOnUtc.HasValue ||
                     session.CompletionNextAttemptOnUtc <= utcNow)) ||
                (session.CompletionState == InterviewCompletionStates.Processing &&
                    (!session.CompletionProcessingStartedOnUtc.HasValue ||
                     session.CompletionProcessingStartedOnUtc <= staleProcessingBeforeUtc)) ||
                (session.CompletionState == InterviewCompletionStates.Ready &&
                    !session.CompletionPublishedOnUtc.HasValue &&
                    session.CompletedOnUtc.HasValue &&
                    session.ReportData != null &&
                    session.ReportData != string.Empty) ||
                ((session.CompletionState == null || session.CompletionState == string.Empty) &&
                    !session.IsActive &&
                    !session.CompletedOnUtc.HasValue &&
                    (session.ReportData == null || session.ReportData == string.Empty)))
            .OrderBy(session => session.CompletionQueuedOnUtc ?? session.CreatedOnUtc)
            .ThenBy(session => session.Id)
            .Take(maxCount));
    }

    public async Task<string> EnsureRecordingShareTokenAsync(InterviewSession session)
    {
        if (session == null || session.Deleted || string.IsNullOrWhiteSpace(session.RecordingUrl))
            return null;

        if (!string.IsNullOrWhiteSpace(session.RecordingShareToken) && session.RecordingShareEnabled)
            return session.RecordingShareToken;

        string token;
        do
        {
            token = GenerateRecordingShareToken();
        } while (await _sessionRepository.Table.AnyAsync(existing => existing.Id != session.Id && existing.RecordingShareToken == token));

        session.RecordingShareToken = token;
        session.RecordingShareEnabled = true;
        session.RecordingShareCreatedOnUtc ??= DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session);
        return session.RecordingShareToken;
    }

    public async Task<string> EnsureReportShareTokenAsync(InterviewSession session)
    {
        if (session == null || session.Deleted ||
            (string.IsNullOrWhiteSpace(session.ReportData) && string.IsNullOrWhiteSpace(session.RecordingUrl)))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(session.ReportShareToken) && session.ReportShareEnabled)
            return session.ReportShareToken;

        string token;
        do
        {
            token = GenerateRecordingShareToken();
        } while (await _sessionRepository.Table.AnyAsync(existing => existing.Id != session.Id && existing.ReportShareToken == token));

        session.ReportShareToken = token;
        session.ReportShareEnabled = true;
        session.ReportShareCreatedOnUtc ??= DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session);
        return session.ReportShareToken;
    }

    public async Task UpdateInterviewSessionAsync(InterviewSession session)
    {
        await _sessionRepository.UpdateAsync(session);
    }

    public async Task<bool> SoftDeleteInterviewSessionAsync(int sessionId, int customerId)
    {
        if (sessionId <= 0 || customerId <= 0)
            return false;

        var session = await _sessionRepository.GetByIdAsync(sessionId);
        if (session == null || session.Deleted || session.CustomerId != customerId || !session.CompletedOnUtc.HasValue)
            return false;

        session.Deleted = true;
        session.IsActive = false;
        session.RecordingShareEnabled = false;
        session.ReportShareEnabled = false;
        await _sessionRepository.UpdateAsync(session);
        return true;
    }

    public async Task<bool> CanAccessReportAsync(int customerId, int sessionId)
    {
        var session = await GetInterviewSessionByIdAsync(sessionId);
        if (session == null)
        {
            LogCanAccessReportResult(false, "session not found", customerId, sessionId, null, null);
            return false;
        }

        if (session.Deleted)
        {
            LogCanAccessReportResult(false, "session deleted", customerId, sessionId, session, null);
            return false;
        }

        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        if (customer == null)
        {
            LogCanAccessReportResult(false, "customer not found", customerId, sessionId, session, null);
            return false;
        }

        if (session.CustomerId == customerId)
        {
            LogCanAccessReportResult(true, "session owner", customerId, sessionId, session, customer);
            return true;
        }

        if (_genericAttributeRepository != null)
        {
            var sessionOwner = await _customerService.GetCustomerByIdAsync(session.CustomerId);
            if (sessionOwner != null)
            {
                var attr = (await _genericAttributeRepository.GetAllAsync(q =>
                    q.Where(a => a.KeyGroup == nameof(Customer) &&
                                 a.Key == AIInterviewDefaults.InstituteVendorIdAttributeKey &&
                                 a.EntityId == sessionOwner.Id))).FirstOrDefault();

                if (attr != null &&
                    int.TryParse(attr.Value, out var attrVendorId) &&
                    attrVendorId > 0)
                {
                    var viewer = await _customerService.GetCustomerByIdAsync(customerId);
                    if (viewer != null && viewer.VendorId == attrVendorId)
                        return true;
                }
            }
        }

        if (await _customerService.IsAdminAsync(customer))
        {
            LogCanAccessReportResult(true, "admin", customerId, sessionId, session, customer);
            return true;
        }

        if (customer.VendorId > 0)
        {
            if (session.ProductId > 0)
            {
                var product = await _productService.GetProductByIdAsync(session.ProductId);
                if (product != null && product.VendorId == customer.VendorId)
                {
                    LogCanAccessReportResult(true, "vendor owns session product", customerId, sessionId, session, customer);
                    return true;
                }

                _logger?.LogInformation("AI Interview report access vendor product check did not match. CustomerId={CustomerId}; SessionId={SessionId}; CustomerVendorId={CustomerVendorId}; SessionProductId={SessionProductId}; ProductFound={ProductFound}; ProductVendorId={ProductVendorId}.",
                    customerId,
                    sessionId,
                    customer.VendorId,
                    session.ProductId,
                    product != null,
                    product?.VendorId ?? 0);
            }

            if (session.JobApplicationId > 0)
            {
                var application = await _applicationService.GetJobApplicationByIdAsync(session.JobApplicationId);
                if (application != null)
                {
                    var product = await _productService.GetProductByIdAsync(application.ProductId);
                    if (product != null && product.VendorId == customer.VendorId)
                    {
                        LogCanAccessReportResult(true, "vendor owns application product", customerId, sessionId, session, customer);
                        return true;
                    }

                    _logger?.LogInformation("AI Interview report access vendor application check did not match. CustomerId={CustomerId}; SessionId={SessionId}; CustomerVendorId={CustomerVendorId}; JobApplicationId={JobApplicationId}; ApplicationProductId={ApplicationProductId}; ProductFound={ProductFound}; ProductVendorId={ProductVendorId}.",
                        customerId,
                        sessionId,
                        customer.VendorId,
                        session.JobApplicationId,
                        application.ProductId,
                        product != null,
                        product?.VendorId ?? 0);
                }
                else
                {
                    _logger?.LogInformation("AI Interview report access vendor application check could not load application. CustomerId={CustomerId}; SessionId={SessionId}; CustomerVendorId={CustomerVendorId}; JobApplicationId={JobApplicationId}.",
                        customerId,
                        sessionId,
                        customer.VendorId,
                        session.JobApplicationId);
                }
            }
        }

        LogCanAccessReportResult(false, "no matching owner, admin, or vendor rule", customerId, sessionId, session, customer);
        return false;
    }

    protected virtual void LogCanAccessReportResult(bool canAccess, string reason, int customerId, int sessionId, InterviewSession session, Customer customer)
    {
        var logLevel = canAccess ? LogLevel.Information : LogLevel.Warning;
        _logger?.Log(logLevel,
            "AI Interview report access check completed. CanAccess={CanAccess}; Reason={Reason}; CustomerId={CustomerId}; SessionId={SessionId}; SessionFound={SessionFound}; SessionCustomerId={SessionCustomerId}; ProductId={ProductId}; JobApplicationId={JobApplicationId}; CustomerFound={CustomerFound}; CustomerVendorId={CustomerVendorId}.",
            canAccess,
            reason,
            customerId,
            sessionId,
            session != null,
            session?.CustomerId ?? 0,
            session?.ProductId ?? 0,
            session?.JobApplicationId ?? 0,
            customer != null,
            customer?.VendorId ?? 0);
    }

    protected virtual async Task<string> ResolveSessionJobTitleAsync(InterviewSession session)
    {
        if (session == null)
            return string.Empty;

        if (session.ProductId > 0)
        {
            var product = await _productService.GetProductByIdAsync(session.ProductId);
            if (!string.IsNullOrWhiteSpace(product?.Name))
                return product.Name;
        }

        if (session.JobApplicationId > 0)
        {
            var application = await _applicationService.GetJobApplicationByIdAsync(session.JobApplicationId);
            if (!string.IsNullOrWhiteSpace(application?.JobTitle))
                return application.JobTitle;

            if (application?.ProductId > 0)
            {
                var product = await _productService.GetProductByIdAsync(application.ProductId);
                if (!string.IsNullOrWhiteSpace(product?.Name))
                    return product.Name;
            }
        }

        return session.InterviewType == AIInterviewDefaults.InterviewTypeMockPractice
            ? "Practice Interview"
            : string.Empty;
    }

    protected static string GenerateRecordingShareToken()
    {
        var tokenBytes = new byte[32];
        RandomNumberGenerator.Fill(tokenBytes);
        return Convert.ToBase64String(tokenBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public class CreditService : ICreditService
{
    private readonly IRepository<CreditWallet> _walletRepository;
    private readonly IRepository<CreditLedgerEntry> _ledgerRepository;

    public CreditService(IRepository<CreditWallet> walletRepository, IRepository<CreditLedgerEntry> ledgerRepository)
    {
        _walletRepository = walletRepository;
        _ledgerRepository = ledgerRepository;
    }

    public async Task<CreditWallet> GetOrCreateWalletAsync(int customerId)
    {
        var wallets = (await _walletRepository.GetAllAsync(query => query.Where(w => w.CustomerId == customerId)))
            .OrderBy(wallet => wallet.Id)
            .ToList();
        var wallet = wallets.FirstOrDefault();
        if (wallet == null)
        {
            wallet = new CreditWallet { CustomerId = customerId, Balance = 0 };
            await _walletRepository.InsertAsync(wallet);
        }
        return wallet;
    }

    public async Task AddCreditAsync(int customerId, decimal amount, string remarks)
    {
        await AddCreditAsync(customerId, amount, remarks, null);
    }

    public async Task AddCreditAsync(int customerId, decimal amount, string remarks, string ledgerSource, int productId = 0, int orderId = 0)
    {
        var wallet = await GetOrCreateWalletAsync(customerId);
        wallet.Balance += amount;
        await _walletRepository.UpdateAsync(wallet);

        await _ledgerRepository.InsertAsync(new CreditLedgerEntry
        {
            CreditWalletId = wallet.Id,
            Amount = amount,
            TransactionType = "Deposit",
            LedgerSource = ledgerSource,
            ProductId = productId,
            OrderId = orderId,
            Remarks = remarks,
            CreatedOnUtc = DateTime.UtcNow
        });
    }

    public async Task<bool> AuthorizeAndChargeAsync(int customerId, decimal amount, string remarks)
    {
        return await AuthorizeAndChargeAsync(customerId, amount, remarks, null);
    }

    public async Task<bool> AuthorizeAndChargeAsync(int customerId, decimal amount, string remarks, string ledgerSource, int productId = 0, int sponsorInviteId = 0)
    {
        var wallet = await GetOrCreateWalletAsync(customerId);
        if (wallet.Balance < amount)
            return false;

        wallet.Balance -= amount;
        await _walletRepository.UpdateAsync(wallet);

        await _ledgerRepository.InsertAsync(new CreditLedgerEntry
        {
            CreditWalletId = wallet.Id,
            Amount = -amount,
            TransactionType = "Withdrawal",
            LedgerSource = ledgerSource,
            ProductId = productId,
            SponsorInviteId = sponsorInviteId,
            Remarks = remarks,
            CreatedOnUtc = DateTime.UtcNow
        });

        return true;
    }
}

public class InterviewStartCreditService : IInterviewStartCreditService
{
    public const decimal InterviewStartCost = 1m;
    public const string RefundNotificationTemplateName = "AIInterview.InterviewStartCreditRefunded";
    public const int RefundNotificationMaxAttempts = 3;
    private static readonly TimeSpan RefundNotificationProcessingLease = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<int, SessionCreditLock> SessionCreditLocks = new();

    private sealed class SessionCreditLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class SessionCreditLockLease : IDisposable
    {
        private readonly int _sessionId;
        private readonly SessionCreditLock _sessionLock;
        private bool _disposed;

        public SessionCreditLockLease(int sessionId, SessionCreditLock sessionLock)
        {
            _sessionId = sessionId;
            _sessionLock = sessionLock;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _sessionLock.Semaphore.Release();
            lock (_sessionLock)
            {
                _sessionLock.ReferenceCount--;
                if (_sessionLock.ReferenceCount == 0)
                    SessionCreditLocks.TryRemove(new KeyValuePair<int, SessionCreditLock>(_sessionId, _sessionLock));
            }
        }
    }

    private readonly IRepository<CreditWallet> _walletRepository;
    private readonly IRepository<CreditLedgerEntry> _ledgerRepository;
    private readonly IRepository<InterviewSession> _sessionRepository;
    private readonly IRepository<SponsorInvite> _inviteRepository;
    private readonly INopDataProvider _dataProvider;
    private readonly ILogger<InterviewStartCreditService> _logger;
    private readonly ICustomerService _customerService;
    private readonly Nop.Services.Catalog.IProductService _productService;
    private readonly Nop.Services.Messages.IWorkflowMessageService _workflowMessageService;
    private readonly Nop.Services.Messages.IMessageTemplateService _messageTemplateService;
    private readonly Nop.Services.Messages.IEmailAccountService _emailAccountService;
    private readonly EmailAccountSettings _emailAccountSettings;
    private readonly IStoreContext _storeContext;

    public InterviewStartCreditService(
        IRepository<CreditWallet> walletRepository,
        IRepository<CreditLedgerEntry> ledgerRepository,
        IRepository<InterviewSession> sessionRepository,
        IRepository<SponsorInvite> inviteRepository,
        INopDataProvider dataProvider,
        ILogger<InterviewStartCreditService> logger,
        ICustomerService customerService,
        Nop.Services.Catalog.IProductService productService,
        Nop.Services.Messages.IWorkflowMessageService workflowMessageService,
        Nop.Services.Messages.IMessageTemplateService messageTemplateService,
        Nop.Services.Messages.IEmailAccountService emailAccountService,
        EmailAccountSettings emailAccountSettings,
        IStoreContext storeContext)
    {
        _walletRepository = walletRepository;
        _ledgerRepository = ledgerRepository;
        _sessionRepository = sessionRepository;
        _inviteRepository = inviteRepository;
        _dataProvider = dataProvider;
        _logger = logger;
        _customerService = customerService;
        _productService = productService;
        _workflowMessageService = workflowMessageService;
        _messageTemplateService = messageTemplateService;
        _emailAccountService = emailAccountService;
        _emailAccountSettings = emailAccountSettings;
        _storeContext = storeContext;
    }

    public async Task<InterviewCreditEligibilityResult> CheckEligibilityAsync(InterviewSession session)
    {
        if (session == null || session.Id <= 0)
            return new InterviewCreditEligibilityResult { Eligible = false };

        var persistedSession = await _sessionRepository.GetByIdAsync(session.Id) ?? session;
        if (HasExistingCharge(persistedSession))
            return BuildEligibility(persistedSession, true, true);

        var chargeContext = await ResolveChargeContextAsync(persistedSession);
        if (chargeContext.CustomerId <= 0)
            return BuildEligibility(persistedSession, false, false, chargeContext);

        var wallet = await GetWalletAsync(chargeContext.CustomerId);
        return BuildEligibility(persistedSession, wallet?.Balance >= InterviewStartCost, false, chargeContext);
    }

    public async Task<InterviewCreditChargeResult> ChargeAsync(InterviewSession session)
    {
        if (session == null || session.Id <= 0)
            return new InterviewCreditChargeResult { Eligible = false };

        using var sessionLock = await AcquireSessionCreditLockAsync(session.Id);
        try
        {
            using var transaction = _dataProvider.CreateTransactionScope();
            var persistedSession = await _sessionRepository.GetByIdAsync(session.Id) ?? session;
            if (HasExistingCharge(persistedSession))
            {
                transaction.Complete();
                return BuildChargeResult(persistedSession, true, true, false);
            }

            var chargeContext = await ResolveChargeContextAsync(persistedSession);
            if (chargeContext.CustomerId <= 0)
                return BuildChargeResult(persistedSession, false, false, false, chargeContext);

            var wallet = await GetWalletAsync(chargeContext.CustomerId);
            if (wallet == null || wallet.Balance < InterviewStartCost)
                return BuildChargeResult(persistedSession, false, false, false, chargeContext);

            wallet.Balance -= InterviewStartCost;
            await _walletRepository.UpdateAsync(wallet);

            var chargedOnUtc = DateTime.UtcNow;
            await _ledgerRepository.InsertAsync(new CreditLedgerEntry
            {
                CreditWalletId = wallet.Id,
                Amount = -InterviewStartCost,
                TransactionType = "Withdrawal",
                LedgerSource = chargeContext.LedgerSource,
                ProductId = persistedSession.ProductId,
                SponsorInviteId = persistedSession.SponsorInviteId,
                InterviewSessionId = persistedSession.Id,
                Remarks = chargeContext.Remarks,
                CreatedOnUtc = chargedOnUtc
            });

            persistedSession.CreditChargedOnUtc = chargedOnUtc;
            persistedSession.CreditChargeCustomerId = chargeContext.CustomerId;
            persistedSession.CreditChargeAmount = InterviewStartCost;
            persistedSession.CreditChargeLedgerSource = chargeContext.LedgerSource;
            await _sessionRepository.UpdateAsync(persistedSession);
            transaction.Complete();

            return BuildChargeResult(persistedSession, true, false, true, chargeContext);
        }
        catch
        {
            // Serializable persistence may choose one parallel request as a transaction victim.
            // If its peer committed first, return that durable marker as a successful no-op.
            var persistedSession = await _sessionRepository.GetByIdAsync(session.Id);
            if (persistedSession?.CreditChargedOnUtc.HasValue == true)
                return BuildChargeResult(persistedSession, true, true, false);
            throw;
        }
    }

    public async Task<bool> RefundAsync(InterviewSession session, string reasonCode, string notice)
    {
        if (session == null || session.Id <= 0)
            return false;

        using var sessionLock = await AcquireSessionCreditLockAsync(session.Id);
        try
        {
            using var transaction = _dataProvider.CreateTransactionScope();
            var persistedSession = await _sessionRepository.GetByIdAsync(session.Id) ?? session;
            if (!persistedSession.CreditChargedOnUtc.HasValue || persistedSession.CreditRefundedOnUtc.HasValue || persistedSession.CreditChargeCustomerId <= 0)
                return false;

            var wallet = await GetWalletAsync(persistedSession.CreditChargeCustomerId);
            if (wallet == null)
                return false;

            var amount = persistedSession.CreditChargeAmount > 0 ? persistedSession.CreditChargeAmount : InterviewStartCost;
            wallet.Balance += amount;
            await _walletRepository.UpdateAsync(wallet);

            var refundedOnUtc = DateTime.UtcNow;
            await _ledgerRepository.InsertAsync(new CreditLedgerEntry
            {
                CreditWalletId = wallet.Id,
                Amount = amount,
                TransactionType = "Deposit",
                LedgerSource = CreditLedgerSources.InterviewStartRefund,
                ProductId = persistedSession.ProductId,
                SponsorInviteId = persistedSession.SponsorInviteId,
                InterviewSessionId = persistedSession.Id,
                Remarks = notice,
                CreatedOnUtc = refundedOnUtc
            });

            persistedSession.CreditRefundedOnUtc = refundedOnUtc;
            persistedSession.CreditRefundReasonCode = reasonCode;
            await _sessionRepository.UpdateAsync(persistedSession);
            transaction.Complete();

            _logger.LogWarning(
                "AIInterview credit refund recorded. ReasonCode={ReasonCode}; SessionId={SessionId}; CustomerId={CustomerId}; LedgerSource={LedgerSource}; Amount={Amount}.",
                reasonCode, persistedSession.Id, persistedSession.CreditChargeCustomerId, persistedSession.CreditChargeLedgerSource, amount);
            return true;
        }
        catch
        {
            var persistedSession = await _sessionRepository.GetByIdAsync(session.Id);
            if (persistedSession?.CreditRefundedOnUtc.HasValue == true)
                return false;
            throw;
        }
    }

    public async Task NotifyRefundAsync(InterviewSession session, string reasonCode)
    {
        if (session == null || session.Id <= 0)
            return;

        using var sessionLock = await AcquireSessionCreditLockAsync(session.Id);
        var persistedSession = await _sessionRepository.GetByIdAsync(session.Id) ?? session;
        var effectiveReasonCode = reasonCode ?? persistedSession.CreditRefundReasonCode ?? string.Empty;
        if (!persistedSession.CreditRefundedOnUtc.HasValue || persistedSession.CreditChargeCustomerId <= 0)
            return;

        if (persistedSession.CreditRefundNotificationSentOnUtc.HasValue)
        {
            LogRefundNotificationSuppressed(persistedSession, effectiveReasonCode, "AlreadySent");
            return;
        }

        if (persistedSession.CreditRefundNotificationAttemptCount >= RefundNotificationMaxAttempts)
        {
            LogRefundNotificationSuppressed(persistedSession, effectiveReasonCode, "RetryLimitReached");
            return;
        }

        try
        {
            var chargedCustomer = await _customerService.GetCustomerByIdAsync(persistedSession.CreditChargeCustomerId);
            if (chargedCustomer == null || string.IsNullOrWhiteSpace(chargedCustomer.Email) || !CommonHelper.IsValidEmail(chargedCustomer.Email))
                throw new InvalidOperationException("The charged customer does not have a valid refund notification email address.");

            var store = await _storeContext.GetCurrentStoreAsync();
            var storeId = store?.Id ?? 0;
            var languageId = store?.DefaultLanguageId ?? 0;
            var templates = await _messageTemplateService.GetMessageTemplatesByNameAsync(RefundNotificationTemplateName, storeId);
            var template = templates?.FirstOrDefault();
            if (template == null || !template.IsActive)
                throw new InvalidOperationException("The interview start refund notification template is unavailable or inactive.");

            var emailAccountId = template.EmailAccountId > 0 ? template.EmailAccountId : _emailAccountSettings.DefaultEmailAccountId;
            var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(emailAccountId);
            emailAccount ??= (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();
            if (emailAccount == null)
                throw new InvalidOperationException("No email account is available for the interview start refund notification.");

            var product = persistedSession.ProductId > 0
                ? await _productService.GetProductByIdAsync(persistedSession.ProductId)
                : null;
            var occurredUtc = persistedSession.CreditRefundedOnUtc.Value;
            var tokens = new List<Nop.Services.Messages.Token>
            {
                new("AIInterview.SessionId", persistedSession.Id.ToString(CultureInfo.InvariantCulture)),
                new("AIInterview.ProductName", product?.Name ?? "Interview"),
                new("AIInterview.RefundAmount", FormatCredits(persistedSession.CreditChargeAmount > 0 ? persistedSession.CreditChargeAmount : InterviewStartCost)),
                new("AIInterview.RefundReason", effectiveReasonCode),
                new("AIInterview.OccurredUtc", occurredUtc.ToString("u", CultureInfo.InvariantCulture))
            };

            using (var transaction = _dataProvider.CreateTransactionScope())
            {
                persistedSession = await _sessionRepository.GetByIdAsync(session.Id) ?? persistedSession;
                if (!persistedSession.CreditRefundedOnUtc.HasValue || persistedSession.CreditChargeCustomerId <= 0)
                    return;

                if (persistedSession.CreditRefundNotificationSentOnUtc.HasValue)
                {
                    LogRefundNotificationSuppressed(persistedSession, effectiveReasonCode, "AlreadySent");
                    return;
                }

                if (persistedSession.CreditRefundNotificationAttemptCount >= RefundNotificationMaxAttempts)
                {
                    LogRefundNotificationSuppressed(persistedSession, effectiveReasonCode, "RetryLimitReached");
                    return;
                }

                var leaseCutoffUtc = DateTime.UtcNow.Subtract(RefundNotificationProcessingLease);
                if (persistedSession.CreditRefundNotificationProcessingOnUtc > leaseCutoffUtc)
                {
                    LogRefundNotificationSuppressed(persistedSession, effectiveReasonCode, "AttemptInProgress");
                    return;
                }

                persistedSession.CreditRefundNotificationProcessingOnUtc = DateTime.UtcNow;
                await _sessionRepository.UpdateAsync(persistedSession);
                transaction.Complete();
            }

            while (persistedSession.CreditRefundNotificationAttemptCount < RefundNotificationMaxAttempts)
            {
                var attemptNumber = persistedSession.CreditRefundNotificationAttemptCount + 1;
                _logger.LogInformation(
                    "AIInterview refund email attempt started. ReasonCode={ReasonCode}; CustomerId={CustomerId}; SessionId={SessionId}; AttemptNumber={AttemptNumber}; MaxAttempts={MaxAttempts}.",
                    effectiveReasonCode, persistedSession.CreditChargeCustomerId, persistedSession.Id, attemptNumber, RefundNotificationMaxAttempts);

                try
                {
                    await _workflowMessageService.SendNotificationAsync(
                        template,
                        emailAccount,
                        languageId,
                        tokens,
                        chargedCustomer.Email,
                        $"{chargedCustomer.FirstName} {chargedCustomer.LastName}".Trim(),
                        ignoreDelayBeforeSend: true);

                    var sentOnUtc = DateTime.UtcNow;
                    using (var transaction = _dataProvider.CreateTransactionScope())
                    {
                        persistedSession = await _sessionRepository.GetByIdAsync(session.Id) ?? persistedSession;
                        persistedSession.CreditRefundNotificationAttemptCount = Math.Max(
                            persistedSession.CreditRefundNotificationAttemptCount,
                            attemptNumber);
                        persistedSession.CreditRefundNotificationAttemptedOnUtc = sentOnUtc;
                        persistedSession.CreditRefundNotificationSentOnUtc ??= sentOnUtc;
                        persistedSession.CreditRefundNotificationProcessingOnUtc = null;
                        await _sessionRepository.UpdateAsync(persistedSession);
                        transaction.Complete();
                    }

                    _logger.LogInformation(
                        "AIInterview refund email sent. ReasonCode={ReasonCode}; CustomerId={CustomerId}; SessionId={SessionId}; RefundAmount={RefundAmount}; AttemptNumber={AttemptNumber}.",
                        effectiveReasonCode, persistedSession.CreditChargeCustomerId, persistedSession.Id, persistedSession.CreditChargeAmount, attemptNumber);
                    return;
                }
                catch (Exception exception)
                {
                    var isTransient = IsTransientNotificationFailure(exception);
                    var willRetry = isTransient && attemptNumber < RefundNotificationMaxAttempts;
                    var attemptedOnUtc = DateTime.UtcNow;

                    using (var transaction = _dataProvider.CreateTransactionScope())
                    {
                        persistedSession = await _sessionRepository.GetByIdAsync(session.Id) ?? persistedSession;
                        persistedSession.CreditRefundNotificationAttemptCount = Math.Max(
                            persistedSession.CreditRefundNotificationAttemptCount,
                            attemptNumber);
                        persistedSession.CreditRefundNotificationAttemptedOnUtc = attemptedOnUtc;
                        if (!willRetry)
                            persistedSession.CreditRefundNotificationProcessingOnUtc = null;
                        await _sessionRepository.UpdateAsync(persistedSession);
                        transaction.Complete();
                    }

                    _logger.LogWarning(
                        exception,
                        "AIInterview refund email attempt failed. ReasonCode={ReasonCode}; CustomerId={CustomerId}; SessionId={SessionId}; AttemptNumber={AttemptNumber}; MaxAttempts={MaxAttempts}; IsTransient={IsTransient}; WillRetry={WillRetry}.",
                        effectiveReasonCode, persistedSession.CreditChargeCustomerId, persistedSession.Id, attemptNumber, RefundNotificationMaxAttempts, isTransient, willRetry);

                    if (!willRetry)
                    {
                        _logger.LogError(
                            exception,
                            "AIInterview refund email final failure. ReasonCode={ReasonCode}; CustomerId={CustomerId}; SessionId={SessionId}; AttemptCount={AttemptCount}; IsTransient={IsTransient}.",
                            effectiveReasonCode, persistedSession.CreditChargeCustomerId, persistedSession.Id, persistedSession.CreditRefundNotificationAttemptCount, isTransient);
                        return;
                    }

                    await Task.Delay(GetRefundNotificationRetryDelay(attemptNumber));
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "AIInterview refund email preparation or persistence failed. ReasonCode={ReasonCode}; CustomerId={CustomerId}; SessionId={SessionId}; AttemptCount={AttemptCount}.",
                effectiveReasonCode, persistedSession.CreditChargeCustomerId, session.Id, persistedSession.CreditRefundNotificationAttemptCount);
        }
    }

    private static bool IsTransientNotificationFailure(Exception exception)
    {
        if (exception is TimeoutException or TaskCanceledException or IOException or
            System.Net.Http.HttpRequestException or System.Net.Mail.SmtpException or System.Net.Sockets.SocketException)
        {
            return true;
        }

        return exception.InnerException != null && IsTransientNotificationFailure(exception.InnerException);
    }

    private static TimeSpan GetRefundNotificationRetryDelay(int failedAttemptNumber)
    {
        return TimeSpan.FromMilliseconds(250 * failedAttemptNumber);
    }

    private void LogRefundNotificationSuppressed(InterviewSession session, string reasonCode, string suppressionReason)
    {
        _logger.LogInformation(
            "AIInterview refund email suppressed. ReasonCode={ReasonCode}; CustomerId={CustomerId}; SessionId={SessionId}; SuppressionReason={SuppressionReason}; AttemptCount={AttemptCount}.",
            reasonCode, session.CreditChargeCustomerId, session.Id, suppressionReason, session.CreditRefundNotificationAttemptCount);
    }

    private async Task<CreditWallet> GetWalletAsync(int customerId)
    {
        return (await _walletRepository.GetAllAsync(query => query
            .Where(wallet => wallet.CustomerId == customerId)
            .OrderBy(wallet => wallet.Id)))
            .FirstOrDefault();
    }

    private static string FormatCredits(decimal value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private async Task<(int CustomerId, string LedgerSource, string Remarks)> ResolveChargeContextAsync(InterviewSession session)
    {
        if (session.SponsorInviteId > 0)
        {
            var invite = await _inviteRepository.GetByIdAsync(session.SponsorInviteId);
            var inviteUsable = invite != null &&
                invite.IsActive &&
                invite.ProductId == session.ProductId &&
                (!invite.ExpiryDateUtc.HasValue || invite.ExpiryDateUtc > DateTime.UtcNow);
            if (!inviteUsable)
                return (0, CreditLedgerSources.SponsorInterviewUsage, "Sponsored Interview Start Charge");

            return (invite.SponsorId, CreditLedgerSources.SponsorInterviewUsage, "Sponsored Interview Start Charge");
        }

        return (session.CustomerId, CreditLedgerSources.InterviewUsage, "Interview Start Charge");
    }

    private static InterviewCreditEligibilityResult BuildEligibility(
        InterviewSession session,
        bool eligible,
        bool alreadyCharged,
        (int CustomerId, string LedgerSource, string Remarks)? context = null)
    {
        return new InterviewCreditEligibilityResult
        {
            Eligible = eligible,
            AlreadyCharged = alreadyCharged,
            ChargeCustomerId = session.CreditChargeCustomerId > 0 ? session.CreditChargeCustomerId : context?.CustomerId ?? 0,
            LedgerSource = !string.IsNullOrWhiteSpace(session.CreditChargeLedgerSource) ? session.CreditChargeLedgerSource : context?.LedgerSource
        };
    }

    private static bool HasExistingCharge(InterviewSession session)
    {
        // Sessions created by older plugin versions were charged before navigation and marked
        // StartedOnUtc immediately. Treat them as already paid so deployment cannot double-charge
        // an applicant who still has an active legacy token.
        return session?.CreditChargedOnUtc.HasValue == true || session?.StartedOnUtc.HasValue == true;
    }

    private static InterviewCreditChargeResult BuildChargeResult(
        InterviewSession session,
        bool eligible,
        bool alreadyCharged,
        bool chargedNow,
        (int CustomerId, string LedgerSource, string Remarks)? context = null)
    {
        return new InterviewCreditChargeResult
        {
            Eligible = eligible,
            AlreadyCharged = alreadyCharged,
            ChargedNow = chargedNow,
            ChargeCustomerId = session.CreditChargeCustomerId > 0 ? session.CreditChargeCustomerId : context?.CustomerId ?? 0,
            LedgerSource = !string.IsNullOrWhiteSpace(session.CreditChargeLedgerSource) ? session.CreditChargeLedgerSource : context?.LedgerSource
        };
    }

    private static async Task<IDisposable> AcquireSessionCreditLockAsync(int sessionId)
    {
        SessionCreditLock sessionLock;
        while (true)
        {
            sessionLock = SessionCreditLocks.GetOrAdd(sessionId, _ => new SessionCreditLock());
            lock (sessionLock)
            {
                if (SessionCreditLocks.TryGetValue(sessionId, out var current) && ReferenceEquals(current, sessionLock))
                {
                    sessionLock.ReferenceCount++;
                    break;
                }
            }
        }

        await sessionLock.Semaphore.WaitAsync();
        return new SessionCreditLockLease(sessionId, sessionLock);
    }
}

public class CreditActivityService : ICreditActivityService
{
    private sealed record LedgerDisplayContext(CreditLedgerEntry Entry, int CustomerId, decimal BalanceAfter);

    private readonly IRepository<CreditWallet> _walletRepository;
    private readonly IRepository<CreditLedgerEntry> _ledgerRepository;
    private readonly IRepository<CreditPurchaseGrant> _grantRepository;
    private readonly IRepository<InterviewSession> _sessionRepository;
    private readonly IRepository<JobApplication> _applicationRepository;
    private readonly IRepository<SponsorInvite> _inviteRepository;
    private readonly Nop.Services.Catalog.IProductService _productService;
    private readonly IDateTimeHelper _dateTimeHelper;

    public CreditActivityService(
        IRepository<CreditWallet> walletRepository,
        IRepository<CreditLedgerEntry> ledgerRepository,
        IRepository<CreditPurchaseGrant> grantRepository,
        IRepository<InterviewSession> sessionRepository,
        IRepository<JobApplication> applicationRepository,
        IRepository<SponsorInvite> inviteRepository,
        Nop.Services.Catalog.IProductService productService,
        IDateTimeHelper dateTimeHelper)
    {
        _walletRepository = walletRepository;
        _ledgerRepository = ledgerRepository;
        _grantRepository = grantRepository;
        _sessionRepository = sessionRepository;
        _applicationRepository = applicationRepository;
        _inviteRepository = inviteRepository;
        _productService = productService;
        _dateTimeHelper = dateTimeHelper;
    }

    public async Task<CreditActivityModel> BuildCreditActivityModelAsync(Customer customer, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var normalizedPageSize = pageSize < 1 ? 5 : Math.Min(pageSize, 50);
        var wallets = (await _walletRepository.GetAllAsync(query => query.Where(wallet => wallet.CustomerId == customer.Id)))
            .OrderBy(wallet => wallet.Id)
            .ToList();
        var walletIds = wallets.Select(wallet => wallet.Id).ToArray();
        var ledgerEntries = walletIds.Length == 0
            ? new List<CreditLedgerEntry>()
            : await _ledgerRepository.Table
                .Where(entry => walletIds.Contains(entry.CreditWalletId))
                .OrderBy(entry => entry.CreatedOnUtc)
                .ThenBy(entry => entry.Id)
                .ToListAsync();

        var totalCount = ledgerEntries.Count;
        var totalPages = totalCount > 0 ? (int)Math.Ceiling(totalCount / (double)normalizedPageSize) : 0;
        var normalizedPage = page < 1 ? 1 : page;
        if (totalPages > 0 && normalizedPage > totalPages)
            normalizedPage = totalPages;

        decimal runningBalance = 0;
        var displayContexts = ledgerEntries
            .Select(entry =>
            {
                runningBalance += entry.Amount;
                return new LedgerDisplayContext(entry, customer.Id, runningBalance);
            })
            .OrderByDescending(context => context.Entry.CreatedOnUtc)
            .ThenByDescending(context => context.Entry.Id)
            .ToList();

        var pagedContexts = displayContexts
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToList();

        var rows = new List<MyActivityCreditLedgerRowModel>();
        var customerTimeZone = _dateTimeHelper == null ? TimeZoneInfo.Utc : await _dateTimeHelper.GetCustomerTimeZoneAsync(customer);
        foreach (var context in pagedContexts)
        {
            var entry = context.Entry;
            var localCreatedOn = _dateTimeHelper == null
                ? DateTime.SpecifyKind(entry.CreatedOnUtc, DateTimeKind.Utc).ToLocalTime()
                : _dateTimeHelper.ConvertToUserTime(entry.CreatedOnUtc, TimeZoneInfo.Utc, customerTimeZone);
            var metadata = await ResolveDisplayMetadataAsync(context);
            var creditsDisplay = FormatCredits(entry.Amount, includeSign: true);

            rows.Add(new MyActivityCreditLedgerRowModel
            {
                CreatedOnUtc = entry.CreatedOnUtc,
                CreatedOn = localCreatedOn,
                CreatedOnDisplay = localCreatedOn.ToString("g", CultureInfo.CurrentCulture),
                Type = entry.Amount >= 0 ? "Deposit" : "Withdrawal",
                Credits = entry.Amount,
                CreditsDisplay = creditsDisplay,
                BalanceAfter = context.BalanceAfter,
                BalanceAfterDisplay = FormatCredits(context.BalanceAfter),
                JobProduct = metadata.JobProduct,
                Source = metadata.Source,
                Description = metadata.Description
            });
        }

        return new CreditActivityModel
        {
            CurrentBalance = wallets.Sum(wallet => wallet.Balance),
            CurrentBalanceDisplay = FormatCredits(wallets.Sum(wallet => wallet.Balance)),
            TotalDeposited = ledgerEntries.Where(entry => entry.Amount > 0).Sum(entry => entry.Amount),
            TotalDepositedDisplay = FormatCredits(ledgerEntries.Where(entry => entry.Amount > 0).Sum(entry => entry.Amount)),
            TotalWithdrawn = ledgerEntries.Where(entry => entry.Amount < 0).Sum(entry => -entry.Amount),
            TotalWithdrawnDisplay = FormatCredits(ledgerEntries.Where(entry => entry.Amount < 0).Sum(entry => -entry.Amount)),
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Entries = rows
        };
    }

    private async Task<(string JobProduct, string Source, string Description)> ResolveDisplayMetadataAsync(LedgerDisplayContext context)
    {
        var entry = context.Entry;
        if (entry.Amount > 0)
            return await ResolveDepositDisplayMetadataAsync(context);

        if (entry.Amount < 0)
            return await ResolveWithdrawalDisplayMetadataAsync(context);

        return ("-", CreditLedgerSources.Adjustment, "Credit adjustment");
    }

    private async Task<(string JobProduct, string Source, string Description)> ResolveDepositDisplayMetadataAsync(LedgerDisplayContext context)
    {
        var entry = context.Entry;
        var productName = await ResolveProductNameAsync(entry.ProductId);
        var grant = await ResolvePurchaseGrantAsync(context);
        productName ??= await ResolveProductNameAsync(grant?.ProductId ?? 0);

        if (IsSource(entry, CreditLedgerSources.InterviewStartRefund))
            return (productName ?? "-", CreditLedgerSources.InterviewStartRefund, "Interview start refund");

        if (IsSource(entry, CreditLedgerSources.Order) || entry.OrderId > 0 || grant != null)
            return (productName ?? "Credit pack", CreditLedgerSources.Order, "Credit pack purchase");

        return ("Credit top-up", CreditLedgerSources.AdminTopUp, "Admin credit top-up");
    }

    private async Task<(string JobProduct, string Source, string Description)> ResolveWithdrawalDisplayMetadataAsync(LedgerDisplayContext context)
    {
        var entry = context.Entry;
        var productName = await ResolveProductNameAsync(entry.ProductId);
        var isSponsored = IsSource(entry, CreditLedgerSources.SponsorInterviewUsage) || entry.SponsorInviteId > 0;

        if (productName == null)
        {
            var session = await ResolveInterviewSessionAsync(context);
            productName = await ResolveSessionProductNameAsync(session);
            isSponsored = isSponsored || session?.SponsorInviteId > 0;
        }

        if (isSponsored)
            return (productName ?? "-", CreditLedgerSources.SponsorInterviewUsage, "Sponsored interview started");

        if (IsSource(entry, CreditLedgerSources.Adjustment) || !string.Equals(entry.TransactionType, "Withdrawal", StringComparison.OrdinalIgnoreCase))
            return (productName ?? "-", CreditLedgerSources.Adjustment, "Credit adjustment");

        return (productName ?? "-", CreditLedgerSources.InterviewUsage, "Interview started");
    }

    private static bool IsSource(CreditLedgerEntry entry, string source)
    {
        return string.Equals(entry?.LedgerSource, source, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CreditPurchaseGrant> ResolvePurchaseGrantAsync(LedgerDisplayContext context)
    {
        var entry = context.Entry;
        if (entry.OrderId > 0)
        {
            var orderGrants = await _grantRepository.Table
                .Where(grant => grant.CustomerId == context.CustomerId && grant.OrderId == entry.OrderId)
                .ToListAsync();
            var byOrder = orderGrants
                .OrderBy(grant => Math.Abs(grant.CreditsGranted - entry.Amount))
                .ThenBy(grant => Math.Abs((grant.CreatedOnUtc - entry.CreatedOnUtc).TotalSeconds))
                .FirstOrDefault();
            if (byOrder != null)
                return byOrder;
        }

        var matchingGrants = await _grantRepository.Table
            .Where(grant => grant.CustomerId == context.CustomerId &&
                grant.CreditsGranted == entry.Amount &&
                grant.CreatedOnUtc >= entry.CreatedOnUtc.AddMinutes(-5) &&
                grant.CreatedOnUtc <= entry.CreatedOnUtc.AddMinutes(5))
            .ToListAsync();

        return matchingGrants
            .OrderBy(grant => Math.Abs((grant.CreatedOnUtc - entry.CreatedOnUtc).TotalSeconds))
            .FirstOrDefault();
    }

    private async Task<InterviewSession> ResolveInterviewSessionAsync(LedgerDisplayContext context)
    {
        var entry = context.Entry;
        if (entry.SponsorInviteId > 0)
        {
            var sponsoredSessions = await _sessionRepository.Table
                .Where(session => session.SponsorInviteId == entry.SponsorInviteId &&
                    session.CreatedOnUtc >= entry.CreatedOnUtc.AddMinutes(-5) &&
                    session.CreatedOnUtc <= entry.CreatedOnUtc.AddMinutes(10))
                .ToListAsync();
            var sponsoredSession = sponsoredSessions
                .OrderBy(session => Math.Abs((session.CreatedOnUtc - entry.CreatedOnUtc).TotalSeconds))
                .FirstOrDefault();
            if (sponsoredSession != null)
                return sponsoredSession;
        }

        var directSessions = await _sessionRepository.Table
            .Where(session => session.CustomerId == context.CustomerId &&
                session.CreatedOnUtc >= entry.CreatedOnUtc.AddMinutes(-5) &&
                session.CreatedOnUtc <= entry.CreatedOnUtc.AddMinutes(10))
            .ToListAsync();
        var directSession = directSessions
            .OrderBy(session => Math.Abs((session.CreatedOnUtc - entry.CreatedOnUtc).TotalSeconds))
            .FirstOrDefault();
        if (directSession != null)
            return directSession;

        var inviteIds = await _inviteRepository.Table
            .Where(invite => invite.SponsorId == context.CustomerId)
            .Select(invite => invite.Id)
            .ToListAsync();
        if (!inviteIds.Any())
            return null;

        var sponsorSessions = await _sessionRepository.Table
            .Where(session => inviteIds.Contains(session.SponsorInviteId) &&
                session.CreatedOnUtc >= entry.CreatedOnUtc.AddMinutes(-5) &&
                session.CreatedOnUtc <= entry.CreatedOnUtc.AddMinutes(10))
            .ToListAsync();

        return sponsorSessions
            .OrderBy(session => Math.Abs((session.CreatedOnUtc - entry.CreatedOnUtc).TotalSeconds))
            .FirstOrDefault();
    }

    private async Task<string> ResolveSessionProductNameAsync(InterviewSession session)
    {
        if (session == null)
            return null;

        var productName = await ResolveProductNameAsync(session.SourceProductId > 0 ? session.SourceProductId : session.ProductId);
        if (productName != null)
            return productName;

        if (session.JobApplicationId <= 0)
            return null;

        var application = await _applicationRepository.GetByIdAsync(session.JobApplicationId);
        productName = await ResolveProductNameAsync(application?.ProductId ?? 0);
        return productName ?? application?.JobTitle;
    }

    private async Task<string> ResolveProductNameAsync(int productId)
    {
        if (productId <= 0 || _productService == null)
            return null;

        var product = await _productService.GetProductByIdAsync(productId);
        return string.IsNullOrWhiteSpace(product?.Name) ? null : product.Name;
    }

    private static string FormatCredits(decimal value, bool includeSign = false)
    {
        var formatted = value.ToString("0.####", CultureInfo.InvariantCulture);
        return includeSign && value > 0 ? $"+{formatted}" : formatted;
    }
}

public class CreditDepositNotificationService : ICreditDepositNotificationService
{
    public const string TemplateName = "AIInterview.CreditDeposited";

    private readonly ICustomerService _customerService;
    private readonly IRepository<CreditWallet> _walletRepository;
    private readonly IRepository<CreditLedgerEntry> _ledgerRepository;
    private readonly Nop.Services.Messages.IWorkflowMessageService _workflowMessageService;
    private readonly Nop.Services.Messages.IMessageTemplateService _messageTemplateService;
    private readonly Nop.Services.Messages.IEmailAccountService _emailAccountService;
    private readonly Nop.Services.Messages.IMessageTokenProvider _messageTokenProvider;
    private readonly EmailAccountSettings _emailAccountSettings;
    private readonly IStoreContext _storeContext;
    private readonly IWebHelper _webHelper;
    private readonly ILogger<CreditDepositNotificationService> _logger;
    private readonly AIInterviewSettings _settings;

    public CreditDepositNotificationService(
        ICustomerService customerService,
        IRepository<CreditWallet> walletRepository,
        IRepository<CreditLedgerEntry> ledgerRepository,
        Nop.Services.Messages.IWorkflowMessageService workflowMessageService,
        Nop.Services.Messages.IMessageTemplateService messageTemplateService,
        Nop.Services.Messages.IEmailAccountService emailAccountService,
        Nop.Services.Messages.IMessageTokenProvider messageTokenProvider,
        EmailAccountSettings emailAccountSettings,
        IStoreContext storeContext,
        IWebHelper webHelper,
        ILogger<CreditDepositNotificationService> logger,
        AIInterviewSettings settings = null)
    {
        _customerService = customerService;
        _walletRepository = walletRepository;
        _ledgerRepository = ledgerRepository;
        _workflowMessageService = workflowMessageService;
        _messageTemplateService = messageTemplateService;
        _emailAccountService = emailAccountService;
        _messageTokenProvider = messageTokenProvider;
        _emailAccountSettings = emailAccountSettings;
        _storeContext = storeContext;
        _webHelper = webHelper;
        _logger = logger;
        _settings = settings;
    }

    public async Task SendCreditDepositedNotificationAsync(CreditDepositNotificationRequest request)
    {
        if (request == null || request.CustomerId <= 0 || request.CreditsDeposited <= 0)
            return;

        try
        {
            var customer = await _customerService.GetCustomerByIdAsync(request.CustomerId);
            if (customer == null || string.IsNullOrWhiteSpace(customer.Email) || !CommonHelper.IsValidEmail(customer.Email))
                return;

            var store = await _storeContext.GetCurrentStoreAsync();
            var storeId = store?.Id ?? 0;
            var languageId = store?.DefaultLanguageId ?? 0;

            var templates = await _messageTemplateService.GetMessageTemplatesByNameAsync(TemplateName, storeId);
            var template = templates?.FirstOrDefault();
            if (template == null || !template.IsActive)
                return;

            var emailAccountId = template.EmailAccountId > 0 ? template.EmailAccountId : _emailAccountSettings.DefaultEmailAccountId;
            var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(emailAccountId);
            emailAccount ??= (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();
            if (emailAccount == null)
                return;

            var wallets = (await _walletRepository.GetAllAsync(query => query.Where(wallet => wallet.CustomerId == request.CustomerId)))
                .OrderBy(wallet => wallet.Id)
                .ToList();
            var walletIds = wallets.Select(wallet => wallet.Id).ToArray();
            var totalCredits = wallets.Sum(wallet => wallet.Balance);
            var withdrawnCredits = walletIds.Length == 0
                ? 0m
                : _ledgerRepository.Table
                    .Where(entry => walletIds.Contains(entry.CreditWalletId) &&
                        entry.Amount < 0 &&
                        entry.TransactionType == "Withdrawal")
                    .Sum(entry => -entry.Amount);

            var storeLocation = (_webHelper.GetStoreLocation() ?? string.Empty).TrimEnd('/');
            var creditPagePath = string.IsNullOrWhiteSpace(_settings?.CreditPageUrl)
                ? AIInterviewDefaults.DefaultCreditPurchasePageUrl
                : _settings.CreditPageUrl;
            var creditPageUrl = creditPagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? creditPagePath
                : $"{storeLocation}/{creditPagePath.TrimStart('/')}";
            var tokens = new List<Nop.Services.Messages.Token>();
            await _messageTokenProvider.AddCustomerTokensAsync(tokens, customer);
            tokens.Add(new Nop.Services.Messages.Token("AIInterview.CreditsDeposited", FormatCredits(request.CreditsDeposited)));
            tokens.Add(new Nop.Services.Messages.Token("AIInterview.DepositSource", request.DepositSource ?? string.Empty));
            tokens.Add(new Nop.Services.Messages.Token("AIInterview.TotalCredits", FormatCredits(totalCredits)));
            tokens.Add(new Nop.Services.Messages.Token("AIInterview.WithdrawnCredits", FormatCredits(withdrawnCredits)));
            tokens.Add(new Nop.Services.Messages.Token("AIInterview.CreditPageUrl", creditPageUrl));
            tokens.Add(new Nop.Services.Messages.Token("AIInterview.OrderId", request.OrderId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
            tokens.Add(new Nop.Services.Messages.Token("AIInterview.TransactionDate", DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture)));
            tokens.Add(new Nop.Services.Messages.Token("AIInterview.DepositRemarks", request.Remarks ?? string.Empty));

            await _workflowMessageService.SendNotificationAsync(
                template,
                emailAccount,
                languageId,
                tokens,
                customer.Email,
                $"{customer.FirstName} {customer.LastName}".Trim(),
                ignoreDelayBeforeSend: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to send AIInterview credit deposit notification for customer {CustomerId}.", request.CustomerId);
        }
    }

    private static string FormatCredits(decimal value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}

public class SponsorInviteService : ISponsorInviteService
{
    private readonly IRepository<SponsorInvite> _inviteRepository;
    private readonly Nop.Services.Catalog.IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly ILocalizationService _localizationService;
    private readonly Nop.Services.Messages.IWorkflowMessageService _workflowMessageService;
    private readonly Nop.Services.Messages.IMessageTemplateService _messageTemplateService;
    private readonly Nop.Services.Messages.IEmailAccountService _emailAccountService;
    private readonly Nop.Core.Domain.Messages.EmailAccountSettings _emailAccountSettings;
    private readonly Nop.Core.IStoreContext _storeContext;
    private readonly IWebHelper _webHelper;
    private readonly IJobProductAccessService _jobProductAccessService;

    public SponsorInviteService(IRepository<SponsorInvite> inviteRepository,
        Nop.Services.Catalog.IProductService productService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        Nop.Services.Messages.IWorkflowMessageService workflowMessageService = null,
        Nop.Services.Messages.IMessageTemplateService messageTemplateService = null,
        Nop.Services.Messages.IEmailAccountService emailAccountService = null,
        Nop.Core.Domain.Messages.EmailAccountSettings emailAccountSettings = null,
        Nop.Core.IStoreContext storeContext = null,
        IWebHelper webHelper = null,
        IJobProductAccessService jobProductAccessService = null)
    {
        _inviteRepository = inviteRepository;
        _productService = productService;
        _customerService = customerService;
        _localizationService = localizationService;
        _workflowMessageService = workflowMessageService;
        _messageTemplateService = messageTemplateService;
        _emailAccountService = emailAccountService;
        _emailAccountSettings = emailAccountSettings;
        _storeContext = storeContext;
        _webHelper = webHelper;
        _jobProductAccessService = jobProductAccessService;
    }

    public async Task InsertSponsorInviteAsync(SponsorInvite invite)
    {
        await _inviteRepository.InsertAsync(invite);
    }

    public async Task<SponsorInvite> GetSponsorInviteByCodeAsync(string code)
    {
        if (string.IsNullOrEmpty(code))
            return null;

        return (await _inviteRepository.GetAllAsync(query => query.Where(i => i.InviteCode == code))).FirstOrDefault();
    }

    public async Task CreateInviteAsync(int sponsorId, string email, int productId, int maxAttempts, DateTime? expiryDateUtc)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.EmailRequired"));

        if (!CommonHelper.IsValidEmail(email))
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.EmailInvalid"));

        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.ProductNotFound"));

        if (_jobProductAccessService != null && !await _jobProductAccessService.CanAcceptJobApplicationsAsync(product))
            throw new NopException(await _localizationService.GetResourceAsync("Common.NotAvailable"));

        var sponsor = await _customerService.GetCustomerByIdAsync(sponsorId);
        if (sponsor == null)
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidOwnership"));

        if (!await _customerService.IsAdminAsync(sponsor))
        {
            if (product.VendorId == 0 || product.VendorId != sponsor.VendorId)
                throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidOwnership"));
        }

        if (maxAttempts <= 0)
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidAttempts"));

        if (expiryDateUtc.HasValue && expiryDateUtc.Value <= DateTime.UtcNow)
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidExpiry"));

        var invite = new SponsorInvite
        {
            SponsorId = sponsorId,
            ProductId = productId,
            Email = email,
            MaxAttempts = maxAttempts,
            ExpiryDateUtc = expiryDateUtc,
            IsActive = true,
            InviteCode = Guid.NewGuid().ToString("N"),
            IsAccepted = false,
            CreatedOnUtc = DateTime.UtcNow
        };

        await InsertSponsorInviteAsync(invite);
        await TrySendInviteNotificationAsync(invite, product);
    }

    protected virtual async Task TrySendInviteNotificationAsync(SponsorInvite invite, Product product)
    {
        if (invite == null || product == null ||
            _workflowMessageService == null ||
            _messageTemplateService == null ||
            _emailAccountService == null ||
            _emailAccountSettings == null ||
            _webHelper == null)
        {
            return;
        }

        try
        {
            var store = _storeContext == null ? null : await _storeContext.GetCurrentStoreAsync();
            var storeId = store?.Id ?? 0;
            var languageId = store?.DefaultLanguageId ?? 0;

            var templates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.SponsorInviteCreated", storeId);
            var template = templates?.FirstOrDefault();
            if (template == null)
                return;

            var emailAccountId = template.EmailAccountId > 0 ? template.EmailAccountId : _emailAccountSettings.DefaultEmailAccountId;
            var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(emailAccountId);
            emailAccount ??= (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();
            if (emailAccount == null)
                return;

              var storeLocation = (_webHelper.GetStoreLocation() ?? string.Empty).TrimEnd('/');
              var inviteUrl = $"{storeLocation}/mockaiinterview/start?productId={product.Id}&sponsorToken={Uri.EscapeDataString(invite.InviteCode ?? string.Empty)}";
            var tokens = new List<Nop.Services.Messages.Token>
            {
                new("AIInterview.JobTitle", product.Name ?? string.Empty),
                new("AIInterview.InviteUrl", inviteUrl),
                new("AIInterview.InviteCode", invite.InviteCode ?? string.Empty),
                new("AIInterview.MaxAttempts", invite.MaxAttempts),
                new("AIInterview.ExpiryDate", invite.ExpiryDateUtc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty)
            };

            await _workflowMessageService.SendNotificationAsync(
                template,
                emailAccount,
                languageId,
                tokens,
                invite.Email,
                invite.Email,
                ignoreDelayBeforeSend: true);
        }
        catch
        {
            // Invite creation must succeed even when notification delivery is unavailable.
        }
    }

    public async Task<IList<SponsorInvite>> GetSponsorInvitesAsync(int sponsorId)
    {
        return await _inviteRepository.GetAllAsync(query => query
            .Where(i => sponsorId <= 0 || i.SponsorId == sponsorId)
            .OrderByDescending(i => i.CreatedOnUtc));
    }

    public async Task<SponsorInvite> GetAcceptedInviteByEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        return (await _inviteRepository.GetAllAsync(query => query
            .Where(i => i.Email == email && i.IsAccepted)
            .OrderByDescending(i => i.CreatedOnUtc)))
            .FirstOrDefault();
    }

    public async Task DeactivateInviteAsync(int inviteId, int sponsorId)
    {
        var invite = await _inviteRepository.GetByIdAsync(inviteId);
        if (invite != null && invite.IsActive && (sponsorId <= 0 || invite.SponsorId == sponsorId))
        {
            invite.IsActive = false;
            await _inviteRepository.UpdateAsync(invite);
        }
    }

    public async Task<bool> ValidateInviteAsync(string code, string email)
    {
        var invite = await GetSponsorInviteByCodeAsync(code);
        if (invite == null) return false;
        if (!invite.IsActive) return false;
        if (invite.IsAccepted) return false;
        if (invite.ExpiryDateUtc.HasValue && invite.ExpiryDateUtc.Value <= DateTime.UtcNow) return false;
        if (!string.Equals(invite.Email, email, StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }
}

public class JobInterviewExperienceService : IJobInterviewExperienceService
{
    private readonly IProductAttributeService _productAttributeService;
    private readonly IProductAttributeParser _productAttributeParser;

    public JobInterviewExperienceService(IProductAttributeService productAttributeService,
        IProductAttributeParser productAttributeParser)
    {
        _productAttributeService = productAttributeService;
        _productAttributeParser = productAttributeParser;
    }

    public async Task EnsureInterviewDifficultyAttributeAsync(Product product)
    {
        if (product == null)
            return;

        var mappings = await _productAttributeService.GetProductAttributeMappingsByProductIdAsync(product.Id);
        var existingMapping = await FindDifficultyMappingAsync(mappings);
        if (existingMapping != null)
            return;

        var attribute = await GetOrCreateDifficultyAttributeAsync();
        var mapping = new ProductAttributeMapping
        {
            ProductId = product.Id,
            ProductAttributeId = attribute.Id,
            TextPrompt = AIInterviewDefaults.InterviewDifficultyAttributeName,
            IsRequired = true,
            AttributeControlType = AttributeControlType.RadioList,
            DisplayOrder = 1
        };
        await _productAttributeService.InsertProductAttributeMappingAsync(mapping);

        for (var index = 0; index < AIInterviewDefaults.InterviewDifficultyValues.Count; index++)
        {
            var value = AIInterviewDefaults.InterviewDifficultyValues[index];
            await _productAttributeService.InsertProductAttributeValueAsync(new ProductAttributeValue
            {
                ProductAttributeMappingId = mapping.Id,
                Name = value,
                IsPreSelected = string.Equals(value, "Medium", StringComparison.OrdinalIgnoreCase),
                DisplayOrder = index
            });
        }
    }

    public async Task<string> ResolveInterviewDifficultyAsync(Product product, IFormCollection form)
    {
        if (product == null)
            return "Medium";

        if (form == null)
            return "Medium";

        var errors = new List<string>();
        var attributesXml = await _productAttributeParser.ParseProductAttributesAsync(product, form, errors);
        if (string.IsNullOrEmpty(attributesXml))
            return "Medium";

        var values = await _productAttributeParser.ParseProductAttributeValuesAsync(attributesXml);
        var selectedDifficulty = values.FirstOrDefault(value =>
            AIInterviewDefaults.InterviewDifficultyValues.Any(difficulty =>
                string.Equals(difficulty, value.Name, StringComparison.OrdinalIgnoreCase)));

        return selectedDifficulty?.Name ?? "Medium";
    }

    protected virtual async Task<ProductAttribute> GetOrCreateDifficultyAttributeAsync()
    {
        var attributes = await _productAttributeService.GetAllProductAttributesAsync(AIInterviewDefaults.InterviewDifficultyAttributeName);
        var attribute = attributes.FirstOrDefault(item =>
            string.Equals(item.Name, AIInterviewDefaults.InterviewDifficultyAttributeName, StringComparison.OrdinalIgnoreCase));
        if (attribute != null)
            return attribute;

        attribute = new ProductAttribute
        {
            Name = AIInterviewDefaults.InterviewDifficultyAttributeName
        };
        await _productAttributeService.InsertProductAttributeAsync(attribute);
        return attribute;
    }

    protected virtual async Task<ProductAttributeMapping> FindDifficultyMappingAsync(IList<ProductAttributeMapping> mappings)
    {
        foreach (var mapping in mappings)
        {
            var attribute = await _productAttributeService.GetProductAttributeByIdAsync(mapping.ProductAttributeId);
            if (attribute == null)
                continue;

            if (string.Equals(attribute.Name, AIInterviewDefaults.InterviewDifficultyAttributeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapping.TextPrompt, AIInterviewDefaults.InterviewDifficultyAttributeName, StringComparison.OrdinalIgnoreCase))
            {
                return mapping;
            }
        }

        return null;
    }
}
