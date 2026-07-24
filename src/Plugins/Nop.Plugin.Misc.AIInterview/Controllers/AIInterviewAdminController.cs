using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Vendors;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Vendors;
using Nop.Services.Helpers;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Models.DataTables;
using Nop.Web.Framework.Models.Extensions;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class AIInterviewAdminController : BasePluginController
{
    private const string AdminDateTimeDisplayFormat = "dd MMM yyyy, hh:mm tt";
    private const string AzureOpenAiProviderValue = "Azure OpenAI";
    private const string SecretMask = "********";

    private sealed record CreditWalletSnapshot(int Id, int CustomerId, decimal Balance);
    private sealed record CreditLedgerSnapshot(int CustomerId, decimal Amount, DateTime CreatedOnUtc);
    private sealed record CreditGrantSnapshot(int CustomerId, DateTime CreatedOnUtc);
    private sealed record ApplicantCreditActivityProjection(int CustomerId, string FirstName, string LastName, string Email, decimal WalletBalance, decimal TotalDeposited, decimal TotalWithdrawn, DateTime? LastCreditActivityUtc);
    private sealed record SelectedProductAttributeSummary([property: JsonPropertyName("AttributeName")] string AttributeName, [property: JsonPropertyName("Value")] string Value);
    private sealed record SelectedProductAttributesSummarySnapshot([property: JsonPropertyName("Attributes")] IList<SelectedProductAttributeSummary> Attributes);

    private readonly ICreditService _creditService;
    private readonly ISponsorInviteService _inviteService;
    private readonly IApplicationService _applicationService;
    private readonly IInterviewSessionService _sessionService;
    private readonly IInterviewTurnService _interviewTurnService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly IVendorService _vendorService;
    private readonly IJobRequirementService _jobRequirementService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly ILogger<AIInterviewAdminController> _logger;
    private readonly IWorkContext _workContext;
    private readonly ISettingService _settingService;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<InterviewSession> _sessionRepository;
    private readonly IRepository<Product> _productRepository;
    private readonly IRepository<CreditWallet> _walletRepository;
    private readonly IRepository<CreditLedgerEntry> _ledgerRepository;
    private readonly IRepository<CreditPurchaseGrant> _creditPurchaseGrantRepository;
    private readonly AIInterviewSettings _aiInterviewSettings;
    private readonly MockAIInterviewSettings _mockAIInterviewSettings;
    private readonly IJobProductAccessService _jobProductAccessService;
    private readonly ICreditDepositNotificationService _creditDepositNotificationService;
    private readonly IDownloadService _downloadService;

    public AIInterviewAdminController(ICreditService creditService,
        ISponsorInviteService inviteService,
        IApplicationService applicationService,
        IInterviewSessionService sessionService,
        ICustomerService customerService,
        IProductService productService,
        IVendorService vendorService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IDateTimeHelper dateTimeHelper,
        ILogger<AIInterviewAdminController> logger,
        IWorkContext workContext,
        ISettingService settingService,
        IRepository<Customer> customerRepository,
        IRepository<CreditWallet> walletRepository,
        IRepository<CreditLedgerEntry> ledgerRepository,
        IRepository<CreditPurchaseGrant> creditPurchaseGrantRepository,
        AIInterviewSettings aiInterviewSettings,
        MockAIInterviewSettings mockAIInterviewSettings,
        IJobRequirementService jobRequirementService = null,
        IInterviewTurnService interviewTurnService = null,
        IRepository<InterviewSession> sessionRepository = null,
        IRepository<Product> productRepository = null,
        IJobProductAccessService jobProductAccessService = null,
        ICreditDepositNotificationService creditDepositNotificationService = null,
        IDownloadService downloadService = null)
    {
        _creditService = creditService;
        _inviteService = inviteService;
        _applicationService = applicationService;
        _sessionService = sessionService;
        _interviewTurnService = interviewTurnService;
        _customerService = customerService;
        _productService = productService;
        _vendorService = vendorService;
        _jobRequirementService = jobRequirementService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _dateTimeHelper = dateTimeHelper;
        _logger = logger;
        _workContext = workContext;
        _settingService = settingService;
        _customerRepository = customerRepository;
        _sessionRepository = sessionRepository;
        _productRepository = productRepository;
        _walletRepository = walletRepository;
        _ledgerRepository = ledgerRepository;
        _creditPurchaseGrantRepository = creditPurchaseGrantRepository;
        _aiInterviewSettings = aiInterviewSettings;
        _mockAIInterviewSettings = mockAIInterviewSettings;
        _jobProductAccessService = jobProductAccessService;
        _creditDepositNotificationService = creditDepositNotificationService;
        _downloadService = downloadService;
    }

    protected async Task<string> GetLocalizedTextAsync(string resourceKey, string defaultValue)
    {
        var text = await _localizationService.GetResourceAsync(resourceKey);
        return string.IsNullOrWhiteSpace(text) || string.Equals(text, resourceKey, StringComparison.OrdinalIgnoreCase)
            ? defaultValue
            : text;
    }

    protected async Task<IActionResult> LocalizedErrorAsync(string resourceKey, string defaultValue, int statusCode = 400)
    {
        var text = await GetLocalizedTextAsync(resourceKey, defaultValue);
        return new JsonResult(new { success = false, message = text, error = text })
        {
            StatusCode = statusCode
        };
    }

    protected virtual async Task<string> FormatAdminLocalDateTimeAsync(DateTime? utcDateTime, string emptyDisplay = "")
    {
        if (!utcDateTime.HasValue)
            return emptyDisplay;

        var localDateTime = await _dateTimeHelper.ConvertToUserTimeAsync(utcDateTime.Value, DateTimeKind.Utc);
        return localDateTime.ToString(AdminDateTimeDisplayFormat, CultureInfo.InvariantCulture);
    }

    protected virtual async Task<(DateTime? StartUtc, DateTime? EndUtcExclusive)> ConvertLocalDateRangeToUtcAsync(DateTime? localDateFrom, DateTime? localDateTo)
    {
        var currentTimeZone = await _dateTimeHelper.GetCurrentTimeZoneAsync();
        var startUtc = localDateFrom.HasValue
            ? _dateTimeHelper.ConvertToUtcTime(localDateFrom.Value.Date, currentTimeZone)
            : (DateTime?)null;
        var endUtcExclusive = localDateTo.HasValue
            ? _dateTimeHelper.ConvertToUtcTime(localDateTo.Value.Date.AddDays(1), currentTimeZone)
            : (DateTime?)null;

        return (startUtc, endUtcExclusive);
    }

    public async Task<IActionResult> AiService()
    {
        return View("~/Plugins/Misc.AIInterview/Views/Admin/AiService.cshtml", await PrepareAiServiceModelAsync());
    }

    [HttpPost]
    public async Task<IActionResult> AiService(AiServiceSettingsModel settingsModel)
    {
        if (!ModelState.IsValid)
            return View("~/Plugins/Misc.AIInterview/Views/Admin/AiService.cshtml", await PrepareAiServiceModelAsync(settingsModel));

        if (!TryValidateCreditProductSkuMappingsJson(settingsModel.CreditProductSkuMappingsJson))
        {
            var mappingValidationError = await GetLocalizedTextAsync(
                "Plugins.Misc.AIInterview.Admin.AiService.CreditProductSkuMappingsJson.Invalid",
                "The credit product SKU mappings JSON is invalid. Use a JSON object such as {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10}.");
            ModelState.AddModelError(nameof(settingsModel.CreditProductSkuMappingsJson), mappingValidationError);
            _notificationService.ErrorNotification(mappingValidationError);
            return View("~/Plugins/Misc.AIInterview/Views/Admin/AiService.cshtml", await PrepareAiServiceModelAsync(settingsModel));
        }

        try
        {
            var currentAiInterviewSettings = await _settingService.LoadSettingAsync<AIInterviewSettings>() ?? _aiInterviewSettings;

            _mockAIInterviewSettings.UseMockResponses = settingsModel.UseMockResponses;
            await _settingService.SaveSettingAsync(_mockAIInterviewSettings);

            currentAiInterviewSettings.Provider = AzureOpenAiProviderValue;
            currentAiInterviewSettings.ApiKey = PreserveSecretIfBlank(settingsModel.ApiKey, currentAiInterviewSettings.ApiKey);
            currentAiInterviewSettings.Model = settingsModel.Model;
            currentAiInterviewSettings.Prompt = settingsModel.Prompt;
            currentAiInterviewSettings.ServiceSettings = settingsModel.ServiceSettings;
            currentAiInterviewSettings.CreditProductSkuMappingsJson = settingsModel.CreditProductSkuMappingsJson;
            currentAiInterviewSettings.CreditPurchasePageUrl = settingsModel.CreditPurchasePageUrl;
            currentAiInterviewSettings.AzureOpenAiEndpointUrl = settingsModel.AzureOpenAiEndpointUrl;
            currentAiInterviewSettings.AzureOpenAiApiKey = PreserveSecretIfBlank(settingsModel.AzureOpenAiApiKey, currentAiInterviewSettings.AzureOpenAiApiKey);
            currentAiInterviewSettings.AzureOpenAiDeploymentOrModel = settingsModel.AzureOpenAiDeploymentOrModel;
            currentAiInterviewSettings.AzureSpeechKey = PreserveSecretIfBlank(settingsModel.AzureSpeechKey, currentAiInterviewSettings.AzureSpeechKey);
            currentAiInterviewSettings.AzureSpeechRegion = settingsModel.AzureSpeechRegion;
            currentAiInterviewSettings.AzureDocumentIntelligenceEndpointUrl = settingsModel.AzureDocumentIntelligenceEndpointUrl;
            currentAiInterviewSettings.AzureDocumentIntelligenceApiKey = PreserveSecretIfBlank(settingsModel.AzureDocumentIntelligenceApiKey, currentAiInterviewSettings.AzureDocumentIntelligenceApiKey);
            currentAiInterviewSettings.AzureDocumentIntelligenceModelId = NormalizeAzureDocumentIntelligenceModelId(settingsModel.AzureDocumentIntelligenceModelId);
            currentAiInterviewSettings.AzureDocumentIntelligenceTimeoutSeconds = NormalizeAzureDocumentIntelligenceTimeoutSeconds(settingsModel.AzureDocumentIntelligenceTimeoutSeconds);
            currentAiInterviewSettings.TrackAzureOpenAiUsage = settingsModel.TrackAzureOpenAiUsage;
            currentAiInterviewSettings.TrackAzureSpeechUsage = settingsModel.TrackAzureSpeechUsage;
            currentAiInterviewSettings.CalculateAzureCostPerInterview = settingsModel.CalculateAzureCostPerInterview;
            currentAiInterviewSettings.AzureOpenAiPromptTokenPricePerThousand = settingsModel.AzureOpenAiPromptTokenPricePerThousand;
            currentAiInterviewSettings.AzureOpenAiCompletionTokenPricePerThousand = settingsModel.AzureOpenAiCompletionTokenPricePerThousand;
            currentAiInterviewSettings.AzureSpeechRecognitionPricePerHour = settingsModel.AzureSpeechRecognitionPricePerHour;
            currentAiInterviewSettings.AzureSpeechSynthesisPricePerThousandCharacters = settingsModel.AzureSpeechSynthesisPricePerThousandCharacters;
            currentAiInterviewSettings.AzureUsageCurrencyCode = settingsModel.AzureUsageCurrencyCode;
            currentAiInterviewSettings.AzureBlobStorageContainerUrl = settingsModel.AzureBlobStorageContainerUrl;
            currentAiInterviewSettings.AzureBlobStorageSasToken = PreserveSecretIfBlank(settingsModel.AzureBlobStorageSasToken, currentAiInterviewSettings.AzureBlobStorageSasToken);
            await _settingService.SaveSettingAsync(currentAiInterviewSettings);
        }
        catch (Exception exception)
        {
            const string defaultMessage = "Unable to save AI Interview service settings. Please check the values and try again.";
            _logger.LogError(exception, "Failed to save AI Interview service settings.");
            ModelState.AddModelError(string.Empty, defaultMessage);
            _notificationService.ErrorNotification(defaultMessage);
            return View("~/Plugins/Misc.AIInterview/Views/Admin/AiService.cshtml", await PrepareAiServiceModelAsync(settingsModel));
        }

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));
        return RedirectToRoute(AIInterviewDefaults.AdminAiServiceRouteName);
    }

    [HttpPost]
    public async Task<IActionResult> SaveProductRequirements(JobRequirementsModel model)
    {
        if (_jobRequirementService == null || model.ProductId <= 0)
            return Json(new { success = false });

        var product = await _productService.GetProductByIdAsync(model.ProductId);
        if (product == null)
            return Json(new { success = false });

        await _jobRequirementService.SaveRequirementsAsync(product, model.ResumeRequired, model.InterviewRequired, model.MinimumScore, model.QuestionCount);
        return Json(new { success = true });
    }

    public async Task<IActionResult> SponsorInvites()
    {
        var model = await PrepareSponsorInviteModelAsync(new SponsorInviteAdminModel());
        return View("~/Plugins/Misc.AIInterview/Views/Admin/SponsorInvites.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> SponsorInvites(SponsorInviteAdminModel model)
    {
        if (string.IsNullOrWhiteSpace(model.BulkEmails))
        {
            _notificationService.ErrorNotification(await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Invite.EmailRequired", "Email is required."));
            model = await PrepareSponsorInviteModelAsync(model);
            return View("~/Plugins/Misc.AIInterview/Views/Admin/SponsorInvites.cshtml", model);
        }

        if (model.ProductId <= 0)
        {
            _notificationService.ErrorNotification(await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Invite.ProductNotFound", "Product not found."));
            model = await PrepareSponsorInviteModelAsync(model);
            return View("~/Plugins/Misc.AIInterview/Views/Admin/SponsorInvites.cshtml", model);
        }

        var customer = await _workContext.GetCurrentCustomerAsync();
        var sponsorId = model.SponsorId.GetValueOrDefault(customer?.Id ?? 0);
        var emails = ParseEmails(model.BulkEmails);
        var validCount = 0;
        var invalidCount = 0;
        var failureMessages = new List<string>();

        foreach (var email in emails)
        {
            if (!CommonHelper.IsValidEmail(email))
            {
                invalidCount++;
                continue;
            }

            try
            {
                await _inviteService.CreateInviteAsync(sponsorId, email, model.ProductId, Math.Max(1, model.MaxAttempts), model.ExpiryDateUtc);
                validCount++;
            }
            catch (NopException ex)
            {
                failureMessages.Add(ex.Message);
            }
        }

        if (validCount == 0)
        {
            _notificationService.ErrorNotification(failureMessages.FirstOrDefault()
                ?? await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Invite.EmailInvalid", "Enter a valid email address."));
        }
        else
        {
            var template = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.BulkSuccess");
            var message = string.Format(template ?? "Successfully created {0} invites. {1} emails were invalid.", validCount, invalidCount);
            if (failureMessages.Any())
                message = $"{message} {failureMessages.Count} invite(s) failed: {string.Join("; ", failureMessages.Distinct())}";

            _notificationService.SuccessNotification(message);
        }

        model = await PrepareSponsorInviteModelAsync(model);
        return View("~/Plugins/Misc.AIInterview/Views/Admin/SponsorInvites.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> DeactivateInvite(int id)
    {
        await _inviteService.DeactivateInviteAsync(id, 0);
        _notificationService.SuccessNotification(await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Employer.Invite.Deactivated", "Invite deactivated successfully."));
        return RedirectToAction(nameof(SponsorInvites));
    }

    public async Task<IActionResult> VendorCredits(int? customerId = null)
    {
        return View("~/Plugins/Misc.AIInterview/Views/Admin/VendorCredits.cshtml", await PrepareCreditModelAsync("Plugins.Misc.AIInterview.Admin.Credits.VendorTitle", customerId, false));
    }

    [HttpPost]
    public async Task<IActionResult> VendorCredits(CreditManagementModel model)
    {
        return await HandleCreditTopUpAsync(model, "Plugins.Misc.AIInterview.Admin.Credits.VendorTitle");
    }

    public async Task<IActionResult> ApplicantCredits(int? customerId = null)
    {
        var model = await PrepareCreditModelAsync("Plugins.Misc.AIInterview.Admin.Credits.ApplicantTitle", customerId, false);
        return View("~/Plugins/Misc.AIInterview/Views/Admin/ApplicantCredits.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> ApplicantCredits(CreditManagementModel model)
    {
        return await HandleCreditTopUpAsync(model, "Plugins.Misc.AIInterview.Admin.Credits.ApplicantTitle");
    }

    [HttpPost]
    public async Task<IActionResult> ApplicantCreditActivityList(ApplicantCreditActivitySearchModel searchModel)
    {
        var model = await PrepareApplicantCreditActivityListModelAsync(searchModel);
        return Json(model);
    }

    public async Task<IActionResult> Scoreboard(ScoreboardFilterModel model)
    {
        var prepared = await PrepareScoreboardModelAsync(model);
        return View("~/Plugins/Misc.AIInterview/Views/Admin/Scoreboard.cshtml", prepared);
    }

    public async Task<IActionResult> MockPracticeSessions(MockPracticeSessionSearchModel searchModel)
    {
        searchModel ??= new MockPracticeSessionSearchModel();
        searchModel.SetGridPageSize();
        if (searchModel.PageSize <= 0)
            searchModel.SetGridPageSize(10, "10, 20, 50, 100");

        await PrepareMockPracticeSessionSearchModelAsync(searchModel);

        return View("~/Plugins/Misc.AIInterview/Views/Admin/MockPracticeSessions.cshtml", searchModel);
    }

    [HttpPost]
    public async Task<IActionResult> MockPracticeSessionsList(MockPracticeSessionSearchModel searchModel)
    {
        searchModel ??= new MockPracticeSessionSearchModel();
        await PrepareMockPracticeSessionSearchModelAsync(searchModel);

        if (_sessionRepository == null || _productRepository == null)
        {
            var emptyPagedList = new Nop.Core.PagedList<MockPracticeSessionRowModel>(new List<MockPracticeSessionRowModel>(), Math.Max(searchModel.Page - 1, 0), searchModel.PageSize > 0 ? searchModel.PageSize : 10, 0);
            return Json(await new MockPracticeSessionListModel().PrepareToGridAsync(searchModel, emptyPagedList, () => AsyncEnumerable.Empty<MockPracticeSessionRowModel>()));
        }

        if (searchModel.Length <= 0)
            searchModel.Length = searchModel.PageSize > 0 ? searchModel.PageSize : 10;

        var customerKeyword = searchModel.CustomerKeyword?.Trim();
        var productKeyword = searchModel.ProductKeyword?.Trim();
        var normalizedDifficulty = searchModel.Difficulty?.Trim();
        var normalizedStatus = searchModel.Status?.Trim();
        var (dateFromUtc, dateToExclusiveUtc) = await ConvertLocalDateRangeToUtcAsync(searchModel.DateFrom, searchModel.DateTo);

        var query =
            from session in _sessionRepository.Table
            join customer in _customerRepository.Table on session.CustomerId equals customer.Id into customerJoin
            from customer in customerJoin.DefaultIfEmpty()
            join product in _productRepository.Table on session.ProductId equals product.Id into productJoin
            from product in productJoin.DefaultIfEmpty()
            join sourceProduct in _productRepository.Table on session.SourceProductId equals sourceProduct.Id into sourceProductJoin
            from sourceProduct in sourceProductJoin.DefaultIfEmpty()
            where session.InterviewType == AIInterviewDefaults.InterviewTypeMockPractice
            select new
            {
                Session = session,
                Customer = customer,
                ProductId = session.SourceProductId > 0 ? session.SourceProductId : session.ProductId,
                ProductName = sourceProduct != null && sourceProduct.Name != null && sourceProduct.Name != string.Empty
                    ? sourceProduct.Name
                    : (product != null ? product.Name : string.Empty)
            };

        if (!string.IsNullOrWhiteSpace(customerKeyword))
        {
            query = query.Where(item =>
                (item.Customer != null && (item.Customer.FirstName ?? string.Empty).Contains(customerKeyword)) ||
                (item.Customer != null && (item.Customer.LastName ?? string.Empty).Contains(customerKeyword)) ||
                (item.Customer != null && (item.Customer.Email ?? string.Empty).Contains(customerKeyword)) ||
                (item.Customer != null && (((item.Customer.FirstName ?? string.Empty) + " " + (item.Customer.LastName ?? string.Empty)).Trim()).Contains(customerKeyword)));
        }

        if (!string.IsNullOrWhiteSpace(productKeyword))
            query = query.Where(item => (item.ProductName ?? string.Empty).Contains(productKeyword));

        if (!string.IsNullOrWhiteSpace(normalizedDifficulty))
            query = query.Where(item => (item.Session.Difficulty ?? string.Empty) == normalizedDifficulty);

        if (string.Equals(normalizedStatus, "Active", StringComparison.OrdinalIgnoreCase))
            query = query.Where(item => item.Session.IsActive && !item.Session.CompletedOnUtc.HasValue);
        else if (string.Equals(normalizedStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            query = query.Where(item => item.Session.CompletedOnUtc.HasValue);

        if (searchModel.HasResume == true)
            query = query.Where(item => item.Session.ResumeDownloadId > 0);
        else if (searchModel.HasResume == false)
            query = query.Where(item => item.Session.ResumeDownloadId <= 0);

        if (searchModel.QuestionCount.HasValue)
            query = query.Where(item => item.Session.QuestionCount == searchModel.QuestionCount.Value);

        if (searchModel.MinScore.HasValue)
            query = query.Where(item => item.Session.Score >= searchModel.MinScore.Value);

        if (searchModel.MaxScore.HasValue)
            query = query.Where(item => item.Session.Score <= searchModel.MaxScore.Value);

        if (dateFromUtc.HasValue)
            query = query.Where(item => item.Session.CreatedOnUtc >= dateFromUtc.Value);

        if (dateToExclusiveUtc.HasValue)
            query = query.Where(item => item.Session.CreatedOnUtc < dateToExclusiveUtc.Value);

        query = query
            .OrderByDescending(item => item.Session.CreatedOnUtc)
            .ThenByDescending(item => item.Session.Id);

        var totalCount = await query.CountAsync();
        var pageItems = await query
            .Skip(searchModel.Start)
            .Take(searchModel.Length)
            .ToListAsync();

        var activeStatusText = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Status.Active");
        var completedStatusText = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Status.Completed");
        var pagedList = new Nop.Core.PagedList<object>(pageItems.Cast<object>().ToList(), Math.Max(searchModel.Page - 1, 0), searchModel.PageSize, totalCount);

        var rows = new List<MockPracticeSessionRowModel>();
        foreach (var item in pageItems)
        {
            rows.Add(new MockPracticeSessionRowModel
            {
                SessionId = item.Session.Id,
                CustomerId = item.Session.CustomerId,
                CustomerName = BuildCustomerDisplayName(item.Customer),
                CustomerEmail = item.Customer?.Email ?? string.Empty,
                CustomerAdminUrl = item.Session.CustomerId > 0 ? BuildCustomerAdminUrl(item.Session.CustomerId) : string.Empty,
                ProductId = item.ProductId,
                ProductName = item.ProductName ?? string.Empty,
                Difficulty = item.Session.Difficulty ?? string.Empty,
                Status = item.Session.CompletedOnUtc.HasValue ? completedStatusText : (item.Session.IsActive ? activeStatusText : string.Empty),
                HasResume = item.Session.ResumeDownloadId > 0,
                QuestionCount = item.Session.QuestionCount,
                Score = item.Session.Score,
                SelectedInputs = BuildMockPracticeSelectedInputsSummary(item.Session.SelectedProductAttributesJson),
                CreatedOnUtc = item.Session.CreatedOnUtc,
                StartedOnUtc = item.Session.StartedOnUtc,
                CompletedOnUtc = item.Session.CompletedOnUtc,
                CreatedOn = await FormatAdminLocalDateTimeAsync(item.Session.CreatedOnUtc),
                StartedOn = await FormatAdminLocalDateTimeAsync(item.Session.StartedOnUtc),
                CompletedOn = await FormatAdminLocalDateTimeAsync(item.Session.CompletedOnUtc),
                ReportUrl = Url.RouteUrl(AIInterviewDefaults.ReportRouteName, new { sessionId = item.Session.Id }) ?? string.Empty
            });
        }

        return Json(await new MockPracticeSessionListModel().PrepareToGridAsync(searchModel, pagedList, () => rows.ToAsyncEnumerable()));
    }

    [HttpPost]
    public async Task<IActionResult> ScoreboardList(ScoreboardFilterModel searchModel)
    {
        var prepared = await PrepareScoreboardModelAsync(searchModel);
        var rows = prepared.Rows ?? new List<ScoreboardRowModel>();
        var totalCount = rows.Count;
        var pageRows = rows
            .Skip(searchModel.Start)
            .Take(searchModel.Length > 0 ? searchModel.Length : searchModel.PageSize)
            .ToList();
        var pagedList = new Nop.Core.PagedList<ScoreboardRowModel>(pageRows, searchModel.Page - 1, searchModel.PageSize, totalCount);

        return Json(await new ScoreboardListModel().PrepareToGridAsync(searchModel, pagedList, () => pageRows.ToAsyncEnumerable()));
    }

    [HttpPost]
    public async Task<IActionResult> ScoreboardExportCsv(ScoreboardFilterModel model)
    {
        var prepared = await PrepareScoreboardModelAsync(model);
        var sb = new StringBuilder();
        sb.AppendLine("SessionId,Candidate,Email,Vendor,Job,Status,Score,CompletedOnUtc,ReportUrl");

        foreach (var row in prepared.Rows)
        {
            sb.AppendLine(string.Join(",",
                row.SessionId,
                Csv(row.CandidateName),
                Csv(row.CandidateEmail),
                Csv(row.VendorName),
                Csv(row.JobTitle),
                Csv(row.Status),
                row.Score.ToString("0.##"),
                Csv(row.CompletedOnUtc?.ToString("u")),
                Csv(row.ReportUrl)));
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "aiinterview-scoreboard.csv");
    }

    public async Task<IActionResult> CandidateDetails(int sessionId, int applicationId = 0)
    {
        var session = await _sessionService.GetInterviewSessionByIdAsync(sessionId);
        if (session == null)
        {
            _notificationService.ErrorNotification("Candidate assessment session was not found.");
            return RedirectToRoute(AIInterviewDefaults.AdminScoreboardRouteName);
        }

        JobApplication application = null;
        if (session.JobApplicationId > 0)
            application = await _applicationService.GetJobApplicationByIdAsync(session.JobApplicationId);

        if (application == null && applicationId > 0)
            application = await _applicationService.GetJobApplicationByIdAsync(applicationId);

        var customer = await _customerService.GetCustomerByIdAsync(session.CustomerId);
        var product = session.ProductId > 0 ? await _productService.GetProductByIdAsync(session.ProductId) : null;
        var vendor = product?.VendorId > 0 ? await _vendorService.GetVendorByIdAsync(product.VendorId) : null;
        var turns = _interviewTurnService == null
            ? new List<InterviewTurn>()
            : ((await _interviewTurnService.GetTurnsBySessionIdAsync(session.Id)) ?? new List<InterviewTurn>())
                .OrderBy(turn => turn.SequenceNumber)
                .ToList();

        var parsedQuestionScores = ParseQuestionScores(session.QuestionScores);
        var reportSections = SplitReportSections(session.ReportData);
        var questionCount = Math.Max(session.QuestionCount, turns.Count);
        var answeredQuestionCount = turns.Count(turn => !string.IsNullOrWhiteSpace(turn.AnswerText));
        var scoredTurns = turns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score!.Value).ToList();
        var turnModels = new List<CandidateDetailsTurnModel>();
        var feedbackAttachment = session.CandidateFeedbackAttachmentDownloadId > 0 && _downloadService != null
            ? await _downloadService.GetDownloadByIdAsync(session.CandidateFeedbackAttachmentDownloadId)
            : null;
        var feedbackAttachmentName = BuildDownloadDisplayName(feedbackAttachment);
        var feedbackAttachmentUrl = feedbackAttachment != null
            ? Url.Action("DownloadFile", "Download", new { area = AreaNames.ADMIN, downloadGuid = feedbackAttachment.DownloadGuid })
            : string.Empty;

        foreach (var turn in turns)
        {
            turnModels.Add(new CandidateDetailsTurnModel
            {
                TurnId = turn.Id,
                SequenceNumber = turn.SequenceNumber,
                QuestionLabel = $"Question {turn.SequenceNumber}",
                QuestionText = turn.QuestionText,
                AnswerText = turn.AnswerText,
                Feedback = turn.Feedback,
                Score = turn.Score,
                TechnicalScore = ParseRubricScore(turn.RubricJson, "technicalScore"),
                CommunicationScore = ParseRubricScore(turn.RubricJson, "communicationScore"),
                ProfessionalismScore = ParseRubricScore(turn.RubricJson, "professionalismScore"),
                PositiveAttitudeScore = ParseRubricScore(turn.RubricJson, "positiveAttitudeScore"),
                AskedOnUtc = turn.AskedOnUtc,
                AnsweredOnUtc = turn.AnsweredOnUtc,
                AskedOn = await FormatAdminLocalDateTimeAsync(turn.AskedOnUtc, "-"),
                AnsweredOn = await FormatAdminLocalDateTimeAsync(turn.AnsweredOnUtc, "-"),
                RubricJson = turn.RubricJson,
                RawAiResponseJson = turn.RawAIResponseJson
            });
        }

        var model = new CandidateDetailsModel
        {
            SessionId = session.Id,
            ApplicationId = application?.Id ?? session.JobApplicationId,
            ProductId = session.ProductId,
            CandidateCustomerId = session.CustomerId,
            CandidateName = GetCustomerName(customer),
            CandidateEmail = customer?.Email ?? string.Empty,
            CandidatePhone = customer?.Phone ?? string.Empty,
            CandidateAdminUrl = customer != null ? BuildCustomerAdminUrl(customer.Id) : string.Empty,
            TargetRole = application?.JobTitle ?? product?.Name ?? string.Empty,
            ProductName = product?.Name ?? string.Empty,
            ProductAdminUrl = product != null ? BuildProductAdminUrl(product.Id) : string.Empty,
            VendorName = vendor?.Name ?? string.Empty,
            VendorAdminUrl = vendor != null ? BuildVendorAdminUrl(vendor.Id) : string.Empty,
            Difficulty = session.Difficulty,
            Status = await GetCandidateDetailsStatusAsync(application, session),
            StatusBadgeClass = BuildStatusBadgeClass(application?.Status, session),
            LifecycleState = BuildLifecycleState(session),
            LifecycleBadgeClass = BuildLifecycleBadgeClass(session),
            ComplianceStatus = BuildComplianceState(session, turns),
            ComplianceBadgeClass = BuildComplianceBadgeClass(session, turns),
            SystemState = BuildSystemState(session),
            SystemBadgeClass = BuildSystemBadgeClass(session),
            Score = session.Score,
            AverageQuestionScore = scoredTurns.Count > 0 ? scoredTurns.Average() : null,
            AverageTechnicalScore = CalculateAverageRubricScore(turns, "technicalScore"),
            AverageCommunicationScore = CalculateAverageRubricScore(turns, "communicationScore"),
            AverageProfessionalismScore = CalculateAverageRubricScore(turns, "professionalismScore"),
            AveragePositiveAttitudeScore = CalculateAverageRubricScore(turns, "positiveAttitudeScore"),
            QuestionCount = questionCount,
            AnsweredQuestionCount = answeredQuestionCount,
            HasRecording = !string.IsNullOrWhiteSpace(session.RecordingUrl),
            RecordingUrl = session.RecordingUrl,
            ReportUrl = Url.RouteUrl(AIInterviewDefaults.ReportRouteName, new { sessionId = session.Id }) ?? string.Empty,
            AppliedOnUtc = application?.CreatedOnUtc,
            CreatedOnUtc = session.CreatedOnUtc,
            StartedOnUtc = session.StartedOnUtc,
            CompletedOnUtc = session.CompletedOnUtc,
            AppliedOn = await FormatAdminLocalDateTimeAsync(application?.CreatedOnUtc, "-"),
            CreatedOn = await FormatAdminLocalDateTimeAsync(session.CreatedOnUtc, "-"),
            StartedOn = await FormatAdminLocalDateTimeAsync(session.StartedOnUtc, "-"),
            CompletedOn = await FormatAdminLocalDateTimeAsync(session.CompletedOnUtc, "-"),
            SummaryText = reportSections.Summary,
            FeedbackText = reportSections.Feedback,
            CandidateFeedbackIssue = session.CandidateFeedbackIssue,
            CandidateFeedbackHelpfulness = session.CandidateFeedbackHelpfulness,
            CandidateFeedbackComment = session.CandidateFeedbackComment,
            CandidateFeedbackAttachmentDownloadId = session.CandidateFeedbackAttachmentDownloadId,
            CandidateFeedbackAttachmentName = feedbackAttachmentName,
            CandidateFeedbackAttachmentUrl = feedbackAttachmentUrl,
            CandidateFeedbackSubmittedOnUtc = session.CandidateFeedbackSubmittedOnUtc,
            CandidateFeedbackSubmittedOn = await FormatAdminLocalDateTimeAsync(session.CandidateFeedbackSubmittedOnUtc, "-"),
            ReportData = session.ReportData,
            QuestionScores = session.QuestionScores,
            SessionKey = session.SessionKey,
            InternalSessionToken = MaskToken(session.Token),
            AzureMediaReference = ExtractMediaReference(session.RecordingUrl),
            ApplicationTrackingReference = application != null ? $"APP-{application.Id}" : $"SESSION-{session.Id}",
            StatusComment = application?.StatusComment ?? string.Empty,
            ParsedQuestionScores = parsedQuestionScores,
            Turns = turnModels
        };

        return View("~/Plugins/Misc.AIInterview/Areas/Admin/Views/AIInterviewAdmin/CandidateDetails.cshtml", model);
    }

    protected virtual string Csv(string value)
    {
        return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    }

    protected virtual string BuildDownloadDisplayName(Nop.Core.Domain.Media.Download download)
    {
        if (download == null)
            return string.Empty;

        var fileName = Path.GetFileName(download.Filename ?? string.Empty);
        var extension = download.Extension ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"attachment-{download.Id}";

        return !string.IsNullOrWhiteSpace(extension) && !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? $"{fileName}{extension}"
            : fileName;
    }

    protected virtual bool TryValidateCreditProductSkuMappingsJson(string json)
    {
        return CreditPurchaseService.TryParseSkuMappings(json, out _, out _);
    }

    protected virtual string PreserveSecretIfBlank(string candidateValue, string existingValue)
    {
        if (string.IsNullOrWhiteSpace(candidateValue) || string.Equals(candidateValue, SecretMask, StringComparison.Ordinal))
            return existingValue;

        return candidateValue.Trim();
    }

    protected virtual string NormalizeAzureDocumentIntelligenceModelId(string modelId)
    {
        return string.IsNullOrWhiteSpace(modelId)
            ? AIInterviewDefaults.DefaultAzureDocumentIntelligenceModelId
            : modelId.Trim();
    }

    protected virtual int NormalizeAzureDocumentIntelligenceTimeoutSeconds(int timeoutSeconds)
    {
        return timeoutSeconds > 0
            ? timeoutSeconds
            : AIInterviewDefaults.DefaultAzureDocumentIntelligenceTimeoutSeconds;
    }

    protected virtual List<string> ParseEmails(string text)
    {
        return (text ?? string.Empty)
            .Split(new[] { ',', ';', ':', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(email => email.Trim())
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    protected static IList<decimal> ParseQuestionScores(string questionScores)
    {
        if (string.IsNullOrWhiteSpace(questionScores))
            return new List<decimal>();

        try
        {
            return JsonSerializer.Deserialize<List<decimal>>(questionScores) ?? new List<decimal>();
        }
        catch
        {
            return new List<decimal>();
        }
    }

    protected static decimal? ParseRubricScore(string rubricJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rubricJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(rubricJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
                return null;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var numeric))
                return numeric;

            if (property.ValueKind == JsonValueKind.String && decimal.TryParse(property.GetString(), out var parsed))
                return parsed;
        }
        catch
        {
        }

        return null;
    }

    protected static (string Summary, string Feedback) SplitReportSections(string reportData)
    {
        if (string.IsNullOrWhiteSpace(reportData))
            return (string.Empty, string.Empty);

        var lines = reportData
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (!lines.Any())
            return (reportData, string.Empty);

        return (lines.FirstOrDefault() ?? string.Empty, lines.Skip(1).FirstOrDefault() ?? string.Empty);
    }

    protected virtual async Task<string> GetCandidateDetailsStatusAsync(JobApplication application, InterviewSession session)
    {
        if (application != null && !string.IsNullOrWhiteSpace(application.Status))
            return await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{JobApplicationStatuses.Normalize(application.Status)}");

        return session.CompletedOnUtc.HasValue ? "Completed" : session.IsActive ? "Active" : "Pending";
    }

    protected virtual decimal? CalculateAverageRubricScore(IList<InterviewTurn> turns, string propertyName)
    {
        var scores = turns.Select(turn => ParseRubricScore(turn.RubricJson, propertyName))
            .Where(score => score.HasValue)
            .Select(score => score!.Value)
            .ToList();

        return scores.Count == 0 ? null : scores.Average();
    }

    protected virtual string BuildLifecycleState(InterviewSession session)
    {
        if (session.CompletedOnUtc.HasValue)
            return "Completed";

        if (session.StartedOnUtc.HasValue || session.IsActive)
            return "In Progress";

        return "Pending";
    }

    protected virtual string BuildLifecycleBadgeClass(InterviewSession session)
    {
        if (session.CompletedOnUtc.HasValue)
            return "is-success";

        if (session.StartedOnUtc.HasValue || session.IsActive)
            return "is-warning";

        return "is-pending";
    }

    protected virtual string BuildComplianceState(InterviewSession session, IList<InterviewTurn> turns)
    {
        if (session.CompletedOnUtc.HasValue && turns.Any(turn => !string.IsNullOrWhiteSpace(turn.AnswerText)))
            return "Passed";

        if (session.StartedOnUtc.HasValue)
            return "Pending Review";

        return "Awaiting Interview";
    }

    protected virtual string BuildComplianceBadgeClass(InterviewSession session, IList<InterviewTurn> turns)
    {
        if (session.CompletedOnUtc.HasValue && turns.Any(turn => !string.IsNullOrWhiteSpace(turn.AnswerText)))
            return "is-success";

        if (session.StartedOnUtc.HasValue)
            return "is-warning";

        return "is-pending";
    }

    protected virtual string BuildSystemState(InterviewSession session)
    {
        if (session.CompletedOnUtc.HasValue && !string.IsNullOrWhiteSpace(session.ReportData))
            return "Processed";

        if (session.StartedOnUtc.HasValue)
            return "Evaluating";

        return "Pending";
    }

    protected virtual string BuildSystemBadgeClass(InterviewSession session)
    {
        if (session.CompletedOnUtc.HasValue && !string.IsNullOrWhiteSpace(session.ReportData))
            return "is-success";

        if (session.StartedOnUtc.HasValue)
            return "is-warning";

        return "is-pending";
    }

    protected virtual string BuildStatusBadgeClass(string status, InterviewSession session)
    {
        var normalizedStatus = JobApplicationStatuses.Normalize(status);
        if (normalizedStatus.Contains("shortlist", StringComparison.OrdinalIgnoreCase)
            || normalizedStatus.Contains("complete", StringComparison.OrdinalIgnoreCase)
            || normalizedStatus.Contains("success", StringComparison.OrdinalIgnoreCase)
            || session.CompletedOnUtc.HasValue)
            return "is-success";

        if (normalizedStatus.Contains("reject", StringComparison.OrdinalIgnoreCase)
            || normalizedStatus.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || normalizedStatus.Contains("suspend", StringComparison.OrdinalIgnoreCase))
            return "is-danger";

        return "is-warning";
    }

    protected virtual string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (token.Length <= 6)
            return new string('*', token.Length);

        return $"{token[..3]}...{token[^3..]}";
    }

    protected virtual string ExtractMediaReference(string recordingUrl)
    {
        if (string.IsNullOrWhiteSpace(recordingUrl))
            return "Not available";

        try
        {
            var uri = new Uri(recordingUrl, UriKind.RelativeOrAbsolute);
            var path = uri.IsAbsoluteUri ? uri.AbsolutePath : recordingUrl;
            return path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? recordingUrl;
        }
        catch
        {
            return recordingUrl;
        }
    }

    protected virtual async Task<SponsorInviteAdminModel> PrepareSponsorInviteModelAsync(SponsorInviteAdminModel model)
    {
        model ??= new SponsorInviteAdminModel();

        model.AvailableProducts = await BuildJobProductSelectListAsync(model.ProductId);
        model.AvailableSponsors = await BuildSponsorSelectListAsync(model.SponsorId);

        var invites = await _inviteService.GetSponsorInvitesAsync(0) ?? new List<SponsorInvite>();
        var products = await _productService.GetProductsByIdsAsync(invites.Select(invite => invite.ProductId).Where(id => id > 0).Distinct().ToArray()) ?? new List<Product>();
        var productLookup = products.ToDictionary(product => product.Id, product => product);
        var vendorIds = products.Where(product => product.VendorId > 0).Select(product => product.VendorId).Distinct().ToArray();
        var vendorList = await _vendorService.GetAllVendorsAsync(showHidden: true, pageSize: int.MaxValue);
        var vendors = vendorIds.Length == 0 ? new List<Vendor>() : (vendorList?.Where(vendor => vendorIds.Contains(vendor.Id)).ToList() ?? new List<Vendor>());
        var vendorLookupByProduct = vendors.ToDictionary(vendor => vendor.Id, vendor => vendor);
        var vendorLookupByCustomer = vendors.Where(vendor => vendor.PmCustomerId.HasValue)
            .ToDictionary(vendor => vendor.PmCustomerId.GetValueOrDefault(), vendor => vendor);
        var inviteAttemptCounts = new Dictionary<int, int>();
        foreach (var invite in invites)
            inviteAttemptCounts[invite.Id] = await _sessionService.GetSponsorInviteAttemptCountAsync(invite.Id);

        model.Invites = new List<SponsorInviteRowModel>();
        foreach (var invite in invites.OrderByDescending(invite => invite.CreatedOnUtc))
        {
            var attemptCount = inviteAttemptCounts.GetValueOrDefault(invite.Id);
            var product = productLookup.TryGetValue(invite.ProductId, out var foundProduct) ? foundProduct : null;
            var vendor = vendorLookupByProduct.TryGetValue(product?.VendorId ?? 0, out var foundVendor) ? foundVendor : null;
            var sponsorVendor = vendorLookupByCustomer.TryGetValue(invite.SponsorId, out var foundSponsorVendor) ? foundSponsorVendor : null;
            model.Invites.Add(new SponsorInviteRowModel
            {
                Id = invite.Id,
                SponsorId = invite.SponsorId,
                ProductId = invite.ProductId,
                ProductName = product != null ? product.Name : $"Product #{invite.ProductId}",
                ProductAdminUrl = product != null ? BuildProductAdminUrl(product.Id) : string.Empty,
                VendorName = vendor != null ? vendor.Name : (sponsorVendor != null ? sponsorVendor.Name : $"Vendor #{invite.SponsorId}"),
                VendorAdminUrl = vendor != null ? BuildVendorAdminUrl(vendor.Id) : (sponsorVendor != null ? BuildVendorAdminUrl(sponsorVendor.Id) : string.Empty),
                Email = invite.Email,
                InviteCode = invite.InviteCode,
                MaxAttempts = invite.MaxAttempts,
                ExpiryDateUtc = invite.ExpiryDateUtc,
                ExpiryDate = await FormatAdminLocalDateTimeAsync(invite.ExpiryDateUtc),
                IsActive = invite.IsActive,
                IsAccepted = invite.IsAccepted,
                IsExpired = invite.ExpiryDateUtc.HasValue && invite.ExpiryDateUtc.Value <= DateTime.UtcNow,
                CreatedOnUtc = invite.CreatedOnUtc,
                CreatedOn = await FormatAdminLocalDateTimeAsync(invite.CreatedOnUtc),
                Status = GetInviteStatus(invite, attemptCount),
                StatusText = await GetInviteStatusTextAsync(invite, attemptCount)
            });
        }

        return model;
    }

    protected virtual string GetInviteStatus(SponsorInvite invite, int attemptCount = 0)
    {
        if (invite == null)
            return string.Empty;

        if (!invite.IsActive)
            return "Plugins.Misc.AIInterview.Employer.Invite.Inactive";

        if (invite.ExpiryDateUtc.HasValue && invite.ExpiryDateUtc.Value <= DateTime.UtcNow)
            return "Plugins.Misc.AIInterview.Employer.Invite.Expired";

        if (IsInviteExhausted(invite, attemptCount))
            return "Plugins.Misc.AIInterview.Employer.Invite.Exhausted";

        if (attemptCount > 0 || invite.IsAccepted)
            return "Plugins.Misc.AIInterview.Employer.Invite.Accepted";

        return "Plugins.Misc.AIInterview.Employer.Invite.Active";
    }

    protected virtual bool IsInviteExhausted(SponsorInvite invite, int attemptCount)
    {
        if (invite == null)
            return false;

        if (invite.MaxAttempts <= 0)
            return false;

        return attemptCount >= invite.MaxAttempts;
    }

    protected virtual async Task<string> GetInviteStatusTextAsync(SponsorInvite invite, int attemptCount = 0)
    {
        var status = GetInviteStatus(invite, attemptCount);
        if (string.IsNullOrWhiteSpace(status))
            return string.Empty;

        return await _localizationService.GetResourceAsync(status);
    }

    protected virtual async Task<CreditManagementModel> PrepareCreditModelAsync(string scopeTitleResourceKey, int? customerId, bool createWallet = true)
    {
        var model = new CreditManagementModel
        {
            CustomerId = customerId ?? 0,
            ScopeTitle = await _localizationService.GetResourceAsync(scopeTitleResourceKey)
        };

        var isVendorScope = string.Equals(scopeTitleResourceKey, "Plugins.Misc.AIInterview.Admin.Credits.VendorTitle", StringComparison.OrdinalIgnoreCase);
        model.AvailableCustomers = isVendorScope
            ? await BuildVendorCustomerSelectListAsync(model.CustomerId)
            : await BuildApplicantCustomerSelectListAsync(model.CustomerId, model.CustomerName, model.CustomerEmail);

        if (!isVendorScope)
            model.ActivitySearchModel = PrepareApplicantCreditActivitySearchModel(model.ActivitySearchModel);

        if (model.CustomerId <= 0)
            return model;

        var customer = await _customerService.GetCustomerByIdAsync(model.CustomerId);
        var isValidSelectedCustomer = isVendorScope
            ? customer != null && !customer.Deleted && customer.VendorId > 0
            : customer != null && !customer.Deleted && customer.VendorId <= 0;
        if (!isValidSelectedCustomer)
        {
            model.CustomerId = 0;
            var resourceKey = isVendorScope
                ? "Plugins.Misc.AIInterview.Admin.Credits.InvalidVendorScope"
                : "Plugins.Misc.AIInterview.Admin.Credits.InvalidApplicantScope";
            var defaultMessage = isVendorScope
                ? "The selected customer is not a vendor account."
                : "The selected customer is not an applicant account.";
            _notificationService.WarningNotification(await GetLocalizedTextAsync(resourceKey, defaultMessage));
            return model;
        }

        model.CustomerName = GetCustomerName(customer);
        model.CustomerEmail = customer.Email;
        model.CustomerAdminUrl = BuildCustomerAdminUrl(customer.Id);
        if (!isVendorScope)
            model.AvailableCustomers = await BuildApplicantCustomerSelectListAsync(model.CustomerId, model.CustomerName, model.CustomerEmail);

        if (createWallet)
            await _creditService.GetOrCreateWalletAsync(model.CustomerId);

        var wallets = (await _walletRepository.GetAllAsync(query => query.Where(item => item.CustomerId == model.CustomerId)))
            .OrderBy(item => item.Id)
            .ToList();
        if (!wallets.Any())
            return model;

        if (wallets.Count > 1)
        {
            _logger.LogWarning("Multiple credit wallets detected for customer {CustomerId}. Applicant credit page is aggregating balances and ledger rows across {WalletCount} wallets.", model.CustomerId, wallets.Count);
        }

        var walletIds = wallets.Select(item => item.Id).ToArray();
        model.WalletBalance = wallets.Sum(item => item.Balance);

        model.LedgerEntries = await _ledgerRepository.Table
            .Where(entry => walletIds.Contains(entry.CreditWalletId))
            .OrderByDescending(entry => entry.CreatedOnUtc)
            .Take(20)
            .Select(entry => new CreditLedgerRowModel
            {
                CustomerId = model.CustomerId,
                CustomerName = model.CustomerName,
                CustomerAdminUrl = model.CustomerAdminUrl,
                Amount = entry.Amount,
                TransactionType = entry.TransactionType,
                Remarks = entry.Remarks,
                CreatedOnUtc = entry.CreatedOnUtc
            })
            .ToListAsync();

        foreach (var ledgerEntry in model.LedgerEntries)
            ledgerEntry.CreatedOn = await FormatAdminLocalDateTimeAsync(ledgerEntry.CreatedOnUtc);

        return model;
    }

    protected virtual ApplicantCreditActivitySearchModel PrepareApplicantCreditActivitySearchModel(ApplicantCreditActivitySearchModel searchModel)
    {
        searchModel ??= new ApplicantCreditActivitySearchModel();
        searchModel.SetGridPageSize();
        return searchModel;
    }

    protected virtual async Task<ApplicantCreditActivityListModel> PrepareApplicantCreditActivityListModelAsync(ApplicantCreditActivitySearchModel searchModel)
    {
        ArgumentNullException.ThrowIfNull(searchModel);
        var hasKeyword = !string.IsNullOrWhiteSpace(searchModel.SearchKeyword);

        var walletBalancesQuery = _walletRepository.Table
            .GroupBy(wallet => wallet.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                WalletBalance = group.Sum(wallet => wallet.Balance)
            });

        var ledgerAggregatesQuery = from entry in _ledgerRepository.Table
                                    join wallet in _walletRepository.Table on entry.CreditWalletId equals wallet.Id
                                    group entry by wallet.CustomerId
            into groupByCustomer
                                    select new
                                    {
                                        CustomerId = groupByCustomer.Key,
                                        TotalDeposited = groupByCustomer.Where(entry => entry.Amount > 0).Sum(entry => entry.Amount),
                                        TotalWithdrawn = groupByCustomer.Where(entry => entry.Amount < 0).Sum(entry => -entry.Amount),
                                        LastLedgerActivityUtc = groupByCustomer.Max(entry => (DateTime?)entry.CreatedOnUtc),
                                        LedgerCount = groupByCustomer.Count()
                                    };

        var grantAggregatesQuery = _creditPurchaseGrantRepository.Table
            .GroupBy(grant => grant.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                LastGrantActivityUtc = group.Max(grant => (DateTime?)grant.CreatedOnUtc),
                GrantCount = group.Count()
            });

        var eligibleCustomerIds = walletBalancesQuery.Where(wallet => wallet.WalletBalance > 0).Select(wallet => wallet.CustomerId)
            .Union(ledgerAggregatesQuery.Where(ledger => ledger.LedgerCount > 0).Select(ledger => ledger.CustomerId))
            .Union(grantAggregatesQuery.Where(grant => grant.GrantCount > 0).Select(grant => grant.CustomerId));

        var customerBaseQuery = _customerRepository.Table.Where(customer =>
            !customer.Deleted &&
            customer.VendorId <= 0);

        if (!hasKeyword)
        {
            customerBaseQuery =
                from customer in customerBaseQuery
                join eligibleCustomerId in eligibleCustomerIds on customer.Id equals eligibleCustomerId
                select customer;
        }

        var activityQuery =
            from customer in customerBaseQuery
            join wallet in walletBalancesQuery on customer.Id equals wallet.CustomerId into walletJoin
            from wallet in walletJoin.DefaultIfEmpty()
            join ledger in ledgerAggregatesQuery on customer.Id equals ledger.CustomerId into ledgerJoin
            from ledger in ledgerJoin.DefaultIfEmpty()
            join grant in grantAggregatesQuery on customer.Id equals grant.CustomerId into grantJoin
            from grant in grantJoin.DefaultIfEmpty()
            select new
            {
                CustomerId = customer.Id,
                customer.FirstName,
                customer.LastName,
                customer.Email,
                WalletBalance = wallet != null ? wallet.WalletBalance : 0m,
                TotalDeposited = ledger != null ? ledger.TotalDeposited : 0m,
                TotalWithdrawn = ledger != null ? ledger.TotalWithdrawn : 0m,
                LastCreditActivityUtc = ledger != null && grant != null
                    ? (ledger.LastLedgerActivityUtc >= grant.LastGrantActivityUtc ? ledger.LastLedgerActivityUtc : grant.LastGrantActivityUtc)
                    : (ledger != null ? ledger.LastLedgerActivityUtc : (grant != null ? grant.LastGrantActivityUtc : null))
            };

        if (hasKeyword)
        {
            var keyword = searchModel.SearchKeyword.Trim();
            activityQuery = activityQuery.Where(item =>
                (item.Email ?? string.Empty).Contains(keyword) ||
                (item.FirstName ?? string.Empty).Contains(keyword) ||
                (item.LastName ?? string.Empty).Contains(keyword) ||
                (((item.FirstName ?? string.Empty) + " " + (item.LastName ?? string.Empty)).Trim()).Contains(keyword));
        }

        activityQuery = activityQuery
            .OrderByDescending(item => item.LastCreditActivityUtc)
            .ThenBy(item => item.CustomerId);

        var totalCount = await activityQuery.CountAsync();
        var pageItems = await activityQuery
            .Skip(searchModel.Start)
            .Take(searchModel.Length)
            .ToListAsync();

        var pagedList = new Nop.Core.PagedList<object>(pageItems.Cast<object>().ToList(), searchModel.Page - 1, searchModel.PageSize, totalCount);

        var rows = new List<ApplicantCreditActivityRowModel>(pageItems.Count);

        foreach (var item in pageItems)
        {
            rows.Add(new ApplicantCreditActivityRowModel
            {
                CustomerId = item.CustomerId,
                CustomerName = $"{item.FirstName} {item.LastName}".Trim(),
                CustomerEmail = item.Email,
                CustomerAdminUrl = BuildCustomerAdminUrl(item.CustomerId),
                ViewLedgerUrl = Url.RouteUrl(AIInterviewDefaults.AdminApplicantCreditsRouteName, new { customerId = item.CustomerId }),
                WalletBalance = item.WalletBalance,
                TotalDeposited = item.TotalDeposited,
                TotalWithdrawn = item.TotalWithdrawn,
                LastCreditActivityUtc = item.LastCreditActivityUtc,
                LastCreditActivity = await FormatAdminLocalDateTimeAsync(item.LastCreditActivityUtc)
            });
        }

        return await new ApplicantCreditActivityListModel().PrepareToGridAsync<ApplicantCreditActivityListModel, ApplicantCreditActivityRowModel, object>(searchModel, pagedList, () =>
        {
            return rows.ToAsyncEnumerable();
        });
    }

    protected virtual async Task<IActionResult> HandleCreditTopUpAsync(CreditManagementModel model, string scopeTitleResourceKey)
    {
        var viewPath = string.Equals(scopeTitleResourceKey, "Plugins.Misc.AIInterview.Admin.Credits.VendorTitle", StringComparison.OrdinalIgnoreCase)
            ? "~/Plugins/Misc.AIInterview/Views/Admin/VendorCredits.cshtml"
            : "~/Plugins/Misc.AIInterview/Views/Admin/ApplicantCredits.cshtml";

        if (model.CustomerId <= 0)
        {
            _notificationService.ErrorNotification(await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Credits.CustomerRequired", "Customer is required."));
            return View(viewPath, await PrepareCreditModelAsync(scopeTitleResourceKey, model.CustomerId, false));
        }

        var customer = await _customerService.GetCustomerByIdAsync(model.CustomerId);
        if (customer == null || customer.Deleted)
        {
            _notificationService.ErrorNotification(await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Credits.CustomerRequired", "Customer is required."));
            return View(viewPath, await PrepareCreditModelAsync(scopeTitleResourceKey, model.CustomerId, false));
        }

        var isVendorScope = string.Equals(scopeTitleResourceKey, "Plugins.Misc.AIInterview.Admin.Credits.VendorTitle", StringComparison.OrdinalIgnoreCase);
        if (isVendorScope && customer.VendorId <= 0)
        {
            _notificationService.ErrorNotification(await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Credits.InvalidVendorScope", "The selected customer is not a vendor account."));
            return View(viewPath, await PrepareCreditModelAsync(scopeTitleResourceKey, model.CustomerId, false));
        }

        if (!isVendorScope && customer.VendorId > 0)
        {
            _notificationService.ErrorNotification(await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Credits.InvalidApplicantScope", "The selected customer is not an applicant account."));
            return View(viewPath, await PrepareCreditModelAsync(scopeTitleResourceKey, model.CustomerId, false));
        }

        if (model.Amount <= 0)
        {
            _notificationService.ErrorNotification(await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.TopUp.InvalidAmount", "Invalid top-up amount."));
            return View(viewPath, await PrepareCreditModelAsync(scopeTitleResourceKey, model.CustomerId, false));
        }

        var remarks = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.TopUp.Remarks");
        await _creditService.AddCreditAsync(model.CustomerId, model.Amount, remarks);
        if (_creditDepositNotificationService != null)
        {
            await _creditDepositNotificationService.SendCreditDepositedNotificationAsync(new CreditDepositNotificationRequest
            {
                CustomerId = model.CustomerId,
                CreditsDeposited = model.Amount,
                DepositSource = CreditDepositSources.ViaAdminTopUp,
                Remarks = remarks
            });
        }
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.TopUp.Success"));

        return View(viewPath, await PrepareCreditModelAsync(scopeTitleResourceKey, model.CustomerId, true));
    }
    protected virtual async Task<ScoreboardFilterModel> PrepareScoreboardModelAsync(ScoreboardFilterModel filter)
    {
        filter ??= new ScoreboardFilterModel();
        filter.SetGridPageSize();
        filter.AvailableStatuses = BuildStatusSelectList(filter.Status);
        var (startDateUtc, endDateUtcExclusive) = await ConvertLocalDateRangeToUtcAsync(filter.StartDate, filter.EndDate);

        var applications = await _applicationService.GetApplicationsAsync(pageSize: int.MaxValue) ?? new Nop.Core.PagedList<JobApplication>(new List<JobApplication>(), 0, 1, 1);
        var filteredApplications = applications.AsEnumerable();

        var rows = new List<ScoreboardRowModel>();
        var productIds = filteredApplications.Select(application => application.ProductId).Distinct().Where(id => id > 0).ToArray();
        var products = await _productService.GetProductsByIdsAsync(productIds) ?? new List<Product>();
        var vendors = new Dictionary<int, Vendor>();
        foreach (var vendorId in products.Where(product => product.VendorId > 0).Select(product => product.VendorId).Distinct())
        {
            var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
            if (vendor != null)
                vendors[vendorId] = vendor;
        }

        var customers = await _customerService.GetCustomersByIdsAsync(filteredApplications.Select(application => application.CustomerId).Distinct().ToArray()) ?? new List<Customer>();
        var customerLookup = customers.ToDictionary(customer => customer.Id, customer => customer);

        foreach (var application in filteredApplications)
        {
            var customer = customerLookup.GetValueOrDefault(application.CustomerId);
            var sessions = await _sessionService.GetSessionsByCustomerIdAsync(application.CustomerId);
            var session = sessions
                .Where(item => item.ProductId == application.ProductId || (item.JobApplicationId == application.Id))
                .Where(item => item.CompletedOnUtc.HasValue)
                .OrderByDescending(item => item.CompletedOnUtc)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();

            var product = products.FirstOrDefault(item => item.Id == application.ProductId);
            var vendor = product != null && product.VendorId > 0 && vendors.TryGetValue(product.VendorId, out var foundVendor) ? foundVendor : null;

            var row = new ScoreboardRowModel
            {
                SessionId = session?.Id ?? 0,
                ApplicationId = application.Id,
                ProductId = application.ProductId,
                VendorId = product?.VendorId ?? 0,
                CandidateCustomerId = application.CustomerId,
                CandidateName = GetCustomerName(customer),
                CandidateEmail = customer?.Email ?? string.Empty,
                CandidateAdminUrl = customer != null ? BuildCustomerAdminUrl(customer.Id) : string.Empty,
                VendorName = vendor?.Name ?? string.Empty,
                VendorAdminUrl = vendor != null ? BuildVendorAdminUrl(vendor.Id) : string.Empty,
                JobTitle = application.JobTitle,
                ProductAdminUrl = product != null ? BuildProductAdminUrl(product.Id) : string.Empty,
                Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{JobApplicationStatuses.Normalize(application.Status)}"),
                Score = session?.Score ?? 0,
                CompletedOnUtc = session?.CompletedOnUtc,
                CompletedOn = await FormatAdminLocalDateTimeAsync(session?.CompletedOnUtc, "-"),
                ReportUrl = session != null ? Url.RouteUrl(AIInterviewDefaults.ReportRouteName, new { sessionId = session.Id }) : string.Empty
            };

            rows.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(filter.Candidate))
            rows = rows.Where(row => row.CandidateName.Contains(filter.Candidate, StringComparison.OrdinalIgnoreCase) ||
                row.CandidateEmail.Contains(filter.Candidate, StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(filter.JobPosting))
            rows = rows.Where(row => (row.JobTitle ?? string.Empty).Contains(filter.JobPosting, StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var normalizedStatus = JobApplicationStatuses.Normalize(filter.Status);
            var localizedStatus = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{normalizedStatus}");
            rows = rows.Where(row => string.Equals(row.Status, localizedStatus, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (startDateUtc.HasValue)
            rows = rows.Where(row => row.CompletedOnUtc.HasValue ? row.CompletedOnUtc.Value >= startDateUtc.Value : true).ToList();

        if (endDateUtcExclusive.HasValue)
            rows = rows.Where(row => row.CompletedOnUtc.HasValue ? row.CompletedOnUtc.Value < endDateUtcExclusive.Value : true).ToList();

        if (!string.IsNullOrWhiteSpace(filter.Vendor))
            rows = rows.Where(row => row.VendorName.Contains(filter.Vendor, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filter.MinScore.HasValue)
            rows = rows.Where(row => row.Score >= filter.MinScore.Value).ToList();

        if (filter.MaxScore.HasValue)
            rows = rows.Where(row => row.Score <= filter.MaxScore.Value).ToList();

        filter.Rows = rows.OrderByDescending(row => row.CompletedOnUtc ?? DateTime.MinValue).ToList();
        return filter;
    }

    protected virtual string GetCustomerName(Customer customer)
    {
        if (customer == null)
            return string.Empty;

        return $"{customer.FirstName} {customer.LastName}".Trim();
    }

    protected virtual async Task<AiServiceSettingsModel> PrepareAiServiceModelAsync(AiServiceSettingsModel model = null)
    {
        var aiInterviewSettings = await _settingService.LoadSettingAsync<AIInterviewSettings>() ?? _aiInterviewSettings;
        var mockAIInterviewSettings = await _settingService.LoadSettingAsync<MockAIInterviewSettings>() ?? _mockAIInterviewSettings;

        model ??= new AiServiceSettingsModel
        {
            UseMockResponses = mockAIInterviewSettings.UseMockResponses,
            ApiKey = aiInterviewSettings.ApiKey,
            Model = aiInterviewSettings.Model,
            Prompt = aiInterviewSettings.Prompt,
            ServiceSettings = aiInterviewSettings.ServiceSettings,
            CreditProductSkuMappingsJson = aiInterviewSettings.CreditProductSkuMappingsJson,
            CreditPurchasePageUrl = aiInterviewSettings.CreditPurchasePageUrl,
            AzureOpenAiEndpointUrl = aiInterviewSettings.AzureOpenAiEndpointUrl,
            AzureOpenAiApiKey = aiInterviewSettings.AzureOpenAiApiKey,
            AzureOpenAiDeploymentOrModel = aiInterviewSettings.AzureOpenAiDeploymentOrModel,
            AzureSpeechKey = aiInterviewSettings.AzureSpeechKey,
            AzureSpeechRegion = aiInterviewSettings.AzureSpeechRegion,
            AzureDocumentIntelligenceEndpointUrl = aiInterviewSettings.AzureDocumentIntelligenceEndpointUrl,
            AzureDocumentIntelligenceApiKey = aiInterviewSettings.AzureDocumentIntelligenceApiKey,
            AzureDocumentIntelligenceModelId = NormalizeAzureDocumentIntelligenceModelId(aiInterviewSettings.AzureDocumentIntelligenceModelId),
            AzureDocumentIntelligenceTimeoutSeconds = NormalizeAzureDocumentIntelligenceTimeoutSeconds(aiInterviewSettings.AzureDocumentIntelligenceTimeoutSeconds),
            TrackAzureOpenAiUsage = aiInterviewSettings.TrackAzureOpenAiUsage,
            TrackAzureSpeechUsage = aiInterviewSettings.TrackAzureSpeechUsage,
            CalculateAzureCostPerInterview = aiInterviewSettings.CalculateAzureCostPerInterview,
            AzureOpenAiPromptTokenPricePerThousand = aiInterviewSettings.AzureOpenAiPromptTokenPricePerThousand,
            AzureOpenAiCompletionTokenPricePerThousand = aiInterviewSettings.AzureOpenAiCompletionTokenPricePerThousand,
            AzureSpeechRecognitionPricePerHour = aiInterviewSettings.AzureSpeechRecognitionPricePerHour,
            AzureSpeechSynthesisPricePerThousandCharacters = aiInterviewSettings.AzureSpeechSynthesisPricePerThousandCharacters,
            AzureUsageCurrencyCode = aiInterviewSettings.AzureUsageCurrencyCode,
            AzureBlobStorageContainerUrl = aiInterviewSettings.AzureBlobStorageContainerUrl,
            AzureBlobStorageSasToken = aiInterviewSettings.AzureBlobStorageSasToken
        };

        model.Provider = AzureOpenAiProviderValue;
        model.AzureDocumentIntelligenceModelId = NormalizeAzureDocumentIntelligenceModelId(model.AzureDocumentIntelligenceModelId);
        model.AzureDocumentIntelligenceTimeoutSeconds = NormalizeAzureDocumentIntelligenceTimeoutSeconds(model.AzureDocumentIntelligenceTimeoutSeconds);
        model.AvailableProviders = BuildProviderSelectList(model.Provider);
        return model;
    }

    protected virtual IList<SelectListItem> BuildProviderSelectList(string selectedProvider)
    {
        return new List<SelectListItem>
        {
            new() { Text = AzureOpenAiProviderValue, Value = AzureOpenAiProviderValue, Selected = string.Equals(selectedProvider, AzureOpenAiProviderValue, StringComparison.OrdinalIgnoreCase) }
        };
    }

    protected virtual async Task<IList<SelectListItem>> BuildJobProductSelectListAsync(int selectedProductId)
    {
        var products = await _productService.SearchProductsAsync(pageSize: int.MaxValue, showHidden: true) ?? new Nop.Core.PagedList<Product>(new List<Product>(), 0, 1, 1);
        var jobProducts = new List<Product>();

        foreach (var product in products)
        {
            if (_jobRequirementService == null || await _jobRequirementService.IsJobProductAsync(product))
            {
                if (_jobProductAccessService != null && !await _jobProductAccessService.CanAcceptJobApplicationsAsync(product))
                    continue;

                jobProducts.Add(product);
            }
        }

        return jobProducts
            .OrderBy(product => product.Name)
            .Select(product => new SelectListItem
            {
                Text = $"{product.Name} (ID: {product.Id})",
                Value = product.Id.ToString(),
                Selected = product.Id == selectedProductId
            })
            .ToList();
    }

    protected virtual async Task<IList<SelectListItem>> BuildSponsorSelectListAsync(int? selectedSponsorId)
    {
        var vendors = await _vendorService.GetAllVendorsAsync(showHidden: true, pageSize: int.MaxValue) ?? new Nop.Core.PagedList<Vendor>(new List<Vendor>(), 0, 1, 1);
        return vendors
            .Where(vendor => vendor.PmCustomerId.HasValue)
            .OrderBy(vendor => vendor.Name)
            .Select(vendor => new SelectListItem
            {
                Text = string.IsNullOrWhiteSpace(vendor.Email)
                    ? $"{vendor.Name} (Customer ID: {vendor.PmCustomerId})"
                    : $"{vendor.Name} ({vendor.Email}) - Customer ID: {vendor.PmCustomerId}",
                Value = vendor.PmCustomerId.GetValueOrDefault().ToString(),
                Selected = selectedSponsorId.HasValue && vendor.PmCustomerId == selectedSponsorId
            })
            .ToList();
    }

    protected virtual async Task<IList<SelectListItem>> BuildVendorCustomerSelectListAsync(int selectedCustomerId)
    {
        var vendors = await _vendorService.GetAllVendorsAsync(showHidden: true, pageSize: int.MaxValue) ?? new Nop.Core.PagedList<Vendor>(new List<Vendor>(), 0, 1, 1);
        var items = vendors
            .Where(vendor => vendor.PmCustomerId.HasValue)
            .OrderBy(vendor => vendor.Name)
            .Select(vendor => new SelectListItem
            {
                Text = string.IsNullOrWhiteSpace(vendor.Email)
                    ? $"{vendor.Name} (Customer ID: {vendor.PmCustomerId})"
                    : $"{vendor.Name} ({vendor.Email}) - Customer ID: {vendor.PmCustomerId}",
                Value = vendor.PmCustomerId.GetValueOrDefault().ToString(),
                Selected = vendor.PmCustomerId == selectedCustomerId
            })
            .ToList();

        items.Insert(0, new SelectListItem
        {
            Text = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Credits.SelectVendor"),
            Value = "0",
            Selected = selectedCustomerId <= 0
        });

        return items;
    }

    protected virtual async Task<IList<SelectListItem>> BuildApplicantCustomerSelectListAsync(int selectedCustomerId, string selectedCustomerName, string selectedCustomerEmail)
    {
        var items = new List<SelectListItem>();

        items.Insert(0, new SelectListItem
        {
            Text = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Credits.SelectApplicant"),
            Value = "0",
            Selected = selectedCustomerId <= 0
        });

        if (selectedCustomerId > 0)
        {
            var displayText = string.IsNullOrWhiteSpace(selectedCustomerEmail)
                ? $"{selectedCustomerName} (Customer ID: {selectedCustomerId})"
                : $"{selectedCustomerName} ({selectedCustomerEmail}) - Customer ID: {selectedCustomerId}";
            items.Add(new SelectListItem
            {
                Text = displayText.Trim(),
                Value = selectedCustomerId.ToString(),
                Selected = true
            });
        }

        return items;
    }

    protected virtual IList<SelectListItem> BuildStatusSelectList(string selectedStatus)
    {
        var items = new List<SelectListItem>
        {
            new() { Text = string.Empty, Value = string.Empty, Selected = string.IsNullOrWhiteSpace(selectedStatus) }
        };

        items.AddRange(JobApplicationStatuses.All.Select(status => new SelectListItem
        {
            Text = status,
            Value = status,
            Selected = string.Equals(status, selectedStatus, StringComparison.OrdinalIgnoreCase)
        }));

        return items;
    }

    protected virtual async Task PrepareMockPracticeSessionSearchModelAsync(MockPracticeSessionSearchModel searchModel)
    {
        if (searchModel == null)
            return;

        searchModel.AvailableStatuses = await BuildMockPracticeStatusSelectListAsync(searchModel.Status);
        searchModel.AvailableHasResumeOptions = await BuildMockPracticeHasResumeSelectListAsync(searchModel.HasResume);
        searchModel.AvailableDifficulties = await BuildMockPracticeDifficultySelectListAsync(searchModel.Difficulty);
    }

    protected virtual async Task<IList<SelectListItem>> BuildMockPracticeStatusSelectListAsync(string selectedStatus)
    {
        return new List<SelectListItem>
        {
            new() { Text = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Status.All"), Value = string.Empty, Selected = string.IsNullOrWhiteSpace(selectedStatus) },
            new() { Text = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Status.Active"), Value = "Active", Selected = string.Equals(selectedStatus, "Active", StringComparison.OrdinalIgnoreCase) },
            new() { Text = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Status.Completed"), Value = "Completed", Selected = string.Equals(selectedStatus, "Completed", StringComparison.OrdinalIgnoreCase) }
        };
    }

    protected virtual async Task<IList<SelectListItem>> BuildMockPracticeHasResumeSelectListAsync(bool? selectedValue)
    {
        return new List<SelectListItem>
        {
            new() { Text = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.HasResume.All"), Value = string.Empty, Selected = !selectedValue.HasValue },
            new() { Text = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.HasResume.Yes"), Value = bool.TrueString, Selected = selectedValue == true },
            new() { Text = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.HasResume.No"), Value = bool.FalseString, Selected = selectedValue == false }
        };
    }

    protected virtual async Task<IList<SelectListItem>> BuildMockPracticeDifficultySelectListAsync(string selectedDifficulty)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Low",
            "Medium",
            "Advanced"
        };

        foreach (var difficultyValue in AIInterviewDefaults.InterviewDifficultyValues)
            values.Add(difficultyValue);

        if (_sessionRepository != null)
        {
            var existingValues = await _sessionRepository.Table
                .Where(session => session.InterviewType == AIInterviewDefaults.InterviewTypeMockPractice && session.Difficulty != null && session.Difficulty != string.Empty)
                .Select(session => session.Difficulty)
                .Distinct()
                .ToListAsync();

            foreach (var existingValue in existingValues)
                values.Add(existingValue);
        }

        var items = new List<SelectListItem>
        {
            new() { Text = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Difficulty.All"), Value = string.Empty, Selected = string.IsNullOrWhiteSpace(selectedDifficulty) }
        };

        items.AddRange(values
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(value => new SelectListItem
            {
                Text = value,
                Value = value,
                Selected = string.Equals(value, selectedDifficulty, StringComparison.OrdinalIgnoreCase)
            }));

        return items;
    }

    protected virtual string BuildMockPracticeSessionStatus(InterviewSession session)
    {
        if (session?.CompletedOnUtc.HasValue == true)
            return "Completed";

        return session?.IsActive == true ? "Active" : string.Empty;
    }

    protected virtual string BuildCustomerDisplayName(Customer customer)
    {
        if (customer == null)
            return string.Empty;

        var fullName = $"{customer.FirstName} {customer.LastName}".Trim();
        return !string.IsNullOrWhiteSpace(fullName) ? fullName : customer.Email ?? string.Empty;
    }

    protected virtual string BuildMockPracticeSelectedInputsSummary(string selectedProductAttributesJson, int maxLength = 160)
    {
        if (string.IsNullOrWhiteSpace(selectedProductAttributesJson))
            return string.Empty;

        try
        {
            var snapshot = JsonSerializer.Deserialize<SelectedProductAttributesSummarySnapshot>(selectedProductAttributesJson);
            var pairs = snapshot?.Attributes?
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute?.AttributeName) && !string.IsNullOrWhiteSpace(attribute.Value))
                .Select(attribute => $"{attribute.AttributeName.Trim()}: {attribute.Value.Trim()}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pairs == null || pairs.Count == 0)
                return string.Empty;

            var summary = string.Join("; ", pairs);
            if (summary.Length <= maxLength)
                return summary;

            return summary.Substring(0, Math.Max(0, maxLength - 3)).TrimEnd() + "...";
        }
        catch
        {
            return string.Empty;
        }
    }

    protected virtual string BuildProductAdminUrl(int productId)
    {
        return productId > 0 ? Url.Action("Edit", "Product", new { area = AreaNames.ADMIN, id = productId }) : string.Empty;
    }

    protected virtual string BuildVendorAdminUrl(int vendorId)
    {
        return vendorId > 0 ? Url.Action("Edit", "Vendor", new { area = AreaNames.ADMIN, id = vendorId }) : string.Empty;
    }

    protected virtual string BuildCustomerAdminUrl(int customerId)
    {
        return customerId > 0 ? Url.Action("Edit", "Customer", new { area = AreaNames.ADMIN, id = customerId }) : string.Empty;
    }
}
