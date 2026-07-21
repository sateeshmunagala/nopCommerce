using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Vendors;
using Nop.Services.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Security.Cryptography;

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
        IDateTimeHelper dateTimeHelper)
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
        var templates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.ApplicationStatusUpdate", store.Id);
        var template = templates.FirstOrDefault();
        if (template == null) return;

        var customer = await _customerRepository.GetByIdAsync(application.CustomerId);
        if (customer == null) return;

        var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(template.EmailAccountId > 0 ? template.EmailAccountId : _emailAccountSettings.DefaultEmailAccountId);
        if (emailAccount == null) emailAccount = (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();
        if (emailAccount == null) return;

        var tokens = new List<Nop.Services.Messages.Token>();
        await _messageTokenProvider.AddCustomerTokensAsync(tokens, customer);
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.JobTitle", application.JobTitle));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.NewStatus", application.Status));
        var customerTimeZone = await _dateTimeHelper.GetCustomerTimeZoneAsync(customer);
        var updateTimestamp = _dateTimeHelper.ConvertToUserTime(DateTime.UtcNow, TimeZoneInfo.Utc, customerTimeZone);
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.UpdateTimestamp", updateTimestamp.ToString("g")));
        tokens.Add(new Nop.Services.Messages.Token("AIInterview.MyApplicationsUrl", $"{_webHelper.GetStoreLocation()}aiinterview/my-applications"));

        await _workflowMessageService.SendNotificationAsync(template, emailAccount, languageId, tokens, customer.Email, customer.FirstName + " " + customer.LastName);
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
        IDateTimeHelper dateTimeHelper)
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
                var customerTimeZone = await _dateTimeHelper.GetCustomerTimeZoneAsync(customer);
                var applicantCompletionDate = session.CompletedOnUtc.HasValue
                    ? _dateTimeHelper.ConvertToUserTime(session.CompletedOnUtc.Value, TimeZoneInfo.Utc, customerTimeZone).ToString("g")
                    : null;
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.CompletionDate", applicantCompletionDate));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.QuestionSummary", session.QuestionScores ?? ""));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.ReportUrl", $"{_webHelper.GetStoreLocation()}aiinterview/report/{session.Id}"));
                tokens.Add(new Nop.Services.Messages.Token("AIInterview.MyApplicationsUrl", $"{_webHelper.GetStoreLocation()}aiinterview/my-applications"));

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
                    var vendorCompletionDate = session.CompletedOnUtc.HasValue
                        ? _dateTimeHelper.ConvertToUserTime(session.CompletedOnUtc.Value, TimeZoneInfo.Utc, _dateTimeHelper.DefaultStoreTimeZone).ToString("g")
                        : null;
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.CompletionDate", vendorCompletionDate));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.QuestionSummary", session.QuestionScores ?? ""));
                    tokens.Add(new Nop.Services.Messages.Token("AIInterview.CandidateReportUrl", $"{_webHelper.GetStoreLocation()}aiinterview/report/{session.Id}"));

                    await _workflowMessageService.SendNotificationAsync(vendorTemplate, emailAccount, languageId, tokens, vendor.Email, vendor.Name);
                }
            }
        }
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

        return (await _sessionRepository.GetAllAsync(query => query.Where(s => s.SessionKey == sessionKey))).FirstOrDefault();
    }

    public async Task<InterviewSession> GetSessionByTokenAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        return (await _sessionRepository.GetAllAsync(query => query.Where(s => s.Token == token))).FirstOrDefault();
    }

    public async Task<InterviewSession> GetSessionByRecordingShareTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return (await _sessionRepository.GetAllAsync(query => query.Where(session =>
            session.RecordingShareEnabled &&
            session.RecordingShareToken == token))).FirstOrDefault();
    }

    public async Task<IList<InterviewSession>> GetSessionsByCustomerIdAsync(int customerId)
    {
        return await _sessionRepository.GetAllAsync(query => query
            .Where(s => s.CustomerId == customerId)
            .OrderByDescending(s => s.CreatedOnUtc));
    }

    public async Task<string> EnsureRecordingShareTokenAsync(InterviewSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.RecordingUrl))
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

    public async Task UpdateInterviewSessionAsync(InterviewSession session)
    {
        await _sessionRepository.UpdateAsync(session);
    }

    public async Task<bool> CanAccessReportAsync(int customerId, int sessionId)
    {
        var session = await GetInterviewSessionByIdAsync(sessionId);
        if (session == null)
            return false;

        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        if (customer == null)
            return false;

        if (session.CustomerId == customerId)
            return true;

        if (await _customerService.IsAdminAsync(customer))
            return true;

        if (customer.VendorId > 0)
        {
            if (session.ProductId > 0)
            {
                var product = await _productService.GetProductByIdAsync(session.ProductId);
                if (product != null && product.VendorId == customer.VendorId)
                    return true;
            }

            if (session.JobApplicationId > 0)
            {
                var application = await _applicationService.GetJobApplicationByIdAsync(session.JobApplicationId);
                if (application != null)
                {
                    var product = await _productService.GetProductByIdAsync(application.ProductId);
                    if (product != null && product.VendorId == customer.VendorId)
                        return true;
                }
            }
        }

        return false;
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
        var wallet = await GetOrCreateWalletAsync(customerId);
        wallet.Balance += amount;
        await _walletRepository.UpdateAsync(wallet);

        await _ledgerRepository.InsertAsync(new CreditLedgerEntry
        {
            CreditWalletId = wallet.Id,
            Amount = amount,
            TransactionType = "Deposit",
            Remarks = remarks,
            CreatedOnUtc = DateTime.UtcNow
        });
    }

    public async Task<bool> AuthorizeAndChargeAsync(int customerId, decimal amount, string remarks)
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
            Remarks = remarks,
            CreatedOnUtc = DateTime.UtcNow
        });

        return true;
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
            var creditPagePath = string.IsNullOrWhiteSpace(_settings?.CreditPurchasePageUrl)
                ? AIInterviewDefaults.DefaultCreditPurchasePageUrl
                : _settings.CreditPurchasePageUrl;
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
                new("AIInterview.ExpiryDate", invite.ExpiryDateUtc?.ToString("u") ?? string.Empty)
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
