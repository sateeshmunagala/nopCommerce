using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
using Nop.Services.Messages;
using Nop.Services.Vendors;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Models.Extensions;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class AIInterviewAdminController : BasePluginController
{
    private const string AzureOpenAiProviderValue = "Azure OpenAI";

    private sealed record CreditWalletSnapshot(int Id, int CustomerId, decimal Balance);
    private sealed record CreditLedgerSnapshot(int CustomerId, decimal Amount, DateTime CreatedOnUtc);
    private sealed record CreditGrantSnapshot(int CustomerId, DateTime CreatedOnUtc);
    private sealed record ApplicantCreditActivityProjection(int CustomerId, string FirstName, string LastName, string Email, decimal WalletBalance, decimal TotalDeposited, decimal TotalWithdrawn, DateTime? LastCreditActivityUtc);

    private readonly ICreditService _creditService;
    private readonly ISponsorInviteService _inviteService;
    private readonly IApplicationService _applicationService;
    private readonly IInterviewSessionService _sessionService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly IVendorService _vendorService;
    private readonly IJobRequirementService _jobRequirementService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AIInterviewAdminController> _logger;
    private readonly IWorkContext _workContext;
    private readonly ISettingService _settingService;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<CreditWallet> _walletRepository;
    private readonly IRepository<CreditLedgerEntry> _ledgerRepository;
    private readonly IRepository<CreditPurchaseGrant> _creditPurchaseGrantRepository;
    private readonly AIInterviewSettings _aiInterviewSettings;
    private readonly MockAIInterviewSettings _mockAIInterviewSettings;

    public AIInterviewAdminController(ICreditService creditService,
        ISponsorInviteService inviteService,
        IApplicationService applicationService,
        IInterviewSessionService sessionService,
        ICustomerService customerService,
        IProductService productService,
        IVendorService vendorService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        ILogger<AIInterviewAdminController> logger,
        IWorkContext workContext,
        ISettingService settingService,
        IRepository<Customer> customerRepository,
        IRepository<CreditWallet> walletRepository,
        IRepository<CreditLedgerEntry> ledgerRepository,
        IRepository<CreditPurchaseGrant> creditPurchaseGrantRepository,
        AIInterviewSettings aiInterviewSettings,
        MockAIInterviewSettings mockAIInterviewSettings,
        IJobRequirementService jobRequirementService = null)
    {
        _creditService = creditService;
        _inviteService = inviteService;
        _applicationService = applicationService;
        _sessionService = sessionService;
        _customerService = customerService;
        _productService = productService;
        _vendorService = vendorService;
        _jobRequirementService = jobRequirementService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _logger = logger;
        _workContext = workContext;
        _settingService = settingService;
        _customerRepository = customerRepository;
        _walletRepository = walletRepository;
        _ledgerRepository = ledgerRepository;
        _creditPurchaseGrantRepository = creditPurchaseGrantRepository;
        _aiInterviewSettings = aiInterviewSettings;
        _mockAIInterviewSettings = mockAIInterviewSettings;
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
            _mockAIInterviewSettings.UseMockResponses = settingsModel.UseMockResponses;
            await _settingService.SaveSettingAsync(_mockAIInterviewSettings);

            _aiInterviewSettings.Provider = AzureOpenAiProviderValue;
            _aiInterviewSettings.ApiKey = settingsModel.ApiKey;
            _aiInterviewSettings.Model = settingsModel.Model;
            _aiInterviewSettings.Prompt = settingsModel.Prompt;
            _aiInterviewSettings.ServiceSettings = settingsModel.ServiceSettings;
            _aiInterviewSettings.CreditProductSkuMappingsJson = settingsModel.CreditProductSkuMappingsJson;
            _aiInterviewSettings.CreditPurchasePageUrl = settingsModel.CreditPurchasePageUrl;
            _aiInterviewSettings.AzureOpenAiEndpointUrl = settingsModel.AzureOpenAiEndpointUrl;
            _aiInterviewSettings.AzureOpenAiApiKey = settingsModel.AzureOpenAiApiKey;
            _aiInterviewSettings.AzureOpenAiDeploymentOrModel = settingsModel.AzureOpenAiDeploymentOrModel;
            _aiInterviewSettings.AzureSpeechKey = settingsModel.AzureSpeechKey;
            _aiInterviewSettings.AzureSpeechRegion = settingsModel.AzureSpeechRegion;
            _aiInterviewSettings.AzureBlobStorageContainerUrl = settingsModel.AzureBlobStorageContainerUrl;
            _aiInterviewSettings.AzureBlobStorageSasToken = settingsModel.AzureBlobStorageSasToken;
            await _settingService.SaveSettingAsync(_aiInterviewSettings);
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

    public async Task<IActionResult> ApplicantCredits(int? customerId = null, int? loadCustomerId = null, string loadCustomerEmail = null)
    {
        customerId = await ResolveApplicantCustomerIdAsync(customerId, loadCustomerId, loadCustomerEmail);
        var model = await PrepareCreditModelAsync("Plugins.Misc.AIInterview.Admin.Credits.ApplicantTitle", customerId, false);
        model.LoadCustomerId = loadCustomerId.GetValueOrDefault() > 0 ? loadCustomerId.Value : customerId ?? 0;
        model.LoadCustomerEmail = loadCustomerEmail ?? string.Empty;
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

    protected virtual string Csv(string value)
    {
        return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    }

    protected virtual bool TryValidateCreditProductSkuMappingsJson(string json)
    {
        return CreditPurchaseService.TryParseSkuMappings(json, out _, out _);
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
                IsActive = invite.IsActive,
                IsAccepted = invite.IsAccepted,
                IsExpired = invite.ExpiryDateUtc.HasValue && invite.ExpiryDateUtc.Value <= DateTime.UtcNow,
                CreatedOnUtc = invite.CreatedOnUtc,
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

        var activityQuery =
            from customer in _customerRepository.Table
            join eligibleCustomerId in eligibleCustomerIds on customer.Id equals eligibleCustomerId
            join wallet in walletBalancesQuery on customer.Id equals wallet.CustomerId into walletJoin
            from wallet in walletJoin.DefaultIfEmpty()
            join ledger in ledgerAggregatesQuery on customer.Id equals ledger.CustomerId into ledgerJoin
            from ledger in ledgerJoin.DefaultIfEmpty()
            join grant in grantAggregatesQuery on customer.Id equals grant.CustomerId into grantJoin
            from grant in grantJoin.DefaultIfEmpty()
            where !customer.Deleted
                  && customer.VendorId <= 0
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

        if (searchModel.SearchCustomerId > 0)
            activityQuery = activityQuery.Where(item => item.CustomerId == searchModel.SearchCustomerId);

        if (!string.IsNullOrWhiteSpace(searchModel.SearchKeyword))
        {
            var keyword = searchModel.SearchKeyword.Trim();
            activityQuery = activityQuery.Where(item =>
                (item.Email ?? string.Empty).Contains(keyword) ||
                (item.FirstName ?? string.Empty).Contains(keyword) ||
                (item.LastName ?? string.Empty).Contains(keyword));
        }

        if (searchModel.SearchHasPositiveBalanceOnly)
            activityQuery = activityQuery.Where(item => item.WalletBalance > 0);

        if (searchModel.SearchActivityDateFromUtc.HasValue)
            activityQuery = activityQuery.Where(item => item.LastCreditActivityUtc.HasValue && item.LastCreditActivityUtc.Value >= searchModel.SearchActivityDateFromUtc.Value);

        if (searchModel.SearchActivityDateToUtc.HasValue)
        {
            var endDateUtc = searchModel.SearchActivityDateToUtc.Value;
            if (endDateUtc.TimeOfDay == TimeSpan.Zero)
                endDateUtc = endDateUtc.Date.AddDays(1).AddTicks(-1);

            activityQuery = activityQuery.Where(item => item.LastCreditActivityUtc.HasValue && item.LastCreditActivityUtc.Value <= endDateUtc);
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

        return await new ApplicantCreditActivityListModel().PrepareToGridAsync(searchModel, pagedList, () =>
        {
            return pageItems.ToAsyncEnumerable().Select(item => new ApplicantCreditActivityRowModel
            {
                CustomerId = item.CustomerId,
                CustomerName = $"{item.FirstName} {item.LastName}".Trim(),
                CustomerEmail = item.Email,
                CustomerAdminUrl = BuildCustomerAdminUrl(item.CustomerId),
                ViewLedgerUrl = Url.RouteUrl(AIInterviewDefaults.AdminApplicantCreditsRouteName, new { customerId = item.CustomerId }),
                WalletBalance = item.WalletBalance,
                TotalDeposited = item.TotalDeposited,
                TotalWithdrawn = item.TotalWithdrawn,
                LastCreditActivityUtc = item.LastCreditActivityUtc
            });
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

        await _creditService.AddCreditAsync(model.CustomerId, model.Amount, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.TopUp.Remarks"));
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.TopUp.Success"));

        return View(viewPath, await PrepareCreditModelAsync(scopeTitleResourceKey, model.CustomerId, true));
    }

    protected virtual async Task<int?> ResolveApplicantCustomerIdAsync(int? customerId, int? loadCustomerId, string loadCustomerEmail)
    {
        if (loadCustomerId.GetValueOrDefault() > 0)
            return loadCustomerId.Value;

        if (!string.IsNullOrWhiteSpace(loadCustomerEmail))
        {
            var customer = await _customerService.GetCustomerByEmailAsync(loadCustomerEmail.Trim());
            if (customer != null)
                return customer.Id;
        }

        return customerId.GetValueOrDefault() > 0 ? customerId : null;
    }

    protected virtual async Task<ScoreboardFilterModel> PrepareScoreboardModelAsync(ScoreboardFilterModel filter)
    {
        filter ??= new ScoreboardFilterModel();
        filter.AvailableStatuses = BuildStatusSelectList(filter.Status);

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

        if (filter.StartDate.HasValue)
            rows = rows.Where(row => row.CompletedOnUtc.HasValue ? row.CompletedOnUtc.Value >= filter.StartDate.Value : true).ToList();

        if (filter.EndDate.HasValue)
            rows = rows.Where(row => row.CompletedOnUtc.HasValue ? row.CompletedOnUtc.Value <= filter.EndDate.Value : true).ToList();

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
            AzureBlobStorageContainerUrl = aiInterviewSettings.AzureBlobStorageContainerUrl,
            AzureBlobStorageSasToken = aiInterviewSettings.AzureBlobStorageSasToken
        };

        model.Provider = AzureOpenAiProviderValue;
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
                jobProducts.Add(product);
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
