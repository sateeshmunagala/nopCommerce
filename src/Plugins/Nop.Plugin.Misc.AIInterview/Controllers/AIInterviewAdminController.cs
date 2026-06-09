using System.Text;
using Microsoft.AspNetCore.Mvc;
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
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class AIInterviewAdminController : BasePluginController
{
    private readonly ICreditService _creditService;
    private readonly ISponsorInviteService _inviteService;
    private readonly IApplicationService _applicationService;
    private readonly IInterviewSessionService _sessionService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly IVendorService _vendorService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IWorkContext _workContext;
    private readonly ISettingService _settingService;
    private readonly IRepository<CreditWallet> _walletRepository;
    private readonly IRepository<CreditLedgerEntry> _ledgerRepository;
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
        IWorkContext workContext,
        ISettingService settingService,
        IRepository<CreditWallet> walletRepository,
        IRepository<CreditLedgerEntry> ledgerRepository,
        AIInterviewSettings aiInterviewSettings,
        MockAIInterviewSettings mockAIInterviewSettings)
    {
        _creditService = creditService;
        _inviteService = inviteService;
        _applicationService = applicationService;
        _sessionService = sessionService;
        _customerService = customerService;
        _productService = productService;
        _vendorService = vendorService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _workContext = workContext;
        _settingService = settingService;
        _walletRepository = walletRepository;
        _ledgerRepository = ledgerRepository;
        _aiInterviewSettings = aiInterviewSettings;
        _mockAIInterviewSettings = mockAIInterviewSettings;
    }

    protected async Task<string> GetLocalizedTextAsync(string resourceKey, string defaultValue)
    {
        var text = await _localizationService.GetResourceAsync(resourceKey);
        return string.IsNullOrEmpty(text) ? defaultValue : text;
    }

    protected async Task<IActionResult> LocalizedErrorAsync(string resourceKey, string defaultValue, int statusCode = 400)
    {
        return new JsonResult(new { error = await GetLocalizedTextAsync(resourceKey, defaultValue) })
        {
            StatusCode = statusCode
        };
    }

    public IActionResult General()
    {
        var model = new GeneralSettingsModel
        {
            ResumeRequired = _aiInterviewSettings.ResumeRequired,
            InterviewRequired = _aiInterviewSettings.InterviewRequired,
            MinimumScore = _aiInterviewSettings.MinimumScore
        };

        return View("~/Plugins/Misc.AIInterview/Views/Admin/General.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> General(GeneralSettingsModel model)
    {
        if (!ModelState.IsValid)
            return View("~/Plugins/Misc.AIInterview/Views/Admin/General.cshtml", model);

        _aiInterviewSettings.ResumeRequired = model.ResumeRequired;
        _aiInterviewSettings.InterviewRequired = model.InterviewRequired;
        _aiInterviewSettings.MinimumScore = model.MinimumScore;
        await _settingService.SaveSettingAsync(_aiInterviewSettings);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));
        return General();
    }

    public IActionResult AiService()
    {
        var model = new AiServiceSettingsModel
        {
            UseMockResponses = _mockAIInterviewSettings.UseMockResponses,
            Provider = _aiInterviewSettings.Provider,
            ApiKey = _aiInterviewSettings.ApiKey,
            Model = _aiInterviewSettings.Model,
            Prompt = _aiInterviewSettings.Prompt,
            ServiceSettings = _aiInterviewSettings.ServiceSettings,
            CreditProductSkuMappingsJson = _aiInterviewSettings.CreditProductSkuMappingsJson,
            CreditPurchasePageUrl = _aiInterviewSettings.CreditPurchasePageUrl,
            AzureOpenAiEndpointUrl = _aiInterviewSettings.AzureOpenAiEndpointUrl,
            AzureOpenAiApiKey = _aiInterviewSettings.AzureOpenAiApiKey,
            AzureOpenAiDeploymentOrModel = _aiInterviewSettings.AzureOpenAiDeploymentOrModel,
            AgoraAppId = _aiInterviewSettings.AgoraAppId,
            AgoraTokenServiceUrl = _aiInterviewSettings.AgoraTokenServiceUrl,
            AzureSpeechKey = _aiInterviewSettings.AzureSpeechKey,
            AzureSpeechRegion = _aiInterviewSettings.AzureSpeechRegion
        };

        return View("~/Plugins/Misc.AIInterview/Views/Admin/AiService.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> AiService(AiServiceSettingsModel model)
    {
        if (!ModelState.IsValid)
            return View("~/Plugins/Misc.AIInterview/Views/Admin/AiService.cshtml", model);

        if (!TryValidateCreditProductSkuMappingsJson(model.CreditProductSkuMappingsJson))
        {
            var mappingValidationError = await GetLocalizedTextAsync(
                "Plugins.Misc.AIInterview.Admin.AiService.CreditProductSkuMappingsJson.Invalid",
                "The credit product SKU mappings JSON is invalid. Use a JSON object such as {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10}.");
            ModelState.AddModelError(nameof(model.CreditProductSkuMappingsJson), mappingValidationError);
            _notificationService.ErrorNotification(mappingValidationError);
            return View("~/Plugins/Misc.AIInterview/Views/Admin/AiService.cshtml", model);
        }

        _mockAIInterviewSettings.UseMockResponses = model.UseMockResponses;
        await _settingService.SaveSettingAsync(_mockAIInterviewSettings);

        _aiInterviewSettings.Provider = model.Provider;
        _aiInterviewSettings.ApiKey = model.ApiKey;
        _aiInterviewSettings.Model = model.Model;
        _aiInterviewSettings.Prompt = model.Prompt;
        _aiInterviewSettings.ServiceSettings = model.ServiceSettings;
        _aiInterviewSettings.CreditProductSkuMappingsJson = model.CreditProductSkuMappingsJson;
        _aiInterviewSettings.CreditPurchasePageUrl = model.CreditPurchasePageUrl;
        _aiInterviewSettings.AzureOpenAiEndpointUrl = model.AzureOpenAiEndpointUrl;
        _aiInterviewSettings.AzureOpenAiApiKey = model.AzureOpenAiApiKey;
        _aiInterviewSettings.AzureOpenAiDeploymentOrModel = model.AzureOpenAiDeploymentOrModel;
        _aiInterviewSettings.AgoraAppId = model.AgoraAppId;
        _aiInterviewSettings.AgoraTokenServiceUrl = model.AgoraTokenServiceUrl;
        _aiInterviewSettings.AzureSpeechKey = model.AzureSpeechKey;
        _aiInterviewSettings.AzureSpeechRegion = model.AzureSpeechRegion;
        await _settingService.SaveSettingAsync(_aiInterviewSettings);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));
        return AiService();
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
        return View("~/Plugins/Misc.AIInterview/Views/Admin/VendorCredits.cshtml", await PrepareCreditModelAsync("Plugins.Misc.AIInterview.Admin.Credits.VendorTitle", customerId));
    }

    [HttpPost]
    public async Task<IActionResult> VendorCredits(CreditManagementModel model)
    {
        return await HandleCreditTopUpAsync(model, "Plugins.Misc.AIInterview.Admin.Credits.VendorTitle");
    }

    public async Task<IActionResult> ApplicantCredits(int? customerId = null)
    {
        return View("~/Plugins/Misc.AIInterview/Views/Admin/ApplicantCredits.cshtml", await PrepareCreditModelAsync("Plugins.Misc.AIInterview.Admin.Credits.ApplicantTitle", customerId));
    }

    [HttpPost]
    public async Task<IActionResult> ApplicantCredits(CreditManagementModel model)
    {
        return await HandleCreditTopUpAsync(model, "Plugins.Misc.AIInterview.Admin.Credits.ApplicantTitle");
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
        var invites = await _inviteService.GetSponsorInvitesAsync(0);
        model.Invites = invites
            .OrderByDescending(invite => invite.CreatedOnUtc)
            .Select(invite => new SponsorInviteRowModel
            {
                Id = invite.Id,
                SponsorId = invite.SponsorId,
                ProductId = invite.ProductId,
                Email = invite.Email,
                InviteCode = invite.InviteCode,
                MaxAttempts = invite.MaxAttempts,
                ExpiryDateUtc = invite.ExpiryDateUtc,
                IsActive = invite.IsActive,
                IsAccepted = invite.IsAccepted,
                IsExpired = invite.ExpiryDateUtc.HasValue && invite.ExpiryDateUtc.Value <= DateTime.UtcNow,
                CreatedOnUtc = invite.CreatedOnUtc,
                Status = GetInviteStatus(invite)
            })
            .ToList();

        return model;
    }

    protected virtual string GetInviteStatus(SponsorInvite invite)
    {
        if (invite == null)
            return string.Empty;

        if (invite.IsAccepted)
            return "Plugins.Misc.AIInterview.Employer.Invite.Accepted";

        if (!invite.IsActive)
            return "Plugins.Misc.AIInterview.Employer.Invite.Inactive";

        if (invite.ExpiryDateUtc.HasValue && invite.ExpiryDateUtc.Value <= DateTime.UtcNow)
            return "Plugins.Misc.AIInterview.Employer.Invite.Expired";

        return "Plugins.Misc.AIInterview.Employer.Invite.Active";
    }

    protected virtual async Task<CreditManagementModel> PrepareCreditModelAsync(string scopeTitleResourceKey, int? customerId, bool createWallet = true)
    {
        var model = new CreditManagementModel
        {
            CustomerId = customerId ?? 0,
            ScopeTitle = await _localizationService.GetResourceAsync(scopeTitleResourceKey)
        };

        if (model.CustomerId <= 0)
            return model;

        if (!createWallet)
            return model;

        var wallet = await _creditService.GetOrCreateWalletAsync(model.CustomerId);
        if (wallet == null)
            return model;

        model.WalletBalance = wallet.Balance;

        model.LedgerEntries = await _ledgerRepository.Table
            .Where(entry => entry.CreditWalletId == wallet.Id)
            .OrderByDescending(entry => entry.CreatedOnUtc)
            .Take(20)
            .Select(entry => new CreditLedgerRowModel
            {
                Amount = entry.Amount,
                TransactionType = entry.TransactionType,
                Remarks = entry.Remarks,
                CreatedOnUtc = entry.CreatedOnUtc
            })
            .ToListAsync();

        return model;
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
        if (customer == null)
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

    protected virtual async Task<ScoreboardFilterModel> PrepareScoreboardModelAsync(ScoreboardFilterModel filter)
    {
        filter ??= new ScoreboardFilterModel();

        var applications = await _applicationService.GetApplicationsAsync(pageSize: int.MaxValue);
        var filteredApplications = applications.AsEnumerable();

        var rows = new List<ScoreboardRowModel>();
        var productIds = filteredApplications.Select(application => application.ProductId).Distinct().Where(id => id > 0).ToArray();
        var products = await _productService.GetProductsByIdsAsync(productIds);
        var vendors = new Dictionary<int, Vendor>();
        foreach (var vendorId in products.Where(product => product.VendorId > 0).Select(product => product.VendorId).Distinct())
        {
            var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
            if (vendor != null)
                vendors[vendorId] = vendor;
        }

        var customers = await _customerService.GetCustomersByIdsAsync(filteredApplications.Select(application => application.CustomerId).Distinct().ToArray());
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
                CandidateName = GetCustomerName(customer),
                CandidateEmail = customer?.Email ?? string.Empty,
                VendorName = vendor?.Name ?? string.Empty,
                JobTitle = application.JobTitle,
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
}
