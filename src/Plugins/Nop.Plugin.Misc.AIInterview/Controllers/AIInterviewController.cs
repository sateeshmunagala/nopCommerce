using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Http;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Data;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Seo;
using Nop.Services.Vendors;
using Nop.Services.Orders;
using Nop.Services.Stores;
using Nop.Services.Common;
using Nop.Services.Helpers;
using Nop.Web.Factories;
using Nop.Web.Framework.Controllers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Nop.Core.Http;
using Nop.Plugin.Misc.AIInterview.Infrastructure;
using Nop.Web.Framework.Mvc.Routing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

public class AIInterviewController : BasePluginController
{
    private const int DefaultMyActivityPageSize = 5;
    private const int MaxMyActivityPageSize = 50;
    private const int DefaultEmployerDashboardTablePageSize = 10;
    private const int DefaultEmployerApplicationsPageSize = DefaultEmployerDashboardTablePageSize;

    private readonly IApplicationService _applicationService;
    private readonly IInterviewSessionService _interviewSessionService;
    private readonly AIInterviewSettings _aiInterviewSettings;
    private readonly IWorkContext _workContext;
    private readonly INotificationService _notificationService;
    private readonly ILocalizationService _localizationService;
    private readonly IDownloadService _downloadService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly IUrlRecordService _urlRecordService;
    private readonly IJobInterviewExperienceService _jobInterviewExperienceService;
    private readonly IJobRequirementService _jobRequirementService;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IInterviewTurnService _interviewTurnService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IStoreContext _storeContext;
    private readonly IProductModelFactory _productModelFactory;
    private readonly IResumeFileService _resumeFileService;
    private readonly IResumeProfileService _resumeProfileService;
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly IAIInterviewJobDisplayService _aiInterviewJobDisplayService;
    private readonly IJobProductAccessService _jobProductAccessService;
    private readonly ISponsorInviteService _inviteService;
    private readonly ICreditService _creditService;
    private readonly ICreditActivityService _creditActivityService;
    private readonly IVendorService _vendorService;
    private readonly IRepository<GenericAttribute> _genericAttributeRepository;
    private readonly IRepository<CreditLedgerEntry> _creditLedgerRepository;
    private readonly ILogger<AIInterviewController> _logger;

    public AIInterviewController(IApplicationService applicationService,
        IInterviewSessionService interviewSessionService,
        AIInterviewSettings aiInterviewSettings,
        IWorkContext workContext,
        INotificationService notificationService,
        ILocalizationService localizationService,
        IDownloadService downloadService,
        ICustomerService customerService,
        IProductService productService,
        IJobRequirementService jobRequirementService,
        IProductTemplateService productTemplateService = null,
        IUrlRecordService urlRecordService = null,
        IJobInterviewExperienceService jobInterviewExperienceService = null,
        ISpecificationAttributeService specificationAttributeService = null,
        IInterviewTurnService interviewTurnService = null,
        IHttpClientFactory httpClientFactory = null,
        INopUrlHelper nopUrlHelper = null,
        IShoppingCartService shoppingCartService = null,
        IStoreContext storeContext = null,
        IProductModelFactory productModelFactory = null,
        IResumeFileService resumeFileService = null,
        IResumeProfileService resumeProfileService = null,
        IDateTimeHelper dateTimeHelper = null,
        IGenericAttributeService genericAttributeService = null,
        IAIInterviewJobDisplayService aiInterviewJobDisplayService = null,
        IJobProductAccessService jobProductAccessService = null,
        ISponsorInviteService inviteService = null,
        ICreditService creditService = null,
        ICreditActivityService creditActivityService = null,
        IVendorService vendorService = null,
        IRepository<GenericAttribute> genericAttributeRepository = null,
        IRepository<CreditLedgerEntry> creditLedgerRepository = null,
        ILogger<AIInterviewController> logger = null)
    {
        _applicationService = applicationService;
        _interviewSessionService = interviewSessionService;
        _aiInterviewSettings = aiInterviewSettings;
        _workContext = workContext;
        _notificationService = notificationService;
        _localizationService = localizationService;
        _downloadService = downloadService;
        _customerService = customerService;
        _productService = productService;
        _productTemplateService = productTemplateService;
        _urlRecordService = urlRecordService;
        _jobInterviewExperienceService = jobInterviewExperienceService;
        _jobRequirementService = jobRequirementService;
        _specificationAttributeService = specificationAttributeService;
        _interviewTurnService = interviewTurnService;
        _httpClientFactory = httpClientFactory;
        _nopUrlHelper = nopUrlHelper;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _productModelFactory = productModelFactory;
        _resumeFileService = resumeFileService;
        _resumeProfileService = resumeProfileService;
        _dateTimeHelper = dateTimeHelper;
        _genericAttributeService = genericAttributeService;
        _aiInterviewJobDisplayService = aiInterviewJobDisplayService;
        _jobProductAccessService = jobProductAccessService;
        _inviteService = inviteService;
        _creditService = creditService;
        _creditActivityService = creditActivityService;
        _vendorService = vendorService;
        _genericAttributeRepository = genericAttributeRepository;
        _creditLedgerRepository = creditLedgerRepository;
        _logger = logger;
    }

    public AIInterviewController(IApplicationService applicationService,
        IInterviewSessionService interviewSessionService,
        AIInterviewSettings aiInterviewSettings,
        IWorkContext workContext,
        INotificationService notificationService,
        ILocalizationService localizationService,
        IDownloadService downloadService,
        ICustomerService customerService,
        IProductService productService,
        IProductTemplateService productTemplateService = null,
        IUrlRecordService urlRecordService = null,
        IJobInterviewExperienceService jobInterviewExperienceService = null,
        ISpecificationAttributeService specificationAttributeService = null,
        INopUrlHelper nopUrlHelper = null,
        IShoppingCartService shoppingCartService = null,
        IStoreContext storeContext = null,
        IProductModelFactory productModelFactory = null,
        IResumeFileService resumeFileService = null,
        IResumeProfileService resumeProfileService = null,
        IDateTimeHelper dateTimeHelper = null,
        IGenericAttributeService genericAttributeService = null,
        IAIInterviewJobDisplayService aiInterviewJobDisplayService = null,
        IJobProductAccessService jobProductAccessService = null,
        ISponsorInviteService inviteService = null,
        ICreditService creditService = null,
        ICreditActivityService creditActivityService = null,
        IVendorService vendorService = null,
        IRepository<GenericAttribute> genericAttributeRepository = null,
        IRepository<CreditLedgerEntry> creditLedgerRepository = null,
        ILogger<AIInterviewController> logger = null)
        : this(applicationService,
            interviewSessionService,
            aiInterviewSettings,
            workContext,
            notificationService,
            localizationService,
            downloadService,
            customerService,
            productService,
            null,
            productTemplateService,
            urlRecordService,
            jobInterviewExperienceService,
            specificationAttributeService,
            null,
            null,
            nopUrlHelper,
            shoppingCartService,
            storeContext,
            productModelFactory,
            resumeFileService,
            resumeProfileService,
            dateTimeHelper,
            genericAttributeService,
            aiInterviewJobDisplayService,
            jobProductAccessService,
            inviteService,
            creditService,
            creditActivityService,
            vendorService,
            genericAttributeRepository,
            creditLedgerRepository,
            logger)
    {
    }

    protected virtual async Task<(DateTime? StartDateUtc, DateTime? EndDateUtc)> ConvertApplicationFilterDatesToUtcAsync(DateTime? startDate, DateTime? endDate)
    {
        if (_dateTimeHelper == null)
            return (startDate, endDate);

        var currentTimeZone = await _dateTimeHelper.GetCurrentTimeZoneAsync();
        DateTime? startDateUtc = startDate.HasValue
            ? _dateTimeHelper.ConvertToUtcTime(startDate.Value.Date, currentTimeZone)
            : null;
        DateTime? endDateUtc = endDate.HasValue
            ? _dateTimeHelper.ConvertToUtcTime(endDate.Value.Date.AddDays(1), currentTimeZone).AddTicks(-1)
            : null;

        return (startDateUtc, endDateUtc);
    }

    public virtual async Task<IActionResult> JobDetailsDrawer(int productId)
    {
        var unavailableText = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.UnableToLoadJobDetails");
        var notFoundText = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.JobNotFound");
        var invalidJobText = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.InvalidJob");

        if (!_aiInterviewSettings.Enabled || _productModelFactory == null)
            return NotFound(unavailableText);

        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null || _productTemplateService == null)
            return NotFound(notFoundText);

        var productTemplate = await _productTemplateService.GetProductTemplateByIdAsync(product.ProductTemplateId);
        if (productTemplate == null ||
            !string.Equals(productTemplate.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(invalidJobText);
        }

        if (_jobProductAccessService != null &&
            !await _jobProductAccessService.CanViewJobProductAsync(product, allowAdminPreview: true))
        {
            return NotFound(notFoundText);
        }

        try
        {
            var model = await _productModelFactory.PrepareProductDetailsModelAsync(product);
            if (model == null)
                return BadRequest(unavailableText);

            if (string.IsNullOrWhiteSpace(model.SeName) && _urlRecordService != null)
                model.SeName = await _urlRecordService.GetSeNameAsync(product);

            var productPageUrl = await BuildProductRedirectUrlAsync(product);
            if (string.IsNullOrWhiteSpace(productPageUrl) && !string.IsNullOrWhiteSpace(model.SeName))
                productPageUrl = Url.RouteUrl("Product", new { SeName = model.SeName }) ?? string.Empty;

            ViewData["ProductPageUrl"] = productPageUrl;
            ViewData["ProductSeName"] = model.SeName ?? string.Empty;

            return PartialView("~/Plugins/Misc.AIInterview/Views/Shared/_AIInterviewJobDetailsDrawer.cshtml", model);
        }
        catch
        {
            return BadRequest(unavailableText);
        }
    }

    [HttpPost]
    public virtual async Task<IActionResult> ToggleSavedJob(int productId, bool save)
    {
        if (!_aiInterviewSettings.Enabled)
            return Json(new { success = false, redirect = Url.RouteUrl("Homepage") });

        if (_shoppingCartService == null || _storeContext == null)
            return Json(new
            {
                success = false,
                message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.SavedJobsUnavailable")
            });

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Json(new { success = false, redirect = Url.RouteUrl(NopRouteNames.General.LOGIN) });

        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return Json(new
            {
                success = false,
                message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.JobNotFound")
            });

        if (_jobRequirementService == null || !await _jobRequirementService.IsJobProductAsync(product))
        {
            return Json(new
            {
                success = false,
                message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.InvalidJob")
            });
        }

        var store = await _storeContext.GetCurrentStoreAsync();
        var savedText = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.SavedToSavedJobs");
        var removedText = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.JobCard.RemovedFromSavedJobs");

        var wishlistItems = (await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id, productId: product.Id, customWishlistId: 0)).ToList();

        if (save)
        {
            if (wishlistItems.Count > 1)
            {
                foreach (var duplicateItem in wishlistItems.Skip(1))
                    await _shoppingCartService.DeleteShoppingCartItemAsync(duplicateItem);

                wishlistItems = (await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id, productId: product.Id, customWishlistId: 0)).ToList();
            }

            if (!wishlistItems.Any())
            {
                var warnings = await _shoppingCartService.AddToCartAsync(customer, product, ShoppingCartType.Wishlist, store.Id, quantity: 1, wishlistId: null);
                if (warnings.Any())
                    return Json(new { success = false, message = warnings.ToArray() });

                wishlistItems = (await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id, productId: product.Id, customWishlistId: 0)).ToList();
            }
        }
        else if (wishlistItems.Any())
        {
            foreach (var wishlistItem in wishlistItems)
                await _shoppingCartService.DeleteShoppingCartItemAsync(wishlistItem);

            wishlistItems.Clear();
        }

        var allWishlistItems = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id, customWishlistId: 0);
        var updateTopWishlistSectionHtml = string.Format(await _localizationService.GetResourceAsync("Wishlist.HeaderQuantity"),
            allWishlistItems.Sum(item => item.Quantity));

        var savedItem = wishlistItems.FirstOrDefault();
        var isSaved = savedItem != null;

        return Json(new
        {
            success = true,
            isSaved,
            wishlistItemId = savedItem?.Id ?? 0,
            message = isSaved ? savedText : removedText,
            updatetopwishlistsectionhtml = updateTopWishlistSectionHtml
        });
    }

    protected virtual async Task<string> BuildProductRedirectUrlAsync(Product product, IDictionary<string, string> query = null)
    {
        if (product == null || _nopUrlHelper == null)
            return null;

        var url = await _nopUrlHelper.RouteGenericUrlAsync(product);
        if (string.IsNullOrWhiteSpace(url) || query == null || !query.Any(item => !string.IsNullOrWhiteSpace(item.Value)))
            return url;

        var filteredQuery = query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

        return filteredQuery.Count == 0 ? url : QueryHelpers.AddQueryString(url, filteredQuery);
    }

    protected static bool SessionMatchesApplication(InterviewSession session, JobApplication application)
    {
        if (string.Equals(session?.InterviewType, AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase))
            return false;

        return session.JobApplicationId == application.Id ||
            (session.JobApplicationId == 0 && application.ProductId > 0 && session.ProductId == application.ProductId);
    }

    protected async Task<IList<JobApplication>> GetApplicationsForJobAsync(int customerId, int productId, string jobTitle)
    {
        if (productId <= 0)
            return await _applicationService.GetJobApplicationsByCustomerIdAndJobTitleAsync(customerId, jobTitle)
                ?? new List<JobApplication>();

        var applications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customerId);
        if (applications != null)
            return applications.Where(application => application.ProductId == productId).ToList();

        return await _applicationService.GetJobApplicationsByCustomerIdAndJobTitleAsync(customerId, jobTitle)
            ?? new List<JobApplication>();
    }

    protected static IList<decimal> ParseQuestionScores(string questionScores)
    {
        if (string.IsNullOrWhiteSpace(questionScores))
            return new List<decimal>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<decimal>>(questionScores);
            return parsed ?? new List<decimal>();
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

    protected virtual string BuildRouteUrl(string routeName, object values = null)
    {
        var relativeUrl = Url?.RouteUrl(new UrlRouteContext
        {
            RouteName = routeName,
            Values = values
        });
        if (string.IsNullOrWhiteSpace(relativeUrl))
            return null;

        if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out _))
            return relativeUrl;

        if (Request?.Host.HasValue == true)
            return $"{Request.Scheme}://{Request.Host}{Request.PathBase}{relativeUrl}";

        return relativeUrl;
    }

    protected virtual string BuildAuthenticatedReportUrl(int sessionId)
    {
        return sessionId > 0 ? Url?.Action("Report", "AIInterview", new { sessionId }) : null;
    }

    protected virtual string BuildReportPanelUrl(int sessionId)
    {
        return sessionId > 0 ? Url?.Action("ReportPanel", "AIInterview", new { sessionId }) : null;
    }

    protected virtual string BuildAuthenticatedRecordingUrl(InterviewSession session)
    {
        return string.IsNullOrWhiteSpace(session?.RecordingUrl) ? null : session.RecordingUrl;
    }

    protected virtual Task<string> BuildRecordingShareUrlAsync(InterviewSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.RecordingUrl))
            return Task.FromResult<string>(null);

        return Task.FromResult(session.RecordingUrl);
    }

    protected virtual async Task<string> BuildReportShareUrlAsync(InterviewSession session)
    {
        if (session == null ||
            (string.IsNullOrWhiteSpace(session.ReportData) && string.IsNullOrWhiteSpace(session.RecordingUrl)))
        {
            return null;
        }

        var token = await _interviewSessionService.EnsureReportShareTokenAsync(session);
        return string.IsNullOrWhiteSpace(token)
            ? null
            : BuildRouteUrl(AIInterviewDefaults.ReportShareRouteName, new { token });
    }

    protected virtual IList<InterviewTurnViewModel> MapTurns(IList<InterviewTurn> turns)
    {
        return (turns ?? new List<InterviewTurn>()).Select(turn => new InterviewTurnViewModel
        {
            TurnId = turn.Id,
            SequenceNumber = turn.SequenceNumber,
            QuestionText = turn.QuestionText,
            AnswerText = turn.AnswerText,
            Score = turn.Score,
            TechnicalScore = ParseRubricScore(turn.RubricJson, "technicalScore"),
            CommunicationScore = ParseRubricScore(turn.RubricJson, "communicationScore"),
            ProfessionalismScore = ParseRubricScore(turn.RubricJson, "professionalismScore"),
            PositiveAttitudeScore = ParseRubricScore(turn.RubricJson, "positiveAttitudeScore"),
            Feedback = turn.Feedback,
            AskedOnUtc = turn.AskedOnUtc,
            AnsweredOnUtc = turn.AnsweredOnUtc
        }).ToList();
    }

    protected virtual async Task<IList<InterviewTurn>> GetTurnsSafeAsync(int sessionId)
    {
        if (_interviewTurnService == null || sessionId <= 0)
            return new List<InterviewTurn>();

        try
        {
            return ((await _interviewTurnService.GetTurnsBySessionIdAsync(sessionId)) ?? new List<InterviewTurn>()).ToList();
        }
        catch
        {
            return new List<InterviewTurn>();
        }
    }

    protected virtual async Task<InterviewReportModel> BuildInterviewReportModelAsync(InterviewSession session)
    {
        if (session == null)
            return null;

        var turns = await GetTurnsSafeAsync(session.Id);
        var product = session.ProductId > 0 ? await _productService.GetProductByIdAsync(session.ProductId) : null;
        var reportShareUrl = await BuildReportShareUrlAsync(session);
        var candidate = session.CustomerId > 0 ? await _customerService.GetCustomerByIdAsync(session.CustomerId) : null;
        var candidateName = candidate != null ? (candidate.FirstName + " " + candidate.LastName).Trim() : string.Empty;
        var skillContext = BuildSelectedAttributesSummary(session.SelectedProductAttributesJson);
        var resumeUsed = session.ResumeDownloadId > 0 || !string.IsNullOrWhiteSpace(session.ResumeProfileJson);

        return new InterviewReportModel
        {
            SessionId = session.Id,
            CustomerId = session.CustomerId,
            ProductId = session.ProductId,
            ProductName = product?.Name ?? string.Empty,
            JobTitle = product?.Name ?? string.Empty,
            Difficulty = session.Difficulty,
            Score = session.Score,
            QuestionScores = session.QuestionScores,
            ParsedQuestionScores = ParseQuestionScores(session.QuestionScores),
            ReportData = InterviewReportSummaryHelper.NormalizePersistedReportData(session.ReportData, turns, session.Score),
            RecordingUrl = BuildAuthenticatedRecordingUrl(session),
            RecordingShareUrl = reportShareUrl,
            ReportShareUrl = reportShareUrl,
            CreatedOnUtc = session.CreatedOnUtc,
            ReportDateUtc = session.CompletedOnUtc ?? session.StartedOnUtc ?? session.CreatedOnUtc,
            CompletedOnUtc = session.CompletedOnUtc,
            Turns = MapTurns(turns),
            CandidateName = candidateName,
            InterviewType = session.InterviewType ?? string.Empty,
            SkillContext = skillContext,
            ResumeUsed = resumeUsed
        };
    }

    private sealed record ReportAttributeSummaryEntry(int? AttributeId, string AttributeName, string TextPrompt, int? ValueId, string Value);

    private sealed record ReportAttributesSummarySnapshot(IList<ReportAttributeSummaryEntry> Attributes);

    protected virtual string BuildSelectedAttributesSummary(string selectedProductAttributesJson)
    {
        if (string.IsNullOrWhiteSpace(selectedProductAttributesJson))
            return string.Empty;

        try
        {
            var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<ReportAttributesSummarySnapshot>(selectedProductAttributesJson, options);
            var attributes = snapshot?.Attributes?
                .Where(a => !string.IsNullOrWhiteSpace(a?.AttributeName) && !string.IsNullOrWhiteSpace(a?.Value))
                .ToList();

            if (attributes == null || attributes.Count == 0)
                return string.Empty;

            var skill = attributes
                .FirstOrDefault(a => string.Equals(a.AttributeName.Trim(), "Skill", StringComparison.OrdinalIgnoreCase))
                ?.Value?.Trim();

            var difficulty = attributes
                .FirstOrDefault(a => string.Equals(a.AttributeName.Trim(), "Difficulty", StringComparison.OrdinalIgnoreCase))
                ?.Value?.Trim();

            if (!string.IsNullOrWhiteSpace(skill) && !string.IsNullOrWhiteSpace(difficulty))
                return $"Skill: {skill} \u00b7 Difficulty: {difficulty}";

            if (!string.IsNullOrWhiteSpace(skill))
                return $"Skill: {skill}";

            if (!string.IsNullOrWhiteSpace(difficulty))
                return $"Difficulty: {difficulty}";

            return string.Join(" \u00b7 ", attributes
                .Select(a => a.Value.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return string.Empty;
        }
    }

    protected virtual async Task<IActionResult> ProxyRecordingAsync(string recordingUrl)
    {
        _logger?.LogInformation("AI Interview recording proxy requested. RecordingUrl={RecordingUrl}; RecordingUrlConfigured={RecordingUrlConfigured}.",
            BuildSafeRecordingUrlLogValue(recordingUrl),
            !string.IsNullOrWhiteSpace(recordingUrl));

        var playbackUrl = BuildRecordingPlaybackUrl(recordingUrl);
        if (string.IsNullOrWhiteSpace(playbackUrl))
        {
            _logger?.LogWarning("AI Interview recording proxy could not build playback URL. RecordingUrl={RecordingUrl}.",
                BuildSafeRecordingUrlLogValue(recordingUrl));
            return NotFound();
        }

        _logger?.LogInformation("AI Interview recording proxy playback URL built. PlaybackUrl={PlaybackUrl}; SasTokenAppended={SasTokenAppended}.",
            BuildSafeRecordingUrlLogValue(playbackUrl),
            !string.IsNullOrWhiteSpace(_aiInterviewSettings?.AzureBlobStorageSasToken));

        using var client = _httpClientFactory?.CreateClient(nameof(AIInterviewController)) ?? new HttpClient();
        var response = await client.GetAsync(playbackUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogWarning("AI Interview recording proxy source returned non-success. RecordingUrl={RecordingUrl}; StatusCode={StatusCode}; ReasonPhrase={ReasonPhrase}.",
                BuildSafeRecordingUrlLogValue(recordingUrl),
                (int)response.StatusCode,
                response.ReasonPhrase);
            response.Dispose();
            return NotFound();
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "video/webm";
        _logger?.LogInformation("AI Interview recording proxy source opened. RecordingUrl={RecordingUrl}; StatusCode={StatusCode}; ContentType={ContentType}; ContentLength={ContentLength}.",
            BuildSafeRecordingUrlLogValue(recordingUrl),
            (int)response.StatusCode,
            contentType,
            response.Content.Headers.ContentLength);

        var stream = await response.Content.ReadAsStreamAsync();
        return File(new ProxyResponseStream(stream, response), contentType);
    }

    public async Task<IActionResult> Index()
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var model = new PublicInfoModel
        {
            MinimumScore = _aiInterviewSettings.MinimumScore
        };

        return View("~/Plugins/Misc.AIInterview/Views/Index.cshtml", model);
    }

    protected virtual string NormalizeMyActivityTab(string tab)
    {
        return (tab ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            var value when string.Equals(value, AIInterviewDefaults.MyActivitySavedJobsTabKey, StringComparison.Ordinal) => AIInterviewDefaults.MyActivitySavedJobsTabKey,
            var value when string.Equals(value, AIInterviewDefaults.MyActivityMockInterviewsTabKey, StringComparison.Ordinal) => AIInterviewDefaults.MyActivityMockInterviewsTabKey,
            var value when string.Equals(value, AIInterviewDefaults.MyActivityCreditsTabKey, StringComparison.Ordinal) => AIInterviewDefaults.MyActivityCreditsTabKey,
            _ => AIInterviewDefaults.MyActivityAppliedJobsTabKey
        };
    }

    protected virtual bool IsHtmxRequest()
    {
        return string.Equals(HttpContext?.Request?.Headers["HX-Request"], "true", StringComparison.OrdinalIgnoreCase);
    }

    protected virtual (int Page, int PageSize, int TotalPages) NormalizeMyActivityPaging(int totalCount, int page, int pageSize)
    {
        var normalizedPageSize = pageSize < 1 ? DefaultMyActivityPageSize : Math.Min(pageSize, MaxMyActivityPageSize);
        var totalPages = totalCount > 0 ? (int)Math.Ceiling(totalCount / (double)normalizedPageSize) : 0;
        var normalizedPage = page < 1 ? 1 : page;

        if (totalPages > 0 && normalizedPage > totalPages)
            normalizedPage = totalPages;

        return (normalizedPage, normalizedPageSize, totalPages);
    }

    protected virtual async Task<ApplicationListModel> BuildMyApplicationsModelAsync(Customer customer, string sortOrder, string status = null, decimal? minScore = null, decimal? maxScore = null)
    {
        return await BuildMyApplicationsModelAsync(customer, sortOrder, status, minScore, maxScore, 1, DefaultMyActivityPageSize, false);
    }

    protected virtual async Task<ApplicationListModel> BuildMyApplicationsModelAsync(Customer customer, string sortOrder, string status, decimal? minScore, decimal? maxScore, int page, int pageSize, bool paginate)
    {
        var applications = (await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id) ?? new List<JobApplication>()).ToList();
        var sessions = (await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id) ?? new List<InterviewSession>()).ToList();

        var applicationModels = await Task.WhenAll(applications.Select(async application =>
        {
            var appSessions = sessions.Where(session => SessionMatchesApplication(session, application)).ToList();
            var latestSession = appSessions
                .Where(session => session.CompletedOnUtc.HasValue)
                .OrderByDescending(session => session.CompletedOnUtc)
                .ThenByDescending(session => session.Id)
                .FirstOrDefault();
            var normalizedStatus = JobApplicationStatuses.Normalize(application.Status);
            var questionScores = ParseQuestionScores(latestSession?.QuestionScores);
            var reportSections = SplitReportSections(latestSession?.ReportData);
            var turns = latestSession != null && _interviewTurnService != null
                ? ((await _interviewTurnService.GetTurnsBySessionIdAsync(latestSession.Id)) ?? new List<InterviewTurn>()).ToList()
                : new List<InterviewTurn>();

            return new ApplicationModel
            {
                Id = application.Id,
                InterviewSessionId = latestSession?.Id ?? 0,
                JobTitle = application.JobTitle,
                InterviewScore = latestSession?.Score,
                InterviewReportUrl = latestSession != null ? BuildAuthenticatedReportUrl(latestSession.Id) : null,
                InterviewReportPanelUrl = latestSession != null ? BuildReportPanelUrl(latestSession.Id) : null,
                RecordingUrl = BuildAuthenticatedRecordingUrl(latestSession),
                RecordingShareUrl = latestSession != null ? await BuildReportShareUrlAsync(latestSession) : null,
                Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{normalizedStatus}"),
                RawStatus = normalizedStatus,
                CreatedOn = application.CreatedOnUtc,
                AttemptCount = appSessions.Count,
                LatestScoreDate = latestSession?.CompletedOnUtc,
                CompletedOn = latestSession?.CompletedOnUtc,
                QuestionScores = latestSession?.QuestionScores,
                QuestionScoreValues = questionScores,
                ReportSummary = reportSections.Summary,
                FeedbackSummary = reportSections.Feedback,
                Turns = MapTurns(turns)
            };
        }));

        var query = applicationModels.AsQueryable();
        var normalizedSortOrder = string.IsNullOrEmpty(sortOrder) ? "LatestApplied" : sortOrder;

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(application => string.Equals(application.RawStatus ?? JobApplicationStatuses.Normalize(application.Status), JobApplicationStatuses.Normalize(status), StringComparison.OrdinalIgnoreCase)
                || string.Equals(application.Status, status, StringComparison.OrdinalIgnoreCase));

        if (minScore.HasValue)
            query = query.Where(application => (application.InterviewScore ?? 0) >= minScore.Value);

        if (maxScore.HasValue)
            query = query.Where(application => (application.InterviewScore ?? 0) <= maxScore.Value);

        query = normalizedSortOrder switch
        {
            "OldestApplied" => query.OrderBy(application => application.CreatedOn),
            "HighestScore" => query.OrderByDescending(application => application.InterviewScore ?? 0),
            "LowestScore" => query.OrderBy(application => application.InterviewScore ?? 0),
            "LatestInterviewDate" => query.OrderByDescending(application => application.LatestScoreDate ?? DateTime.MinValue),
            _ => query.OrderByDescending(application => application.CreatedOn)
        };

        var orderedApplications = query.ToList();
        var totalCount = orderedApplications.Count;
        var (normalizedPage, normalizedPageSize, totalPages) = paginate
            ? NormalizeMyActivityPaging(totalCount, page, pageSize)
            : (1, totalCount > 0 ? totalCount : DefaultMyActivityPageSize, totalCount > 0 ? 1 : 0);
        var pagedApplications = paginate
            ? orderedApplications.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToList()
            : orderedApplications;

        return new ApplicationListModel
        {
            Applications = pagedApplications,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            SortOrder = normalizedSortOrder,
            Status = status,
            MinScore = minScore,
            MaxScore = maxScore,
            TotalCount = totalCount,
            TotalPages = totalPages
        };
    }

    protected virtual async Task<IList<InterviewHistoryItemModel>> BuildMockInterviewHistoryItemsAsync(Customer customer)
    {
        var fallbackInterviewTitle = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Common.Interview");
        var sessions = ((await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id)) ?? new List<InterviewSession>())
            .Where(session => string.Equals(session.InterviewType, AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var model = await Task.WhenAll(sessions.Select(async session =>
        {
            var sourceProductId = session.SourceProductId > 0 ? session.SourceProductId : session.ProductId;
            var product = sourceProductId > 0 ? await _productService.GetProductByIdAsync(sourceProductId) : null;

            return new InterviewHistoryItemModel
            {
                SessionId = session.Id,
                JobTitle = product?.Name ?? fallbackInterviewTitle,
                CreatedOnUtc = session.CreatedOnUtc,
                CompletedOnUtc = session.CompletedOnUtc,
                Status = session.CompletedOnUtc.HasValue
                    ? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Status.Completed")
                    : await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Status.Active"),
                Score = session.Score,
                InterviewReportUrl = session.CompletedOnUtc.HasValue && !string.IsNullOrWhiteSpace(session.ReportData)
                    ? Url?.RouteUrl(AIInterviewDefaults.ReportRouteName, new { sessionId = session.Id })
                    : null,
                InterviewReportPanelUrl = session.CompletedOnUtc.HasValue && !string.IsNullOrWhiteSpace(session.ReportData)
                    ? Url?.Action("ReportPanel", "AIInterview", new { sessionId = session.Id })
                    : null,
                RecordingUrl = session.CompletedOnUtc.HasValue ? session.RecordingUrl : null,
                RecordingShareUrl = session.CompletedOnUtc.HasValue
                    ? await BuildReportShareUrlAsync(session)
                    : null
            };
        }));

        return model.ToList();
    }

    protected virtual async Task<MockInterviewHistoryListModel> BuildMockInterviewHistoryModelAsync(Customer customer, int page, int pageSize)
    {
        var items = (await BuildMockInterviewHistoryItemsAsync(customer))
            .OrderByDescending(item => item.CompletedOnUtc ?? item.CreatedOnUtc)
            .ThenByDescending(item => item.CreatedOnUtc)
            .ToList();
        var (normalizedPage, normalizedPageSize, totalPages) = NormalizeMyActivityPaging(items.Count, page, pageSize);

        return new MockInterviewHistoryListModel
        {
            Items = items.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToList(),
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalCount = items.Count,
            TotalPages = totalPages
        };
    }

    protected virtual async Task<SavedJobsListModel> BuildSavedJobsModelAsync(Customer customer, int page, int pageSize)
    {
        var normalizedPageSize = pageSize < 1 ? DefaultMyActivityPageSize : Math.Min(pageSize, MaxMyActivityPageSize);
        var model = new SavedJobsListModel
        {
            Page = page < 1 ? 1 : page,
            PageSize = normalizedPageSize
        };

        if (_shoppingCartService == null || _storeContext == null || _productModelFactory == null || _jobRequirementService == null)
            return model;

        var store = await _storeContext.GetCurrentStoreAsync();
        var wishlistItems = (await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id, customWishlistId: 0) ?? new List<ShoppingCartItem>())
            .OrderByDescending(item => item.CreatedOnUtc)
            .ToList();
        var productIds = wishlistItems.Select(item => item.ProductId).Distinct().ToArray();
        if (!productIds.Any())
            return model;

        var products = await _productService.GetProductsByIdsAsync(productIds) ?? new List<Product>();
        var productSortOrder = wishlistItems
            .GroupBy(item => item.ProductId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.CreatedOnUtc));

        var jobProducts = new List<Product>();
        foreach (var product in products
                     .Where(product => product != null)
                     .OrderByDescending(product => productSortOrder.GetValueOrDefault(product.Id)))
        {
            if (!await _jobRequirementService.IsJobProductAsync(product))
                continue;

            if (_jobProductAccessService != null && !await _jobProductAccessService.CanAppearInListingsAsync(product))
                continue;

            jobProducts.Add(product);
        }

        if (!jobProducts.Any())
            return model;

        var (normalizedPage, normalizedEffectivePageSize, totalPages) = NormalizeMyActivityPaging(jobProducts.Count, page, pageSize);
        var pagedProducts = jobProducts
            .Skip((normalizedPage - 1) * normalizedEffectivePageSize)
            .Take(normalizedEffectivePageSize)
            .ToList();

        model.Products = (await _productModelFactory.PrepareProductOverviewModelsAsync(pagedProducts)).ToList();
        model.Page = normalizedPage;
        model.PageSize = normalizedEffectivePageSize;
        model.TotalCount = jobProducts.Count;
        model.TotalPages = totalPages;
        return model;
    }

    protected virtual async Task<MyActivityPageModel> BuildMyActivityPageModelAsync(Customer customer, string tab, string sortOrder, string status = null, decimal? minScore = null, decimal? maxScore = null, int page = 1, int pageSize = DefaultMyActivityPageSize)
    {
        var activeTab = NormalizeMyActivityTab(tab);
        var model = new MyActivityPageModel
        {
            ActiveTab = activeTab
        };

        switch (activeTab)
        {
            case var value when string.Equals(value, AIInterviewDefaults.MyActivitySavedJobsTabKey, StringComparison.Ordinal):
                model.SavedJobs = await BuildSavedJobsModelAsync(customer, page, pageSize);
                break;
            case var value when string.Equals(value, AIInterviewDefaults.MyActivityMockInterviewsTabKey, StringComparison.Ordinal):
                model.MockInterviews = await BuildMockInterviewHistoryModelAsync(customer, page, pageSize);
                break;
            case var value when string.Equals(value, AIInterviewDefaults.MyActivityCreditsTabKey, StringComparison.Ordinal):
                model.Credits = _creditActivityService == null
                    ? new CreditActivityModel()
                    : await _creditActivityService.BuildCreditActivityModelAsync(customer, page, pageSize);
                break;
            default:
                model.AppliedJobs = await BuildMyApplicationsModelAsync(customer, sortOrder, status, minScore, maxScore, page, pageSize, true);
                break;
        }

        return model;
    }

    public async Task<IActionResult> MyActivity(string tab = null, string sortOrder = null, string status = null, decimal? minScore = null, decimal? maxScore = null, int page = 1, int pageSize = DefaultMyActivityPageSize)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var model = await BuildMyActivityPageModelAsync(customer, tab, sortOrder, status, minScore, maxScore, page, pageSize);
        ViewData["IsMyActivity"] = true;

        if (IsHtmxRequest())
            return PartialView("~/Plugins/Misc.AIInterview/Views/Shared/_MyActivityTabContent.cshtml", model);

        return View("~/Plugins/Misc.AIInterview/Views/MyActivity.cshtml", model);
    }

    protected virtual string NormalizeEmployerDashboardTab(string tab)
    {
        return (tab ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            var value when string.Equals(value, AIInterviewDefaults.EmployerDashboardJobsTabKey, StringComparison.Ordinal) => AIInterviewDefaults.EmployerDashboardJobsTabKey,
            var value when string.Equals(value, AIInterviewDefaults.EmployerDashboardApplicationsTabKey, StringComparison.Ordinal) => AIInterviewDefaults.EmployerDashboardApplicationsTabKey,
            var value when string.Equals(value, AIInterviewDefaults.EmployerDashboardInvitesTabKey, StringComparison.Ordinal) => AIInterviewDefaults.EmployerDashboardInvitesTabKey,
            _ => AIInterviewDefaults.EmployerDashboardOverviewTabKey
        };
    }

    protected virtual RouteValueDictionary BuildEmployerDashboardRouteValues(string tab, ApplicationListModel applicationsModel = null)
    {
        var values = new RouteValueDictionary
        {
            ["tab"] = NormalizeEmployerDashboardTab(tab)
        };

        if (applicationsModel == null)
            return values;

        values["CandidateNameOrEmail"] = applicationsModel.CandidateNameOrEmail;
        values["Status"] = applicationsModel.Status;
        values["MinScore"] = applicationsModel.MinScore;
        values["MaxScore"] = applicationsModel.MaxScore;
        values["StartDate"] = applicationsModel.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        values["EndDate"] = applicationsModel.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        values["InterviewSort"] = applicationsModel.InterviewSort;
        values["OnlyWithInterviewScore"] = applicationsModel.OnlyWithInterviewScore;
        values["PageSize"] = applicationsModel.PageSize;
        values["Page"] = applicationsModel.Page;

        return values;
    }

    protected virtual async Task<EmployerDashboardPageModel> BuildEmployerDashboardPageModelAsync(Customer customer, string tab, ApplicationListModel applicationsModel = null, int page = 1, int pageSize = DefaultEmployerDashboardTablePageSize)
    {
        var activeTab = NormalizeEmployerDashboardTab(tab);
        var model = new EmployerDashboardPageModel
        {
            ActiveTab = activeTab
        };

        switch (activeTab)
        {
            case var value when string.Equals(value, AIInterviewDefaults.EmployerDashboardJobsTabKey, StringComparison.Ordinal):
                model.Jobs = await BuildEmployerDashboardJobsTabModelAsync(customer, page, pageSize);
                break;
            case var value when string.Equals(value, AIInterviewDefaults.EmployerDashboardApplicationsTabKey, StringComparison.Ordinal):
                model.Applications = await BuildEmployerApplicationsModelAsync(customer, applicationsModel ?? new ApplicationListModel(), page > 0 ? page - 1 : 0, pageSize);
                break;
            case var value when string.Equals(value, AIInterviewDefaults.EmployerDashboardInvitesTabKey, StringComparison.Ordinal):
                model.Invites = await BuildEmployerDashboardInvitesTabModelAsync(customer, page, pageSize);
                break;
            default:
                model.Overview = await BuildVendorScoreboardModelAsync(customer, page, pageSize);
                break;
        }

        return model;
    }

    public async Task<IActionResult> EmployerDashboard(string tab = null, string candidateNameOrEmail = null, string status = null, decimal? minScore = null, decimal? maxScore = null, DateTime? startDate = null, DateTime? endDate = null, string interviewSort = "TopScorersFirst", bool onlyWithInterviewScore = false, int page = 1, int pageSize = DefaultEmployerApplicationsPageSize, int pageIndex = 0)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var activeTab = NormalizeEmployerDashboardTab(tab);
        var effectivePage = page > 0 ? page : pageIndex + 1;
        var effectivePageSize = pageSize > 0
            ? pageSize
            : string.Equals(activeTab, AIInterviewDefaults.EmployerDashboardApplicationsTabKey, StringComparison.Ordinal)
                ? DefaultEmployerApplicationsPageSize
                : DefaultEmployerDashboardTablePageSize;
        var applicationsModel = new ApplicationListModel
        {
            CandidateNameOrEmail = candidateNameOrEmail,
            Status = status,
            MinScore = minScore,
            MaxScore = maxScore,
            StartDate = startDate,
            EndDate = endDate,
            InterviewSort = string.IsNullOrWhiteSpace(interviewSort) ? "TopScorersFirst" : interviewSort,
            OnlyWithInterviewScore = onlyWithInterviewScore,
            Page = effectivePage,
            PageSize = effectivePageSize
        };

        var model = await BuildEmployerDashboardPageModelAsync(customer, activeTab, applicationsModel, effectivePage, effectivePageSize);
        ViewData["EmployerDashboardRouteValues"] = BuildEmployerDashboardRouteValues(model.ActiveTab, model.Applications);

        return View("~/Plugins/Misc.AIInterview/Views/EmployerDashboard.cshtml", model);
    }

    protected virtual async Task<bool> IsInstituteVendorAsync(Nop.Core.Domain.Customers.Customer customer)
    {
        if (customer == null || customer.VendorId <= 0)
            return false;
        return await _customerService.IsInCustomerRoleAsync(customer, "Institute", true);
    }

    protected static string BuildInstituteSlug(string vendorName)
    {
        return InstituteRegistrationSlugService.BuildSlug(vendorName);
    }

    protected virtual async Task<int> ResolveInstituteVendorIdFromRegistrationValueAsync(string registrationValue)
    {
        return await InstituteRegistrationSlugService.ResolveVendorIdAsync(
            _vendorService,
            registrationValue,
            _logger,
            nameof(AIInterviewController));
    }

    protected virtual async Task<IList<Customer>> GetInstituteStudentsAsync(int vendorId)
    {
        if (_genericAttributeRepository == null || vendorId <= 0)
            return new List<Customer>();

        var vendorIdStr = vendorId.ToString();
        var customerIds = (await _genericAttributeRepository.GetAllAsync(q =>
            q.Where(a =>
                a.KeyGroup == nameof(Customer) &&
                a.Key == AIInterviewDefaults.InstituteVendorIdAttributeKey &&
                a.Value == vendorIdStr)))
            .Select(a => a.EntityId)
            .Distinct()
            .ToList();

        if (!customerIds.Any())
            return new List<Customer>();

        var students = new List<Customer>();
        foreach (var id in customerIds)
        {
            var c = await _customerService.GetCustomerByIdAsync(id);
            if (c != null && !c.Deleted)
                students.Add(c);
        }
        return students.OrderBy(c => c.Email).ToList();
    }

    protected virtual string NormalizeInstituteDashboardTab(string tab)
    {
        return (tab ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            var value when string.Equals(value, AIInterviewDefaults.LegacyInstituteCreditsTabKey, StringComparison.Ordinal) => AIInterviewDefaults.InstituteDashboardTabKey,
            var value when string.Equals(value, AIInterviewDefaults.InstituteCandidatesTabKey, StringComparison.Ordinal) => AIInterviewDefaults.InstituteCandidatesTabKey,
            var value when string.Equals(value, AIInterviewDefaults.LegacyInstituteCandidatesTabKey, StringComparison.Ordinal) => AIInterviewDefaults.InstituteCandidatesTabKey,
            _ => AIInterviewDefaults.InstituteDashboardTabKey
        };
    }

    protected virtual int GetInstituteNavigationTab(string tab)
    {
        return AIInterviewDefaults.InstituteDashboardNavigationTab;
    }

    protected virtual async Task<string> BuildInstituteJoinUrlAsync(Customer customer)
    {
        var vendor = _vendorService != null
            ? await _vendorService.GetVendorByIdAsync(customer.VendorId)
            : null;
        var slug = vendor != null ? BuildInstituteSlug(vendor.Name) : "institute";
        if (string.IsNullOrWhiteSpace(slug))
            slug = "institute";

        if (!await InstituteRegistrationSlugService.IsSlugUniqueForVendorAsync(
            _vendorService,
            slug,
            customer.VendorId,
            _logger,
            nameof(BuildInstituteJoinUrlAsync)))
        {
            return string.Empty;
        }

        return $"{Request.Scheme}://{Request.Host}/register?{AIInterviewDefaults.InstituteRegistrationCookieName}={slug}";
    }

    protected virtual async Task<InstituteDashboardPageModel> BuildInstituteDashboardPageModelAsync(Customer customer, string tab, string transferMessage = null, bool transferSucceeded = false)
    {
        var activeTab = NormalizeInstituteDashboardTab(tab);
        var students = await GetInstituteStudentsAsync(customer.VendorId);
        var instituteWallet = await _creditService.GetOrCreateWalletAsync(customer.Id);
        var joinUrl = await BuildInstituteJoinUrlAsync(customer);
        var candidates = new List<InstituteCandidateModel>();
        var studentWalletIds = new List<int>();
        decimal studentCurrentBalances = 0;

        foreach (var student in students)
        {
            var wallet = await _creditService.GetOrCreateWalletAsync(student.Id);
            studentWalletIds.Add(wallet.Id);
            studentCurrentBalances += wallet.Balance;
            var fullName = (student.FirstName + " " + student.LastName).Trim();
            candidates.Add(new InstituteCandidateModel
            {
                CustomerId = student.Id,
                InviteId = student.Id,
                Email = student.Email ?? string.Empty,
                CustomerName = string.IsNullOrWhiteSpace(fullName) ? student.Email : fullName,
                IsAccepted = true,
                IsActive = student.Active,
                CreditBalance = wallet.Balance,
                CreatedOnUtc = student.CreatedOnUtc
            });
        }

        var consumedCredits = 0m;
        if (_creditLedgerRepository != null && studentWalletIds.Any())
        {
            var usageSources = new HashSet<string>(
                new[] { CreditLedgerSources.InterviewUsage, CreditLedgerSources.SponsorInterviewUsage },
                StringComparer.OrdinalIgnoreCase);
            var usageEntries = await _creditLedgerRepository.GetAllAsync(q =>
                q.Where(entry => studentWalletIds.Contains(entry.CreditWalletId)));
            consumedCredits = usageEntries
                .Where(entry =>
                    string.Equals(entry.TransactionType, "Withdrawal", StringComparison.OrdinalIgnoreCase) &&
                    usageSources.Contains(entry.LedgerSource ?? string.Empty))
                .Sum(entry => Math.Abs(entry.Amount));
        }

        var vendor = _vendorService != null
            ? await _vendorService.GetVendorByIdAsync(customer.VendorId)
            : null;
        var vendorName = customer.FirstName?.Trim() is { Length: > 0 } fn
            ? fn : customer.Email ?? string.Empty;

        return new InstituteDashboardPageModel
        {
            ActiveTab = activeTab,
            SelectedNavigationTab = GetInstituteNavigationTab(activeTab),
            VendorName = vendor?.Name ?? vendorName,
            JoinUrl = joinUrl,
            JoinUrlUnavailableMessage = string.IsNullOrWhiteSpace(joinUrl)
                ? "Applicant registration link is unavailable because this institute name does not produce a unique registration slug. Contact support or an administrator to update the institute name and generate a unique link."
                : null,
            AvailableCredits = instituteWallet.Balance,
            ConsumedCredits = consumedCredits,
            TotalCredits = instituteWallet.Balance + studentCurrentBalances + consumedCredits,
            Candidates = candidates,
            TransferMessage = transferMessage,
            TransferSucceeded = transferSucceeded
        };
    }

    protected virtual IActionResult InstituteTabResult(InstituteDashboardPageModel model)
    {
        ViewData["InstituteJoinUrl"] = model.JoinUrl;
        ViewData["InstituteVendorName"] = model.VendorName;

        if (IsHtmxRequest())
            return PartialView("~/Plugins/Misc.AIInterview/Views/Shared/_InstituteDashboardTabContent.cshtml", model);

        return View("~/Plugins/Misc.AIInterview/Views/InstituteDashboard.cshtml", model);
    }

    public virtual async Task<IActionResult> InstituteDashboard(string tab = null)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await IsInstituteVendorAsync(customer))
            return RedirectToRoute("Homepage");

        var model = await BuildInstituteDashboardPageModelAsync(customer, tab);
        return InstituteTabResult(model);
    }

    public virtual IActionResult InstituteCandidates()
    {
        return RedirectToRoute(AIInterviewDefaults.InstituteDashboardRouteName, new { tab = AIInterviewDefaults.InstituteCandidatesTabKey });
    }

    public virtual IActionResult InstituteCredits()
    {
        return RedirectToRoute(AIInterviewDefaults.InstituteDashboardRouteName, new { tab = AIInterviewDefaults.InstituteDashboardTabKey });
    }

    public virtual async Task<IActionResult> InstituteApplicantLedger(int applicantCustomerId, int page = 1, int pageSize = 12)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await IsInstituteVendorAsync(customer))
            return RedirectToRoute("Homepage");

        var applicants = await GetInstituteStudentsAsync(customer.VendorId);
        var applicant = applicants.FirstOrDefault(student => student.Id == applicantCustomerId);
        if (applicant == null)
            return NotFound();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var wallet = _creditService == null ? null : await _creditService.GetOrCreateWalletAsync(applicant.Id);
        var entries = wallet == null || _creditLedgerRepository == null
            ? new List<CreditLedgerEntry>()
            : (await _creditLedgerRepository.GetAllAsync(q =>
                q.Where(entry => entry.CreditWalletId == wallet.Id)
                    .OrderBy(entry => entry.CreatedOnUtc)
                    .ThenBy(entry => entry.Id)))
                .ToList();

        var runningBalance = 0m;
        var ledgerRows = entries.Select(entry =>
        {
            runningBalance += entry.Amount;
            return new InstituteApplicantLedgerRowModel
            {
                CreatedOnUtc = entry.CreatedOnUtc,
                Action = BuildInstituteLedgerAction(entry),
                Amount = entry.Amount,
                RunningBalance = runningBalance,
                Source = BuildInstituteLedgerSource(entry),
                Remarks = entry.Remarks ?? string.Empty
            };
        }).ToList();

        var totalRows = ledgerRows.Count;
        var totalPages = totalRows == 0 ? 1 : (int)Math.Ceiling(totalRows / (decimal)pageSize);
        page = Math.Min(page, totalPages);

        var fullName = (applicant.FirstName + " " + applicant.LastName).Trim();
        var model = new InstituteApplicantLedgerModalModel
        {
            ApplicantCustomerId = applicant.Id,
            ApplicantName = string.IsNullOrWhiteSpace(fullName) ? applicant.Email : fullName,
            ApplicantEmail = applicant.Email ?? string.Empty,
            CurrentBalance = wallet?.Balance ?? 0m,
            TotalDeposits = entries.Where(entry => entry.Amount > 0).Sum(entry => entry.Amount),
            TotalWithdrawals = Math.Abs(entries.Where(entry => entry.Amount < 0).Sum(entry => entry.Amount)),
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            TotalPages = totalPages,
            Filters = new InstituteApplicantLedgerFilterModel
            {
                ApplicantCustomerId = applicant.Id,
                Page = page,
                PageSize = pageSize
            },
            Rows = ledgerRows
                .OrderByDescending(row => row.CreatedOnUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList()
        };

        return PartialView("~/Plugins/Misc.AIInterview/Views/Shared/_InstituteApplicantLedgerModal.cshtml", model);
    }

    protected static string BuildInstituteLedgerAction(CreditLedgerEntry entry)
    {
        if (entry == null)
            return "Ledger entry";

        var isDeposit = string.Equals(entry.TransactionType, "Deposit", StringComparison.OrdinalIgnoreCase) || entry.Amount > 0;
        var isWithdrawal = string.Equals(entry.TransactionType, "Withdrawal", StringComparison.OrdinalIgnoreCase) || entry.Amount < 0;

        if (string.Equals(entry.LedgerSource, CreditLedgerSources.Adjustment, StringComparison.OrdinalIgnoreCase))
            return isDeposit ? "Allocated by institute" : isWithdrawal ? "Deallocated by institute" : "Institute adjustment";

        if (string.Equals(entry.LedgerSource, CreditLedgerSources.InterviewUsage, StringComparison.OrdinalIgnoreCase))
            return "Interview usage";

        if (string.Equals(entry.LedgerSource, CreditLedgerSources.SponsorInterviewUsage, StringComparison.OrdinalIgnoreCase))
            return "Sponsored interview usage";

        if (string.Equals(entry.LedgerSource, CreditLedgerSources.Order, StringComparison.OrdinalIgnoreCase))
            return "Credit purchase";

        if (string.Equals(entry.LedgerSource, CreditLedgerSources.AdminTopUp, StringComparison.OrdinalIgnoreCase))
            return "Admin top-up";

        return "Ledger entry";
    }

    protected static string BuildInstituteLedgerSource(CreditLedgerEntry entry)
    {
        if (!IsKnownInstituteLedgerSource(entry?.LedgerSource))
            return "-";

        return entry.LedgerSource;
    }

    protected static bool IsKnownInstituteLedgerSource(string source)
    {
        return string.Equals(source, CreditLedgerSources.Adjustment, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, CreditLedgerSources.InterviewUsage, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, CreditLedgerSources.SponsorInterviewUsage, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, CreditLedgerSources.Order, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, CreditLedgerSources.AdminTopUp, StringComparison.OrdinalIgnoreCase);
    }

    [HttpPost]
    public virtual async Task<IActionResult> InstituteCreditAllot(
        int selectedCandidateCustomerId, string transferAction, decimal amount, string remarks)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await IsInstituteVendorAsync(customer))
            return RedirectToRoute("Homepage");

        var students = await GetInstituteStudentsAsync(customer.VendorId);
        var candidateBelongsToInstitute = students.Any(student => student.Id == selectedCandidateCustomerId);
        var normalizedAction = (transferAction ?? string.Empty).Trim().ToLowerInvariant();
        var targetTab = AIInterviewDefaults.InstituteCandidatesTabKey;

        if (selectedCandidateCustomerId <= 0 || amount <= 0)
        {
            var validationModel = await BuildInstituteDashboardPageModelAsync(customer, targetTab,
                "Please select an applicant and enter a credit amount greater than zero.");
            return InstituteTabResult(validationModel);
        }

        if (!candidateBelongsToInstitute)
        {
            var validationModel = await BuildInstituteDashboardPageModelAsync(customer, targetTab,
                "Selected applicant does not belong to this institute.");
            return InstituteTabResult(validationModel);
        }

        if (!string.Equals(normalizedAction, "allocate", StringComparison.Ordinal) &&
            !string.Equals(normalizedAction, "deallocate", StringComparison.Ordinal))
        {
            var validationModel = await BuildInstituteDashboardPageModelAsync(customer, targetTab,
                "Please choose Allocate or Deallocate.");
            return InstituteTabResult(validationModel);
        }

        if (string.IsNullOrWhiteSpace(remarks))
        {
            var validationModel = await BuildInstituteDashboardPageModelAsync(customer, targetTab,
                "Comments are required.");
            return InstituteTabResult(validationModel);
        }

        var candidate = await _customerService.GetCustomerByIdAsync(selectedCandidateCustomerId);
        if (candidate == null)
        {
            var validationModel = await BuildInstituteDashboardPageModelAsync(customer, targetTab,
                "Selected applicant not found.");
            return InstituteTabResult(validationModel);
        }

        var comments = remarks.Trim();
        var effectiveRemarks = string.Equals(normalizedAction, "allocate", StringComparison.Ordinal)
            ? $"Institute allocation to applicant {candidate.Email}: {comments}"
            : $"Institute deallocation from applicant {candidate.Email}: {comments}";

        if (string.Equals(normalizedAction, "allocate", StringComparison.Ordinal))
        {
            var chargedInstitute = await _creditService.AuthorizeAndChargeAsync(
                customer.Id, amount, effectiveRemarks,
                CreditLedgerSources.Adjustment);

            if (!chargedInstitute)
            {
                var insufficientModel = await BuildInstituteDashboardPageModelAsync(customer, targetTab,
                    "Insufficient credits in your institute account. Please purchase more credits.");
                return InstituteTabResult(insufficientModel);
            }

            await _creditService.AddCreditAsync(
                candidate.Id, amount, effectiveRemarks,
                CreditLedgerSources.Adjustment);

            _notificationService.SuccessNotification(
                $"{amount:0.##} credit(s) allotted to applicant {candidate.Email} successfully.");
            var successModel = await BuildInstituteDashboardPageModelAsync(customer, targetTab,
                $"{amount:0.##} credit(s) allotted to applicant {candidate.Email} successfully.", true);
            return InstituteTabResult(successModel);
        }

        var chargedStudent = await _creditService.AuthorizeAndChargeAsync(
            candidate.Id, amount, effectiveRemarks,
            CreditLedgerSources.Adjustment);

        if (!chargedStudent)
        {
            var insufficientModel = await BuildInstituteDashboardPageModelAsync(customer, targetTab,
                "Insufficient credits in the applicant's account.");
            return InstituteTabResult(insufficientModel);
        }

        await _creditService.AddCreditAsync(
            customer.Id, amount, effectiveRemarks,
            CreditLedgerSources.Adjustment);

        _notificationService.SuccessNotification(
            $"{amount:0.##} credit(s) deallocated from applicant {candidate.Email} successfully.");
        var deallocateSuccessModel = await BuildInstituteDashboardPageModelAsync(customer, targetTab,
            $"{amount:0.##} credit(s) deallocated from applicant {candidate.Email} successfully.", true);
        return InstituteTabResult(deallocateSuccessModel);
    }

    public async Task<IActionResult> MyApplications(string sortOrder, string status = null, decimal? minScore = null, decimal? maxScore = null)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var model = await BuildMyApplicationsModelAsync(customer, sortOrder, status, minScore, maxScore);
        return View("~/Plugins/Misc.AIInterview/Views/MyApplications.cshtml", model);
    }

    public async Task<IActionResult> Report(int sessionId)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        if (!await _interviewSessionService.CanAccessReportAsync(customer.Id, sessionId))
            return Challenge();

        var session = await _interviewSessionService.GetInterviewSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrEmpty(session.ReportData))
        {
            _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Report.NotFound"));
            return RedirectToRoute(AIInterviewDefaults.MyApplicationsRouteName);
        }

        var model = await BuildInterviewReportModelAsync(session);

        return View("~/Plugins/Misc.AIInterview/Views/Report.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> ReportPanel(int sessionId)
    {
        if (!_aiInterviewSettings.Enabled)
            return NotFound();

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        if (!await _interviewSessionService.CanAccessReportAsync(customer.Id, sessionId))
            return Challenge();

        var session = await _interviewSessionService.GetInterviewSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrWhiteSpace(session.ReportData))
            return NotFound();

        var model = await BuildInterviewReportModelAsync(session);
        return PartialView("~/Plugins/Misc.AIInterview/Views/Shared/_ReportShareContent.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> Recording(int sessionId)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
        {
            _logger?.LogWarning("AI Interview recording access challenged. SessionId={SessionId}; Reason=current customer unavailable.", sessionId);
            return Challenge();
        }

        _logger?.LogInformation("AI Interview recording access requested. SessionId={SessionId}; CustomerId={CustomerId}.",
            sessionId,
            customer.Id);

        var canAccess = await _interviewSessionService.CanAccessReportAsync(customer.Id, sessionId);
        _logger?.LogInformation("AI Interview recording permission checked. SessionId={SessionId}; CustomerId={CustomerId}; CanAccess={CanAccess}.",
            sessionId,
            customer.Id,
            canAccess);
        if (!canAccess)
        {
            _logger?.LogWarning("AI Interview recording access denied. SessionId={SessionId}; CustomerId={CustomerId}.",
                sessionId,
                customer.Id);
            return Challenge();
        }

        var session = await _interviewSessionService.GetInterviewSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrWhiteSpace(session.RecordingUrl))
        {
            _logger?.LogWarning("AI Interview recording not found. SessionId={SessionId}; CustomerId={CustomerId}; SessionFound={SessionFound}; RecordingUrlConfigured={RecordingUrlConfigured}.",
                sessionId,
                customer.Id,
                session != null,
                !string.IsNullOrWhiteSpace(session?.RecordingUrl));
            return NotFound();
        }

        _logger?.LogInformation("AI Interview recording access accepted. SessionId={SessionId}; CustomerId={CustomerId}; RecordingUrl={RecordingUrl}.",
            sessionId,
            customer.Id,
            BuildSafeRecordingUrlLogValue(session.RecordingUrl));

        return await ProxyRecordingAsync(session.RecordingUrl);
    }

    [HttpGet]
    public async Task<IActionResult> RecordingShare(string token)
    {
        if (!_aiInterviewSettings.Enabled)
            return NotFound();

        _logger?.LogInformation("AI Interview recording share access requested. TokenConfigured={TokenConfigured}; TokenLength={TokenLength}.",
            !string.IsNullOrWhiteSpace(token),
            token?.Length ?? 0);

        var session = await _interviewSessionService.GetSessionByRecordingShareTokenAsync(token);
        if (session == null ||
            !session.RecordingShareEnabled ||
            string.IsNullOrWhiteSpace(session.RecordingShareToken) ||
            !string.Equals(session.RecordingShareToken, token, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(session.RecordingUrl))
        {
            _logger?.LogWarning("AI Interview recording share not found or unavailable. TokenConfigured={TokenConfigured}; SessionFound={SessionFound}; SessionId={SessionId}; ShareEnabled={ShareEnabled}; StoredTokenConfigured={StoredTokenConfigured}; TokenMatches={TokenMatches}; RecordingUrlConfigured={RecordingUrlConfigured}.",
                !string.IsNullOrWhiteSpace(token),
                session != null,
                session?.Id ?? 0,
                session?.RecordingShareEnabled ?? false,
                !string.IsNullOrWhiteSpace(session?.RecordingShareToken),
                session != null && string.Equals(session.RecordingShareToken, token, StringComparison.Ordinal),
                !string.IsNullOrWhiteSpace(session?.RecordingUrl));
            return NotFound();
        }

        _logger?.LogInformation("AI Interview recording share access accepted. SessionId={SessionId}; RecordingUrl={RecordingUrl}.",
            session.Id,
            BuildSafeRecordingUrlLogValue(session.RecordingUrl));

        return await ProxyRecordingAsync(session.RecordingUrl);
    }

    [HttpGet]
    public async Task<IActionResult> ReportShare(string token)
    {
        if (!_aiInterviewSettings.Enabled)
            return NotFound();

        var session = await _interviewSessionService.GetSessionByReportShareTokenAsync(token);
        if (session == null ||
            !session.ReportShareEnabled ||
            string.IsNullOrWhiteSpace(session.ReportShareToken) ||
            !string.Equals(session.ReportShareToken, token, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(session.ReportData) ||
            (string.IsNullOrWhiteSpace(session.ReportData) && string.IsNullOrWhiteSpace(session.RecordingUrl)))
        {
            return NotFound();
        }

        var model = await BuildInterviewReportModelAsync(session);
        return View("~/Plugins/Misc.AIInterview/Views/ReportShare.cshtml", model);
    }

    public async Task<IActionResult> Interview(string sessionKey)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var session = await _interviewSessionService.GetSessionBySessionKeyAsync(sessionKey);
        if (session == null || session.CustomerId != customer.Id || session.CompletedOnUtc.HasValue)
            return RedirectToRoute(AIInterviewDefaults.IndexRouteName);

        return RedirectToRoute(AIInterviewDefaults.MockRuntimeRouteName, new { token = session.Token });
    }

    protected virtual string BuildRecordingPlaybackUrl(string recordingUrl)
    {
        if (string.IsNullOrWhiteSpace(recordingUrl))
        {
            _logger?.LogWarning("AI Interview recording playback URL validation failed. Reason=missing recording URL; ContainerUrlConfigured={ContainerUrlConfigured}; SasTokenConfigured={SasTokenConfigured}.",
                !string.IsNullOrWhiteSpace(_aiInterviewSettings?.AzureBlobStorageContainerUrl),
                !string.IsNullOrWhiteSpace(_aiInterviewSettings?.AzureBlobStorageSasToken));
            return null;
        }

        if (string.IsNullOrWhiteSpace(_aiInterviewSettings?.AzureBlobStorageContainerUrl) ||
            string.IsNullOrWhiteSpace(_aiInterviewSettings?.AzureBlobStorageSasToken))
        {
            _logger?.LogWarning("AI Interview recording playback URL validation failed. Reason=missing Azure Blob configuration; RecordingUrl={RecordingUrl}; ContainerUrlConfigured={ContainerUrlConfigured}; SasTokenConfigured={SasTokenConfigured}.",
                BuildSafeRecordingUrlLogValue(recordingUrl),
                !string.IsNullOrWhiteSpace(_aiInterviewSettings?.AzureBlobStorageContainerUrl),
                !string.IsNullOrWhiteSpace(_aiInterviewSettings?.AzureBlobStorageSasToken));
            return null;
        }

        if (!Uri.TryCreate(_aiInterviewSettings.AzureBlobStorageContainerUrl.Trim(), UriKind.Absolute, out var containerUri) ||
            !Uri.TryCreate(recordingUrl.Trim(), UriKind.Absolute, out var recordingUri))
        {
            _logger?.LogWarning("AI Interview recording playback URL validation failed. Reason=invalid absolute URL; RecordingUrl={RecordingUrl}; ContainerUrl={ContainerUrl}.",
                BuildSafeRecordingUrlLogValue(recordingUrl),
                BuildSafeRecordingUrlLogValue(_aiInterviewSettings.AzureBlobStorageContainerUrl));
            return null;
        }

        if (!string.IsNullOrWhiteSpace(recordingUri.Query) || !string.IsNullOrWhiteSpace(recordingUri.Fragment))
        {
            _logger?.LogWarning("AI Interview recording playback URL validation failed. Reason=recording URL contains query or fragment; RecordingUrl={RecordingUrl}; HasQuery={HasQuery}; HasFragment={HasFragment}.",
                BuildSafeRecordingUrlLogValue(recordingUrl),
                !string.IsNullOrWhiteSpace(recordingUri.Query),
                !string.IsNullOrWhiteSpace(recordingUri.Fragment));
            return null;
        }

        if (!string.Equals(containerUri.Scheme, recordingUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(containerUri.Host, recordingUri.Host, StringComparison.OrdinalIgnoreCase) ||
            containerUri.Port != recordingUri.Port)
        {
            _logger?.LogWarning("AI Interview recording playback URL validation failed. Reason=recording URL host does not match configured container; RecordingUrl={RecordingUrl}; ContainerUrl={ContainerUrl}.",
                BuildSafeRecordingUrlLogValue(recordingUrl),
                BuildSafeRecordingUrlLogValue(_aiInterviewSettings.AzureBlobStorageContainerUrl));
            return null;
        }

        var containerPath = containerUri.AbsolutePath.TrimEnd('/');
        var recordingPath = recordingUri.AbsolutePath.TrimEnd('/');
        var isMatchingContainer = string.Equals(recordingPath, containerPath, StringComparison.OrdinalIgnoreCase) ||
            recordingPath.StartsWith(containerPath + "/", StringComparison.OrdinalIgnoreCase);
        if (!isMatchingContainer)
        {
            _logger?.LogWarning("AI Interview recording playback URL validation failed. Reason=recording URL path is outside configured container; RecordingPath={RecordingPath}; ContainerPath={ContainerPath}.",
                recordingPath,
                containerPath);
            return null;
        }

        var sasToken = _aiInterviewSettings.AzureBlobStorageSasToken.Trim();
        if (!sasToken.StartsWith("?", StringComparison.Ordinal))
            sasToken = sasToken.StartsWith("&", StringComparison.Ordinal) ? "?" + sasToken[1..] : "?" + sasToken;

        var playbackUrl = $"{recordingUri.GetLeftPart(UriPartial.Path).TrimEnd('/')}{sasToken}";
        _logger?.LogInformation("AI Interview recording playback URL validation succeeded. RecordingUrl={RecordingUrl}; PlaybackUrl={PlaybackUrl}; SasTokenAppended={SasTokenAppended}.",
            BuildSafeRecordingUrlLogValue(recordingUrl),
            BuildSafeRecordingUrlLogValue(playbackUrl),
            true);

        return playbackUrl;
    }

    protected static string BuildSafeRecordingUrlLogValue(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return uri.GetLeftPart(UriPartial.Path);

        const int maxLength = 300;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed class ProxyResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;

        public ProxyResponseStream(Stream inner, HttpResponseMessage response)
        {
            _inner = inner;
            _response = response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    }

    public async Task<IActionResult> Apply(string jobTitle, int productId = 0)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        if (productId > 0)
        {
            var product = await _productService.GetProductByIdAsync(productId);
            if (product != null)
            {
                if (_jobProductAccessService != null && !await _jobProductAccessService.CanAcceptJobApplicationsAsync(product))
                    return NotFound();

                var redirectUrl = await BuildProductRedirectUrlAsync(product, new Dictionary<string, string>
                {
                    ["jobTitle"] = jobTitle
                });
                if (!string.IsNullOrWhiteSpace(redirectUrl))
                    return Redirect(redirectUrl);
            }
        }

        return RedirectToRoute("Homepage");
    }

    [HttpPost]
    public async Task<IActionResult> Apply(ApplyModel model)
    {
        var result = await SubmitApplicationAsync(model);
        if (!result.Success)
        {
            if (result.RequiresLogin && !string.IsNullOrWhiteSpace(result.RedirectUrl))
                return Redirect(result.RedirectUrl);

            if (result.StatusCode == 404)
                return NotFound();

            await PopulateApplyModelAsync(model);
            return View("~/Plugins/Misc.AIInterview/Views/Apply.cshtml", model);
        }

        _notificationService.SuccessNotification(result.Message);
        return RedirectToRoute(AIInterviewDefaults.MyApplicationsRouteName);
    }

    [HttpPost]
    public async Task<IActionResult> ApplyInline(ApplyModel model)
    {
        var result = await SubmitApplicationAsync(model);
        if (!result.Success)
        {
            if (result.StatusCode > 0)
                Response.StatusCode = result.StatusCode;

            return Json(new { success = false, error = result.Message, redirect = result.RedirectUrl, requiresLogin = result.RequiresLogin });
        }

        return Json(new { success = true, message = result.Message });
    }

    protected virtual async Task<ApplySubmissionResult> SubmitApplicationAsync(ApplyModel model)
    {
        if (!_aiInterviewSettings.Enabled)
            return new ApplySubmissionResult
            {
                Success = false,
                Message = await _localizationService.GetResourceAsync("Common.NotAvailable")
            };

        var customer = await _workContext.GetCurrentCustomerAsync();
        var isRegisteredCustomer = customer != null &&
            await _customerService.IsRegisteredAsync(customer) &&
            !string.IsNullOrWhiteSpace(customer.Email);
        if (!isRegisteredCustomer)
            return new ApplySubmissionResult
            {
                Success = false,
                Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized"),
                RequiresLogin = true,
                RedirectUrl = BuildLoginRedirectUrl(model)
            };

        var applications = await GetApplicationsForJobAsync(customer.Id, model.ProductId, model.JobTitle);
        if (applications.Any(a => !JobApplicationStatuses.CanReapply(a.Status)))
        {
            var message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.AlreadyApplied");
            _notificationService.WarningNotification(message);
            return new ApplySubmissionResult { Success = false, Message = message };
        }

        if (string.IsNullOrWhiteSpace(model.JobTitle))
            ModelState.AddModelError(nameof(model.JobTitle), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.JobTitle.Required"));

        var product = model.ProductId > 0 ? await _productService.GetProductByIdAsync(model.ProductId) : null;
        if (_jobProductAccessService != null && !await _jobProductAccessService.CanAcceptJobApplicationsAsync(product))
            return new ApplySubmissionResult
            {
                Success = false,
                Message = await _localizationService.GetResourceAsync("Common.NotAvailable"),
                StatusCode = 404
            };

        var allApplications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id) ?? new List<JobApplication>();
        var ownedResumeDownloadIds = ResumeSelectionHelper.GetOwnedResumeDownloadIds(allApplications);
        var hasSelectedExistingResume = model.SelectedResumeDownloadId > 0;
        var validSelectedExistingResume = hasSelectedExistingResume && ownedResumeDownloadIds.Contains(model.SelectedResumeDownloadId);

        var jobRequirements = _jobRequirementService == null
            ? new JobRequirementsModel()
            : await _jobRequirementService.GetRequirementsAsync(model.ProductId);
        model.ResumeRequired = jobRequirements.ResumeRequired;
        model.AvailableResumes = await ResumeSelectionHelper.BuildResumeSelectListAsync(allApplications, _downloadService, model.SelectedResumeDownloadId, _dateTimeHelper);

        if (model.ResumeFile == null)
        {
            if (hasSelectedExistingResume)
            {
                if (!validSelectedExistingResume)
                    ModelState.AddModelError(nameof(model.SelectedResumeDownloadId), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.PreviousResume.Invalid"));

                ModelState.Remove(nameof(model.ResumeFile));
            }
            else if (jobRequirements.ResumeRequired && !ModelState.ContainsKey(nameof(model.ResumeFile)))
            {
                ModelState.AddModelError(nameof(model.ResumeFile), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Required"));
            }
        }
        else
        {
            var validation = _resumeFileService?.ValidateResumeFile(model.ResumeFile)
                ?? new ResumeFileValidationResult
                {
                    Success = (Path.GetExtension(model.ResumeFile.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(model.ResumeFile.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase)) &&
                        model.ResumeFile.Length <= 5 * 1024 * 1024,
                    ErrorMessage = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid")
                };

            if (!validation.Success)
                ModelState.AddModelError(nameof(model.ResumeFile), string.IsNullOrWhiteSpace(validation.ErrorMessage) ? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid") : validation.ErrorMessage);
        }

        if (!ModelState.IsValid)
            return new ApplySubmissionResult
            {
                Success = false,
                Message = ModelState.Values.SelectMany(value => value.Errors)
                    .Select(error => error.ErrorMessage)
                    .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
                    ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.Error")
            };

        if (jobRequirements.InterviewRequired)
        {
            var legacyApplicationIds = allApplications
                .Where(application => application.ProductId == model.ProductId)
                .Select(application => application.Id)
                .ToList();
            var completedSessions = ((await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id)) ?? new List<InterviewSession>())
                .Where(s => s.CompletedOnUtc.HasValue &&
                    (s.ProductId == model.ProductId ||
                        (s.ProductId == 0 && legacyApplicationIds.Contains(s.JobApplicationId))))
                .ToList();
            if (!completedSessions.Any())
            {
                var message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.InterviewRequired");
                _notificationService.ErrorNotification(message);
                return new ApplySubmissionResult { Success = false, Message = message };
            }

            var highestScore = await _interviewSessionService.GetHighestScoreByCustomerIdAndProductIdAsync(customer.Id, model.ProductId);
            if (highestScore < jobRequirements.MinimumScore)
            {
                var message = string.Format(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.MinimumScoreNotReached"), jobRequirements.MinimumScore);
                _notificationService.ErrorNotification(message);
                return new ApplySubmissionResult { Success = false, Message = message };
            }
        }

        var resumeDownloadId = validSelectedExistingResume ? model.SelectedResumeDownloadId : 0;
        if (model.ResumeFile != null)
        {
            if (_resumeFileService != null)
            {
                var storedResume = await _resumeFileService.StoreResumeAsync(model.ResumeFile);
                if (!storedResume.Success)
                {
                    return new ApplySubmissionResult
                    {
                        Success = false,
                        Message = string.IsNullOrWhiteSpace(storedResume.ErrorMessage)
                            ? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid")
                            : storedResume.ErrorMessage
                    };
                }

                resumeDownloadId = storedResume.DownloadId;
            }
            else
            {
                var download = new Download
                {
                    DownloadGuid = Guid.NewGuid(),
                    UseDownloadUrl = false,
                    DownloadBinary = await _downloadService.GetDownloadBitsAsync(model.ResumeFile),
                    ContentType = model.ResumeFile.ContentType,
                    Filename = model.ResumeFile.FileName,
                    Extension = Path.GetExtension(model.ResumeFile.FileName),
                    IsNew = true
                };
                await _downloadService.InsertDownloadAsync(download);
                resumeDownloadId = download.Id;
            }
        }

        var jobApplication = new JobApplication
        {
            CustomerId = customer.Id,
            ProductId = model.ProductId,
            JobTitle = model.JobTitle,
            ResumeDownloadId = resumeDownloadId,
            Status = JobApplicationStatuses.Applied,
            CreatedOnUtc = DateTime.UtcNow
        };
        await _applicationService.InsertJobApplicationAsync(jobApplication);
        if (jobApplication.ResumeDownloadId > 0 && _resumeProfileService != null)
            await _resumeProfileService.EnsureResumeProfileAsync(jobApplication, product, forceRegenerate: model.ResumeFile != null);

        await _applicationService.SendApplicationSubmittedNotificationAsync(jobApplication, (await _workContext.GetWorkingLanguageAsync()).Id);
        return new ApplySubmissionResult
        {
            Success = true,
            Message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Apply.Success")
        };
    }

    protected virtual async Task PopulateApplyModelAsync(ApplyModel model)
    {
        if (model == null)
            return;

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return;

        var applications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id) ?? new List<JobApplication>();
        var jobRequirements = _jobRequirementService == null
            ? new JobRequirementsModel()
            : await _jobRequirementService.GetRequirementsAsync(model.ProductId);

        model.ResumeRequired = jobRequirements.ResumeRequired;
        model.AvailableResumes = await ResumeSelectionHelper.BuildResumeSelectListAsync(applications, _downloadService, model.SelectedResumeDownloadId, _dateTimeHelper);
    }

    protected async Task<bool> IsAuthorizedForEmployerActionsAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        return customer != null && (await _customerService.IsAdminAsync(customer) || customer.VendorId > 0);
    }

    protected virtual string BuildLoginRedirectUrl(ApplyModel model)
    {
        var returnUrl = Request?.Headers?.Referer.ToString();
        if (string.IsNullOrWhiteSpace(returnUrl))
            returnUrl = Url?.RouteUrl(AIInterviewDefaults.ApplyRouteName, new { productId = model?.ProductId, jobTitle = model?.JobTitle });

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            var query = new Dictionary<string, string>();
            if (model?.ProductId > 0)
                query["productId"] = model.ProductId.ToString();
            if (!string.IsNullOrWhiteSpace(model?.JobTitle))
                query["jobTitle"] = model.JobTitle;

            returnUrl = query.Count > 0
                ? QueryHelpers.AddQueryString("/aiinterview/apply", query)
                : "/aiinterview/apply";
        }

        return Url?.RouteUrl(NopRouteNames.General.LOGIN, new { returnUrl })
            ?? QueryHelpers.AddQueryString("/login", "returnUrl", returnUrl);
    }

    protected async Task<SpecificationAttribute> GetSpecificationAttributeByNameAsync(params string[] names)
    {
        if (_specificationAttributeService == null)
            return null;

        foreach (var name in names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var attributes = await _specificationAttributeService.GetSpecificationAttributesByNameAsync(name, 0, 10);
            var attribute = attributes.FirstOrDefault(specificationAttribute =>
                string.Equals(AIInterviewJobDisplayService.NormalizeSpecificationAttributeName(specificationAttribute.Name), AIInterviewJobDisplayService.NormalizeSpecificationAttributeName(name), StringComparison.OrdinalIgnoreCase));
            if (attribute != null)
                return attribute;
        }

        var allAttributes = await _specificationAttributeService.GetAllSpecificationAttributesAsync(pageSize: int.MaxValue);
        return allAttributes.FirstOrDefault(specificationAttribute =>
            names.Any(name => string.Equals(AIInterviewJobDisplayService.NormalizeSpecificationAttributeName(specificationAttribute.Name), AIInterviewJobDisplayService.NormalizeSpecificationAttributeName(name), StringComparison.OrdinalIgnoreCase)));
    }

    protected static string[] GetExperienceLevelAttributeAliases() => AIInterviewJobDisplayService.ExperienceLevelAliases;

    protected static string[] GetWorkArrangementAttributeAliases() => AIInterviewJobDisplayService.WorkArrangementAliases;

    protected static string[] GetEmploymentTypeAttributeAliases() => AIInterviewJobDisplayService.EmploymentTypeAliases;

    protected static string[] GetJobLocationAttributeAliases() => AIInterviewJobDisplayService.JobLocationAliases;

    protected static string[] GetSalaryRangeAttributeAliases() => AIInterviewJobDisplayService.SalaryRangeAliases;

    protected async Task<SpecificationAttribute> GetExperienceLevelSpecificationAttributeAsync()
    {
        return await GetSpecificationAttributeByNameAsync(GetExperienceLevelAttributeAliases());
    }

    protected async Task<SpecificationAttribute> GetWorkArrangementSpecificationAttributeAsync()
    {
        return await GetSpecificationAttributeByNameAsync(GetWorkArrangementAttributeAliases());
    }

    protected async Task<SpecificationAttribute> GetEmploymentTypeSpecificationAttributeAsync()
    {
        return await GetSpecificationAttributeByNameAsync(GetEmploymentTypeAttributeAliases());
    }

    protected async Task<SpecificationAttribute> GetJobLocationSpecificationAttributeAsync()
    {
        return await GetSpecificationAttributeByNameAsync(GetJobLocationAttributeAliases());
    }

    protected async Task<int> ResolveSalaryRangeSpecificationOptionIdAsync()
    {
        return await ResolveCustomTextSpecificationOptionIdAsync(GetSalaryRangeAttributeAliases());
    }

    protected async Task<int> ResolveCustomTextSpecificationOptionIdAsync(params string[] attributeNames)
    {
        var attribute = await GetSpecificationAttributeByNameAsync(attributeNames);
        if (attribute == null)
            return 0;

        var options = await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(attribute.Id);
        return options.FirstOrDefault(option => string.Equals(option.Name, "Value", StringComparison.OrdinalIgnoreCase))?.Id
            ?? options.FirstOrDefault()?.Id
            ?? 0;
    }

    protected async Task PrepareVendorJobModelAsync(VendorJobModel model)
    {
        if (model == null)
            return;

        var selectText = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Select");
        model.AvailableSalaryLpaOptions = BuildSalaryLpaSelectList(selectText);
        EnsureSalaryLpaOption(model.AvailableSalaryLpaOptions, model.SalaryMinCtcPa);
        EnsureSalaryLpaOption(model.AvailableSalaryLpaOptions, model.SalaryMaxCtcPa);

        if (_specificationAttributeService == null)
            return;

        IList<SelectListItem> BuildSelectList(IEnumerable<SpecificationAttributeOption> options, int? selectedId)
        {
            var items = new List<SelectListItem>
            {
                new() { Text = selectText, Value = string.Empty }
            };

            items.AddRange(options.Select(option => new SelectListItem
            {
                Text = option.Name,
                Value = option.Id.ToString(),
                Selected = selectedId.HasValue && option.Id == selectedId.Value
            }));

            return items;
        }

        var experienceAttribute = await GetExperienceLevelSpecificationAttributeAsync();
        if (experienceAttribute != null)
            model.AvailableExperienceLevels = BuildSelectList(
                await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(experienceAttribute.Id),
                model.ExperienceLevelOptionId);

        var workModeAttribute = await GetWorkArrangementSpecificationAttributeAsync();
        if (workModeAttribute != null)
            model.AvailableWorkModes = BuildSelectList(
                await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(workModeAttribute.Id),
                model.WorkModeOptionId);

        var employmentTypeAttribute = await GetEmploymentTypeSpecificationAttributeAsync();
        if (employmentTypeAttribute != null)
            model.AvailableEmploymentTypes = BuildSelectList(
                await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(employmentTypeAttribute.Id),
                model.EmploymentTypeOptionId);

        var jobLocationAttribute = await GetJobLocationSpecificationAttributeAsync();
        if (jobLocationAttribute != null)
            model.AvailableJobLocations = BuildSelectList(
                await _specificationAttributeService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(jobLocationAttribute.Id),
                model.JobLocationOptionId);
    }

    protected virtual IList<SelectListItem> BuildSalaryLpaSelectList(string selectText)
    {
        var items = new List<SelectListItem>
        {
            new() { Text = selectText, Value = string.Empty }
        };

        var salarySteps = new SortedSet<decimal>();

        for (var lpa = 0m; lpa <= 15m; lpa += 0.5m)
            salarySteps.Add(lpa);

        for (var lpa = 16m; lpa <= 30m; lpa += 1m)
            salarySteps.Add(lpa);

        for (var lpa = 32m; lpa <= 60m; lpa += 2m)
            salarySteps.Add(lpa);

        for (var lpa = 65m; lpa <= 100m; lpa += 5m)
            salarySteps.Add(lpa);

        foreach (var lpa in salarySteps)
        {
            items.Add(new SelectListItem
            {
                Text = $"{lpa:0.##} LPA",
                Value = (lpa * 100000m).ToString("0", CultureInfo.InvariantCulture)
            });
        }

        return items;
    }

    protected virtual void EnsureSalaryLpaOption(IList<SelectListItem> options, decimal? ctcPa)
    {
        if (options == null || !ctcPa.HasValue || ctcPa.Value < 0)
            return;

        if (options.Any(option => decimal.TryParse(option.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedValue) && parsedValue == ctcPa.Value))
            return;

        var lpa = ctcPa.Value / 100000m;
        var newOption = new SelectListItem
        {
            Text = $"{lpa:0.##} LPA",
            Value = ctcPa.Value.ToString("0", CultureInfo.InvariantCulture)
        };

        var insertIndex = options
            .Select((option, index) => new
            {
                Index = index,
                Value = decimal.TryParse(option.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedValue)
                    ? parsedValue
                    : decimal.MaxValue
            })
            .FirstOrDefault(entry => entry.Value > ctcPa.Value)?.Index ?? options.Count;

        options.Insert(insertIndex, newOption);
    }

    protected async Task InsertProductSpecificationAttributeAsync(int productId, int optionId, SpecificationAttributeType attributeType = SpecificationAttributeType.Option, string customValue = null, int displayOrder = 0)
    {
        if (_specificationAttributeService == null || optionId <= 0)
            return;

        await _specificationAttributeService.InsertProductSpecificationAttributeAsync(new ProductSpecificationAttribute
        {
            ProductId = productId,
            SpecificationAttributeOptionId = optionId,
            AttributeType = attributeType,
            CustomValue = customValue,
            ShowOnProductPage = true,
            AllowFiltering = false,
            DisplayOrder = displayOrder
        });
    }

    protected virtual async Task<VendorScoreboardModel> BuildVendorScoreboardModelAsync(Customer customer, int page = 1, int pageSize = DefaultEmployerDashboardTablePageSize)
    {
        var isAdmin = await _customerService.IsAdminAsync(customer);
        var vendorId = isAdmin ? 0 : customer.VendorId;
        var applications = await _applicationService.GetApplicationsAsync(vendorId: vendorId);
        var products = await _productService.SearchProductsAsync(vendorId: vendorId, showHidden: true);
        var completedScores = new List<decimal>();
        var customers = (await _customerService.GetCustomersByIdsAsync(applications.Select(application => application.CustomerId).Distinct().ToArray()))?.ToList()
            ?? new List<Customer>();
        var unknownCandidate = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown");

        foreach (var application in applications)
        {
            var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(application.CustomerId);
            completedScores.AddRange(sessions
                .Where(s => SessionMatchesApplication(s, application) && s.CompletedOnUtc.HasValue)
                .Select(s => s.Score));
        }

        var recentApplications = await Task.WhenAll(applications
            .OrderByDescending(application => application.CreatedOnUtc)
            .Select(async application =>
            {
                var appCustomer = customers.FirstOrDefault(entry => entry.Id == application.CustomerId);
                var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(application.CustomerId);
                var appSessions = sessions.Where(session => SessionMatchesApplication(session, application)).ToList();
                var latestCompletedSession = appSessions
                    .Where(session => session.CompletedOnUtc.HasValue)
                    .OrderByDescending(session => session.CompletedOnUtc)
                    .ThenByDescending(session => session.Id)
                    .FirstOrDefault();
                var normalizedStatus = JobApplicationStatuses.Normalize(application.Status);
                var candidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : string.Empty;

                return new ApplicationModel
                {
                    Id = application.Id,
                    JobTitle = application.JobTitle,
                    CandidateName = string.IsNullOrWhiteSpace(candidateName) ? unknownCandidate : candidateName,
                    CandidateEmail = appCustomer?.Email ?? unknownCandidate,
                    RawStatus = normalizedStatus,
                    Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{normalizedStatus}"),
                    InterviewScore = latestCompletedSession?.Score,
                    InterviewReportUrl = latestCompletedSession != null ? Url?.Action("Report", "AIInterview", new { sessionId = latestCompletedSession.Id }) : null,
                    RecordingUrl = latestCompletedSession?.RecordingUrl,
                    CreatedOn = application.CreatedOnUtc,
                    CompletedOn = latestCompletedSession?.CompletedOnUtc
                };
            }));

        var pagedApplications = ApplyInMemoryPaging(
            recentApplications.ToList(),
            page,
            pageSize,
            DefaultEmployerDashboardTablePageSize,
            out var normalizedPage,
            out var normalizedPageSize,
            out var totalCount,
            out var totalPages);

        return new VendorScoreboardModel
        {
            TotalJobs = products?.Count ?? 0,
            TotalApplications = applications.Count,
            CompletedInterviews = completedScores.Count,
            ShortlistedApplications = applications.Count(application =>
                string.Equals(JobApplicationStatuses.Normalize(application.Status), JobApplicationStatuses.Shortlisted, StringComparison.OrdinalIgnoreCase)),
            ActiveFlaggedViolations = applications.Count(application =>
            {
                var normalizedStatus = JobApplicationStatuses.Normalize(application.Status);
                return string.Equals(normalizedStatus, JobApplicationStatuses.Rejected, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedStatus, JobApplicationStatuses.Withdrawn, StringComparison.OrdinalIgnoreCase);
            }),
            AverageScore = completedScores.Any() ? completedScores.Average() : null,
            HighestScore = completedScores.Any() ? completedScores.Max() : null,
            Page = normalizedPage,
            PageSize = normalizedPageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            RecentApplications = pagedApplications
        };
    }

    protected virtual async Task<ApplicationListModel> BuildEmployerApplicationsModelAsync(Customer customer, ApplicationListModel model, int pageIndex = 0, int pageSize = DefaultEmployerApplicationsPageSize)
    {
        var isEmployer = !await _customerService.IsAdminAsync(customer) && customer.VendorId > 0;
        var (startDateUtc, endDateUtc) = await ConvertApplicationFilterDatesToUtcAsync(model.StartDate, model.EndDate);
        var currentPage = model.Page > 0 ? model.Page : pageIndex + 1;
        var effectivePageSize = model.PageSize > 0 ? model.PageSize : pageSize;

        // Keep dashboard filtering accurate by enriching the employer-owned result set first, then paging in memory.
        var applications = await _applicationService.GetApplicationsAsync(
            candidateNameOrEmail: model.CandidateNameOrEmail,
            status: model.Status,
            minScore: model.MinScore,
            maxScore: model.MaxScore,
            startDate: startDateUtc,
            endDate: endDateUtc,
            vendorId: isEmployer ? customer.VendorId : 0,
            pageIndex: 0,
            pageSize: int.MaxValue,
            sortByScore: model.SortByScore);
        var applicationItems = applications?.ToList() ?? new List<JobApplication>();

        var customerIds = applicationItems.Select(application => application.CustomerId).Distinct().ToList();
        var customers = await _customerService.GetCustomersByIdsAsync(customerIds.ToArray()) ?? new List<Customer>();

        model.Applications = await Task.WhenAll(applicationItems.Select(async application =>
        {
            var appCustomer = customers.FirstOrDefault(entry => entry.Id == application.CustomerId);
            var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(application.CustomerId) ?? new List<InterviewSession>();
            var appSessions = sessions.Where(session => SessionMatchesApplication(session, application)).ToList();
            var session = appSessions
                .Where(entry => entry.CompletedOnUtc.HasValue)
                .OrderByDescending(entry => entry.CompletedOnUtc)
                .ThenByDescending(entry => entry.Id)
                .FirstOrDefault();
            var normalizedStatus = JobApplicationStatuses.Normalize(application.Status);
            var questionScores = ParseQuestionScores(session?.QuestionScores);
            var reportSections = SplitReportSections(session?.ReportData);

            return new ApplicationModel
            {
                Id = application.Id,
                JobTitle = application.JobTitle,
                CandidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown"),
                CandidateEmail = appCustomer?.Email ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown"),
                CandidatePhone = string.IsNullOrWhiteSpace(appCustomer?.Phone) ? "+1 555 201 001" : appCustomer.Phone,
                Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{normalizedStatus}"),
                RawStatus = normalizedStatus,
                StatusComment = application.StatusComment,
                ResumeDownloadId = application.ResumeDownloadId,
                HasResume = application.ResumeDownloadId > 0,
                ResumeDownloadUrl = application.ResumeDownloadId > 0 ? Url.RouteUrl(AIInterviewDefaults.EmployerDownloadResumeRouteName, new { applicationId = application.Id }) : null,
                InterviewScore = session?.Score,
                InterviewReportUrl = session != null ? Url.Action("Report", "AIInterview", new { sessionId = session.Id }) : null,
                InterviewReportPanelUrl = session != null ? BuildReportPanelUrl(session.Id) : null,
                CreatedOn = application.CreatedOnUtc,
                AttemptCount = appSessions.Count,
                CompletedOn = session?.CompletedOnUtc,
                ChargeMode = await GetEmployerChargeModeLabelAsync(session != null && session.SponsorInviteId > 0),
                PromptSource = string.IsNullOrWhiteSpace(_aiInterviewSettings.Provider) && string.IsNullOrWhiteSpace(_aiInterviewSettings.Model)
                    ? "Resume-backed template"
                    : $"Provider: {_aiInterviewSettings.Provider}, Model: {_aiInterviewSettings.Model}",
                CoverMessage = !string.IsNullOrWhiteSpace(application.StatusComment)
                    ? application.StatusComment
                    : $"Applied for {application.JobTitle} with a resume-backed profile and interview history.",
                QuestionScores = session?.QuestionScores,
                QuestionScoreValues = questionScores,
                ReportSummary = reportSections.Summary,
                FeedbackSummary = reportSections.Feedback
            };
        }));

        model.Applications = ApplyEmployerInterviewFiltersAndSorting(model.Applications, model);

        if (!string.IsNullOrWhiteSpace(model.JobTitleOrKeyword))
        {
            model.Applications = model.Applications
                .Where(application => (application.JobTitle ?? string.Empty).Contains(model.JobTitleOrKeyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        model.Applications = ApplyInMemoryPaging(
            model.Applications,
            currentPage,
            effectivePageSize,
            DefaultEmployerApplicationsPageSize,
            out var normalizedPage,
            out var normalizedPageSize,
            out var totalCount,
            out var totalPages);
        model.Page = normalizedPage;
        model.PageSize = normalizedPageSize;
        model.TotalCount = totalCount;
        model.TotalPages = totalPages;

        return model;
    }

    protected virtual async Task<EmployerDashboardJobsTabModel> BuildEmployerDashboardJobsTabModelAsync(Customer customer, int page = 1, int pageSize = DefaultEmployerDashboardTablePageSize)
    {
        var model = new EmployerDashboardJobsTabModel();
        if (_jobRequirementService == null || _productService == null)
            return model;

        var isAdmin = await _customerService.IsAdminAsync(customer);
        var vendorId = isAdmin ? 0 : customer.VendorId;
        var products = await _productService.SearchProductsAsync(vendorId: vendorId, showHidden: true, pageSize: int.MaxValue)
            ?? new PagedList<Product>(new List<Product>(), 0, 1, 1);

        foreach (var product in products.OrderByDescending(product => product.CreatedOnUtc))
        {
            if (product == null || product.Deleted || !await _jobRequirementService.IsJobProductAsync(product))
                continue;

            var salarySnapshot = _aiInterviewJobDisplayService == null
                ? new AIInterviewJobSpecificationSnapshotModel()
                : await _aiInterviewJobDisplayService.GetSpecificationSnapshotAsync(product.Id);

            model.Jobs.Add(new EmployerDashboardJobModel
            {
                ProductId = product.Id,
                JobTitle = product.Name,
                Published = product.Published,
                SalaryRange = AIInterviewJobDisplayService.SanitizeSalaryDisplay(salarySnapshot?.SalaryRange),
                CreatedOnUtc = product.CreatedOnUtc,
                ApplicationCount = await _applicationService.GetApplicationCountAsync(productId: product.Id)
            });
        }

        model.Jobs = ApplyInMemoryPaging(
            model.Jobs,
            page,
            pageSize,
            DefaultEmployerDashboardTablePageSize,
            out var normalizedPage,
            out var normalizedPageSize,
            out var totalCount,
            out var totalPages);
        model.Page = normalizedPage;
        model.PageSize = normalizedPageSize;
        model.TotalCount = totalCount;
        model.TotalPages = totalPages;

        return model;
    }

    protected virtual async Task<EmployerDashboardInvitesTabModel> BuildEmployerDashboardInvitesTabModelAsync(Customer customer, int page = 1, int pageSize = DefaultEmployerDashboardTablePageSize)
    {
        var model = new EmployerDashboardInvitesTabModel();
        if (_inviteService == null || _creditService == null)
            return model;

        var invites = (await _inviteService.GetSponsorInvitesAsync(customer.Id) ?? new List<SponsorInvite>())
            .OrderByDescending(invite => invite.CreatedOnUtc)
            .ToList();
        var wallet = await _creditService.GetOrCreateWalletAsync(customer.Id);

        model.CreditBalance = wallet.Balance;
        model.CreditBalanceDisplay = decimal.Truncate(wallet.Balance).ToString("0", CultureInfo.InvariantCulture);
        model.AvailableProducts = await BuildEmployerInviteProductSelectListAsync(customer);
        model.Invites = ApplyInMemoryPaging(
            invites,
            page,
            pageSize,
            DefaultEmployerDashboardTablePageSize,
            out var normalizedPage,
            out var normalizedPageSize,
            out var totalCount,
            out var totalPages);
        model.Page = normalizedPage;
        model.PageSize = normalizedPageSize;
        model.TotalCount = totalCount;
        model.TotalPages = totalPages;

        foreach (var invite in model.Invites)
            model.InviteStatuses[invite.Id] = await GetInviteStatusTextAsync(invite);

        return model;
    }

    protected virtual IList<T> ApplyInMemoryPaging<T>(IList<T> items, int page, int pageSize, int defaultPageSize, out int normalizedPage, out int normalizedPageSize, out int totalCount, out int totalPages)
    {
        items ??= new List<T>();
        totalCount = items.Count;
        normalizedPageSize = pageSize > 0 ? pageSize : Math.Max(1, defaultPageSize);
        totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);
        normalizedPage = page > 0 ? page : 1;

        if (totalPages > 0 && normalizedPage > totalPages)
            normalizedPage = totalPages;

        if (totalPages == 0)
            normalizedPage = 1;

        return items
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToList();
    }

    protected virtual async Task<IList<SelectListItem>> BuildEmployerInviteProductSelectListAsync(Customer customer)
    {
        var products = await _productService.SearchProductsAsync(pageSize: int.MaxValue, showHidden: true)
            ?? new PagedList<Product>(new List<Product>(), 0, 1, 1);

        var filteredProducts = products.AsEnumerable();
        if (customer?.VendorId > 0)
            filteredProducts = filteredProducts.Where(product => product.VendorId == customer.VendorId);

        var items = new List<SelectListItem>
        {
            new()
            {
                Value = string.Empty,
                Text = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Select")
            }
        };

        foreach (var product in filteredProducts.OrderBy(product => product.Name))
        {
            if (_jobRequirementService != null && !await _jobRequirementService.IsJobProductAsync(product))
                continue;

            if (_jobProductAccessService != null && !await _jobProductAccessService.CanAcceptJobApplicationsAsync(product))
                continue;

            items.Add(new SelectListItem
            {
                Value = product.Id.ToString(),
                Text = $"{product.Name} (ID: {product.Id})"
            });
        }

        return items;
    }

    protected virtual async Task<string> GetInviteStatusTextAsync(SponsorInvite invite)
    {
        if (invite == null)
            return string.Empty;

        var attempts = _interviewSessionService == null
            ? 0
            : await _interviewSessionService.GetSponsorInviteAttemptCountAsync(invite.Id);

        var statusKey = attempts >= invite.MaxAttempts && invite.MaxAttempts > 0
            ? "Plugins.Misc.AIInterview.Employer.Invite.Exhausted"
            : !invite.IsActive
                ? "Plugins.Misc.AIInterview.Employer.Invite.Inactive"
                : invite.ExpiryDateUtc.HasValue && invite.ExpiryDateUtc.Value <= DateTime.UtcNow
                    ? "Plugins.Misc.AIInterview.Employer.Invite.Expired"
                    : attempts > 0 || invite.IsAccepted
                        ? "Plugins.Misc.AIInterview.Employer.Invite.Accepted"
                        : "Plugins.Misc.AIInterview.Employer.Invite.Active";

        return await _localizationService.GetResourceAsync(statusKey);
    }

    public async Task<IActionResult> EmployerApplications(ApplicationListModel model, int pageIndex = 0, int pageSize = 10)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        model = await BuildEmployerApplicationsModelAsync(customer, model, pageIndex, pageSize);

        return View("~/Plugins/Misc.AIInterview/Views/EmployerApplications.cshtml", model);
    }

    public async Task<IActionResult> EmployerDownloadResume(int applicationId)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var application = await _applicationService.GetJobApplicationByIdAsync(applicationId);
        if (application == null)
            return NotFound(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Resume.NotFound"));

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _customerService.IsAdminAsync(customer) && customer.VendorId > 0)
        {
            var product = await _productService.GetProductByIdAsync(application.ProductId);
            if (product == null || product.VendorId != customer.VendorId)
                return Challenge();
        }

        if (application.ResumeDownloadId <= 0)
            return NotFound(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.NoResume"));

        var download = await _downloadService.GetDownloadByIdAsync(application.ResumeDownloadId);
        if (download?.DownloadBinary == null || download.DownloadBinary.Length == 0)
            return NotFound(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Resume.NotFound"));

        var extension = string.IsNullOrWhiteSpace(download.Extension) ? Path.GetExtension(download.Filename ?? string.Empty) : download.Extension;
        var fileName = Path.GetFileName(download.Filename ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = $"resume{(string.IsNullOrWhiteSpace(extension) ? string.Empty : extension)}";
        else if (!string.IsNullOrWhiteSpace(extension) && !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            fileName += extension;

        return File(download.DownloadBinary,
            string.IsNullOrWhiteSpace(download.ContentType) ? "application/octet-stream" : download.ContentType,
            fileName);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(UpdateStatusModel model)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var application = await _applicationService.GetJobApplicationByIdAsync(model.Id);
        if (application == null)
            return NotFound();

        var normalizedStatus = JobApplicationStatuses.Normalize(model.Status);
        if (!JobApplicationStatuses.IsValid(normalizedStatus))
        {
            _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.UpdateStatus.Invalid"));
            return RedirectToRoute(AIInterviewDefaults.EmployerDashboardRouteName, new { tab = AIInterviewDefaults.EmployerDashboardApplicationsTabKey });
        }

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _customerService.IsAdminAsync(customer) && customer.VendorId > 0)
        {
            var product = await _productService.GetProductByIdAsync(application.ProductId);
            if (product == null || product.VendorId != customer.VendorId)
                return Challenge();
        }

        application.Status = normalizedStatus;
        application.StatusComment = model.StatusComment;

        await _applicationService.UpdateJobApplicationAsync(application);

        // Send "Application Status Update" email
        await _applicationService.SendApplicationStatusUpdateNotificationAsync(application, (await _workContext.GetWorkingLanguageAsync()).Id);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.UpdateStatus.Success"));

        return RedirectToRoute(AIInterviewDefaults.EmployerDashboardRouteName, new { tab = AIInterviewDefaults.EmployerDashboardApplicationsTabKey });
    }

    public async Task<IActionResult> ExportCsv(ApplicationListModel model)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var isEmployer = !await _customerService.IsAdminAsync(customer) && customer.VendorId > 0;
        var (startDateUtc, endDateUtc) = await ConvertApplicationFilterDatesToUtcAsync(model.StartDate, model.EndDate);

        var applications = await _applicationService.GetApplicationsAsync(
            candidateNameOrEmail: model.CandidateNameOrEmail,
            status: model.Status,
            minScore: model.MinScore,
            maxScore: model.MaxScore,
            startDate: startDateUtc,
            endDate: endDateUtc,
            vendorId: isEmployer ? customer.VendorId : 0,
            sortByScore: false);

        var customerIds = applications.Select(a => a.CustomerId).Distinct().ToList();
        var customers = await _customerService.GetCustomersByIdsAsync(customerIds.ToArray());

        var sb = new StringBuilder();
        var idHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ID");
        var candidateHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Candidate");
        var emailHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Email");
        var statusHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.History.Status");
        var scoreHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.History.Score");
        var dateHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.History.Date");
        var jobTitleHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.JobTitle");
        var chargeModeHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ChargeMode");
        var attemptsHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Attempts");
        sb.AppendLine($"{idHeader},{candidateHeader},{emailHeader},{statusHeader},{scoreHeader},{dateHeader},{jobTitleHeader},{chargeModeHeader},{attemptsHeader}");

        var exportRows = new List<ApplicationModel>();

        foreach (var a in applications)
        {
            var appCustomer = customers.FirstOrDefault(c => c.Id == a.CustomerId);
            var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(a.CustomerId);
            var appSessions = sessions.Where(s => SessionMatchesApplication(s, a)).ToList();
            var session = appSessions.OrderByDescending(s => s.CompletedOnUtc).FirstOrDefault(s => s.CompletedOnUtc.HasValue);

            var candidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown");
            var email = appCustomer?.Email ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown");
            var status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{JobApplicationStatuses.Normalize(a.Status)}");
            exportRows.Add(new ApplicationModel
            {
                Id = a.Id,
                CandidateName = candidateName,
                CandidateEmail = email,
                Status = status,
                InterviewScore = session?.Score,
                CreatedOn = a.CreatedOnUtc,
                JobTitle = a.JobTitle ?? string.Empty,
                ChargeMode = await GetEmployerChargeModeLabelAsync(session != null && session.SponsorInviteId > 0),
                AttemptCount = appSessions.Count
            });
        }

        foreach (var row in ApplyEmployerInterviewFiltersAndSorting(exportRows, model))
        {
            var score = row.InterviewScore?.ToString() ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.None");
            var candidateNameCsv = $"\"{row.CandidateName.Replace("\"", "\"\"")}\"";
            var emailCsv = $"\"{row.CandidateEmail.Replace("\"", "\"\"")}\"";
            var statusCsv = $"\"{row.Status?.Replace("\"", "\"\"")}\"";
            var jobTitleCsv = $"\"{row.JobTitle.Replace("\"", "\"\"")}\"";
            var chargeModeCsv = $"\"{row.ChargeMode.Replace("\"", "\"\"")}\"";

            sb.AppendLine($"{row.Id},{candidateNameCsv},{emailCsv},{statusCsv},{score},{row.CreatedOn:yyyy-MM-dd HH:mm:ss},{jobTitleCsv},{chargeModeCsv},{row.AttemptCount}");
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "applications.csv");
    }

    public async Task<IActionResult> VendorScoreboard()
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var model = await BuildVendorScoreboardModelAsync(customer);

        return View("~/Plugins/Misc.AIInterview/Views/VendorScoreboard.cshtml", model);
    }

    public async Task<IActionResult> VendorJobCreation()
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var model = new VendorJobModel();
        await PrepareVendorJobModelAsync(model);
        return View("~/Plugins/Misc.AIInterview/Views/VendorJobCreation.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> VendorJobCreation(VendorJobModel model)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer.VendorId <= 0)
            return Challenge();

        return await SaveVendorJobAsync(model);
    }

    public async Task<IActionResult> VendorJobEdit(int productId)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return NotFound();

        if (_jobRequirementService != null && !await _jobRequirementService.IsJobProductAsync(product))
            return NotFound();

        if (!await CanManageVendorJobAsync(customer, product))
            return Challenge();

        var model = new VendorJobModel();
        await PopulateVendorJobModelFromProductAsync(model, product);
        await PrepareVendorJobModelAsync(model);
        return View("~/Plugins/Misc.AIInterview/Views/VendorJobCreation.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> VendorJobEdit(int productId, VendorJobModel model)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return NotFound();

        if (_jobRequirementService != null && !await _jobRequirementService.IsJobProductAsync(product))
            return NotFound();

        if (!await CanManageVendorJobAsync(customer, product))
            return Challenge();

        model.Id = productId;
        model.IsEditMode = true;

        return await SaveVendorJobAsync(model, product);
    }

    [HttpPost]
    public async Task<IActionResult> ToggleVendorJobPublish(int productId)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return NotFound();

        if (_jobRequirementService != null && !await _jobRequirementService.IsJobProductAsync(product))
            return NotFound();

        if (!await CanManageVendorJobAsync(customer, product))
            return Challenge();

        product.Published = !product.Published;
        product.UpdatedOnUtc = DateTime.UtcNow;
        await _productService.UpdateProductAsync(product);

        return RedirectToRoute(AIInterviewDefaults.EmployerDashboardRouteName, new { tab = AIInterviewDefaults.EmployerDashboardJobsTabKey });
    }

    protected virtual async Task<IActionResult> SaveVendorJobAsync(VendorJobModel model, Product existingProduct = null)
    {
        NormalizeVendorJobModel(model);
        var (productTemplate, salaryRangeOptionId) = await ValidateVendorJobModelAsync(model, existingProduct == null);

        if (!ModelState.IsValid)
        {
            if (existingProduct != null)
                model.PublicJobUrl = await BuildProductRedirectUrlAsync(existingProduct);

            await PrepareVendorJobModelAsync(model);
            return View("~/Plugins/Misc.AIInterview/Views/VendorJobCreation.cshtml", model);
        }

        var customer = await _workContext.GetCurrentCustomerAsync();
        var now = DateTime.UtcNow;
        var isEditMode = existingProduct != null;
        var product = existingProduct ?? new Product
        {
            ProductType = ProductType.SimpleProduct,
            ProductTemplateId = productTemplate.Id,
            VendorId = customer.VendorId,
            VisibleIndividually = true,
            DisableBuyButton = true,
            IsShipEnabled = false,
            ManageInventoryMethod = ManageInventoryMethod.DontManageStock,
            OrderMinimumQuantity = 1,
            OrderMaximumQuantity = 1,
            CreatedOnUtc = now
        };

        product.Name = model.Name;
        product.ShortDescription = model.ShortDescription;
        product.FullDescription = model.FullDescription;
        product.Sku = string.IsNullOrWhiteSpace(model.Sku)
            ? (string.IsNullOrWhiteSpace(product.Sku) ? $"AIJOB-{DateTime.UtcNow:yyyyMMddHHmmssfff}" : product.Sku)
            : model.Sku;
        product.Published = model.Published;
        product.AvailableEndDateTimeUtc = GetInclusiveApplyUntilUtc(model.ApplyUntilUtc);
        product.UpdatedOnUtc = now;

        if (isEditMode)
            await _productService.UpdateProductAsync(product);
        else
            await _productService.InsertProductAsync(product);

        await SaveJobSalaryAttributesAsync(product, model);
        await ReplaceVendorJobSpecificationAttributesAsync(product, model, salaryRangeOptionId);

        if (_jobInterviewExperienceService != null)
            await _jobInterviewExperienceService.EnsureInterviewDifficultyAttributeAsync(product);

        if (_jobRequirementService != null)
            await _jobRequirementService.SaveRequirementsAsync(product, model.ResumeRequired, model.InterviewRequired, 0m, 3);

        var seName = await _urlRecordService.ValidateSeNameAsync(product, string.Empty, product.Name, true);
        await _urlRecordService.SaveSlugAsync(product, seName, 0);

        var successKey = isEditMode
            ? "Plugins.Misc.AIInterview.VendorJobCreation.UpdateSuccess"
            : "Plugins.Misc.AIInterview.VendorJobCreation.Success";
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync(successKey));

        return RedirectToRoute(AIInterviewDefaults.EmployerDashboardRouteName, new { tab = AIInterviewDefaults.EmployerDashboardJobsTabKey });
    }

    protected virtual async Task<(ProductTemplate ProductTemplate, int SalaryRangeOptionId)> ValidateVendorJobModelAsync(VendorJobModel model, bool isCreate)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(model.Name), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Name.Required"));

        if (model.ApplyUntilUtc.HasValue && model.ApplyUntilUtc.Value.Date < DateTime.UtcNow.Date)
            ModelState.AddModelError(nameof(model.ApplyUntilUtc), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.ApplyUntilUtc.Past"));

        if (model.SalaryMinCtcPa.HasValue && model.SalaryMinCtcPa.Value < 0)
            ModelState.AddModelError(nameof(model.SalaryMinCtcPa), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.SalaryMinCtcPa.Invalid"));

        if (model.SalaryMaxCtcPa.HasValue && model.SalaryMaxCtcPa.Value < 0)
            ModelState.AddModelError(nameof(model.SalaryMaxCtcPa), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.SalaryMaxCtcPa.Invalid"));

        if (model.SalaryMinCtcPa.HasValue && model.SalaryMaxCtcPa.HasValue && model.SalaryMaxCtcPa.Value < model.SalaryMinCtcPa.Value)
            ModelState.AddModelError(nameof(model.SalaryMaxCtcPa), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.SalaryRange.Invalid"));

        if (_urlRecordService == null || _productTemplateService == null)
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Unavailable"));

        var productTemplate = _productTemplateService == null
            ? null
            : ((await _productTemplateService.GetAllProductTemplatesAsync()) ?? new List<ProductTemplate>()).FirstOrDefault(template =>
                string.Equals(template.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.OrdinalIgnoreCase));
        if (isCreate && productTemplate == null)
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Unavailable"));

        var experienceAttribute = await GetExperienceLevelSpecificationAttributeAsync();
        var workModeAttribute = await GetWorkArrangementSpecificationAttributeAsync();
        var employmentTypeAttribute = await GetEmploymentTypeSpecificationAttributeAsync();

        if (!await IsValidSpecificationOptionSelectionAsync(model.ExperienceLevelOptionId, experienceAttribute))
            ModelState.AddModelError(nameof(model.ExperienceLevelOptionId), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.ExperienceLevel.Invalid"));

        if (!await IsValidSpecificationOptionSelectionAsync(model.WorkModeOptionId, workModeAttribute))
            ModelState.AddModelError(nameof(model.WorkModeOptionId), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.WorkMode.Invalid"));

        if (!await IsValidSpecificationOptionSelectionAsync(model.EmploymentTypeOptionId, employmentTypeAttribute))
            ModelState.AddModelError(nameof(model.EmploymentTypeOptionId), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.EmploymentType.Invalid"));

        var jobLocationAttribute = await GetJobLocationSpecificationAttributeAsync();
        if (jobLocationAttribute == null)
            ModelState.AddModelError(nameof(model.JobLocationOptionId), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.JobLocation.Unsupported"));
        else if (!await IsValidSpecificationOptionSelectionAsync(model.JobLocationOptionId, jobLocationAttribute))
            ModelState.AddModelError(nameof(model.JobLocationOptionId), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.JobLocation.Invalid"));

        var salaryRangeOptionId = 0;
        var salaryDisplay = AIInterviewJobDisplayService.BuildSalaryDisplayText(model.SalaryMinCtcPa, model.SalaryMaxCtcPa);
        if (!string.IsNullOrWhiteSpace(salaryDisplay))
        {
            salaryRangeOptionId = await ResolveSalaryRangeSpecificationOptionIdAsync();
            if (salaryRangeOptionId <= 0)
                ModelState.AddModelError(nameof(model.SalaryRange), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.SalaryRange.Unsupported"));
        }

        return (productTemplate, salaryRangeOptionId);
    }

    protected virtual async Task PopulateVendorJobModelFromProductAsync(VendorJobModel model, Product product)
    {
        if (model == null || product == null)
            return;

        model.Id = product.Id;
        model.IsEditMode = true;
        model.Name = product.Name;
        model.ShortDescription = product.ShortDescription;
        model.FullDescription = product.FullDescription;
        model.Sku = product.Sku;
        model.Published = product.Published;
        model.ApplyUntilUtc = product.AvailableEndDateTimeUtc?.Date;
        model.PublicJobUrl = await BuildProductRedirectUrlAsync(product);

        if (_jobRequirementService != null)
        {
            var requirements = await _jobRequirementService.GetRequirementsAsync(product);
            model.ResumeRequired = requirements?.ResumeRequired ?? false;
            model.InterviewRequired = requirements?.InterviewRequired ?? false;
        }

        await LoadVendorJobSpecificationSelectionsAsync(model, product.Id);
        await LoadJobSalaryAttributesAsync(product, model);
    }

    protected virtual async Task LoadVendorJobSpecificationSelectionsAsync(VendorJobModel model, int productId)
    {
        var mappings = await GetProductSpecificationMappingsAsync(productId);

        foreach (var mapping in mappings)
        {
            var attributeName = mapping.Attribute?.Name ?? string.Empty;
            if (IsSpecificationAliasMatch(attributeName, GetExperienceLevelAttributeAliases()))
                model.ExperienceLevelOptionId = mapping.Option?.Id;
            else if (IsSpecificationAliasMatch(attributeName, GetWorkArrangementAttributeAliases()))
                model.WorkModeOptionId = mapping.Option?.Id;
            else if (IsSpecificationAliasMatch(attributeName, GetEmploymentTypeAttributeAliases()))
                model.EmploymentTypeOptionId = mapping.Option?.Id;
            else if (IsSpecificationAliasMatch(attributeName, GetJobLocationAttributeAliases()))
                model.JobLocationOptionId = mapping.Option?.Id;
        }
    }

    protected virtual async Task LoadJobSalaryAttributesAsync(Product product, VendorJobModel model)
    {
        if (product == null || model == null || _genericAttributeService == null)
            return;

        model.SalaryMinCtcPa = ParseNullableDecimal(await _genericAttributeService.GetAttributeAsync<string>(product, AIInterviewDefaults.JobSalaryMinCtcPaAttributeName));
        model.SalaryMaxCtcPa = ParseNullableDecimal(await _genericAttributeService.GetAttributeAsync<string>(product, AIInterviewDefaults.JobSalaryMaxCtcPaAttributeName));
        model.SalaryRange = AIInterviewJobDisplayService.BuildSalaryDisplayText(model.SalaryMinCtcPa, model.SalaryMaxCtcPa);

        if (!string.IsNullOrWhiteSpace(model.SalaryRange))
            return;

        var legacySalaryText = await LoadLegacySalaryRangeAsync(product.Id);
        model.SalaryRange = legacySalaryText;

        if (TryParseLegacySalaryRange(legacySalaryText, out var minValue, out var maxValue))
        {
            model.SalaryMinCtcPa = minValue;
            model.SalaryMaxCtcPa = maxValue;
        }
    }

    protected virtual async Task SaveJobSalaryAttributesAsync(Product product, VendorJobModel model)
    {
        if (product == null || model == null || _genericAttributeService == null)
            return;

        model.SalaryRange = AIInterviewJobDisplayService.BuildSalaryDisplayText(model.SalaryMinCtcPa, model.SalaryMaxCtcPa);
        var hasStructuredSalary = !string.IsNullOrWhiteSpace(model.SalaryRange);

        await _genericAttributeService.SaveAttributeAsync(product, AIInterviewDefaults.JobSalaryMinCtcPaAttributeName, model.SalaryMinCtcPa);
        await _genericAttributeService.SaveAttributeAsync(product, AIInterviewDefaults.JobSalaryMaxCtcPaAttributeName, model.SalaryMaxCtcPa);
        await _genericAttributeService.SaveAttributeAsync(product, AIInterviewDefaults.JobSalaryCurrencyCodeAttributeName, hasStructuredSalary ? "INR" : null);
        await _genericAttributeService.SaveAttributeAsync(product, AIInterviewDefaults.JobSalaryPeriodAttributeName, hasStructuredSalary ? "PA" : null);
    }

    protected virtual async Task ReplaceVendorJobSpecificationAttributesAsync(Product product, VendorJobModel model, int salaryRangeOptionId)
    {
        if (product == null || _specificationAttributeService == null)
            return;

        var mappings = await GetProductSpecificationMappingsAsync(product.Id);
        foreach (var mapping in mappings.Where(entry => IsCompactEmployerMetadataAttribute(entry.Attribute?.Name ?? string.Empty)))
            await _specificationAttributeService.DeleteProductSpecificationAttributeAsync(mapping.Mapping);

        await InsertProductSpecificationAttributeAsync(product.Id, model.ExperienceLevelOptionId ?? 0, displayOrder: 0);
        await InsertProductSpecificationAttributeAsync(product.Id, model.WorkModeOptionId ?? 0, displayOrder: 1);
        await InsertProductSpecificationAttributeAsync(product.Id, model.EmploymentTypeOptionId ?? 0, displayOrder: 2);
        await InsertProductSpecificationAttributeAsync(product.Id, model.JobLocationOptionId ?? 0, displayOrder: 3);

        if (!string.IsNullOrWhiteSpace(model.SalaryRange))
        {
            await InsertProductSpecificationAttributeAsync(product.Id, salaryRangeOptionId,
                SpecificationAttributeType.CustomText, model.SalaryRange, 4);
        }
    }

    protected virtual async Task<IList<(ProductSpecificationAttribute Mapping, SpecificationAttributeOption Option, SpecificationAttribute Attribute)>> GetProductSpecificationMappingsAsync(int productId)
    {
        if (_specificationAttributeService == null || productId <= 0)
        {
            return new List<(ProductSpecificationAttribute Mapping, SpecificationAttributeOption Option, SpecificationAttribute Attribute)>();
        }

        var mappings = await _specificationAttributeService.GetProductSpecificationAttributesAsync(productId)
            ?? new List<ProductSpecificationAttribute>();
        if (!mappings.Any())
        {
            return new List<(ProductSpecificationAttribute Mapping, SpecificationAttributeOption Option, SpecificationAttribute Attribute)>();
        }

        var optionIds = mappings.Select(mapping => mapping.SpecificationAttributeOptionId).Distinct().ToArray();
        var options = await _specificationAttributeService.GetSpecificationAttributeOptionsByIdsAsync(optionIds);
        var optionLookup = options.ToDictionary(option => option.Id);
        var attributes = await _specificationAttributeService.GetSpecificationAttributeByIdsAsync(options.Select(option => option.SpecificationAttributeId).Distinct().ToArray());
        var attributeLookup = attributes.ToDictionary(attribute => attribute.Id);

        return mappings
            .Select(mapping =>
            {
                optionLookup.TryGetValue(mapping.SpecificationAttributeOptionId, out var option);
                attributeLookup.TryGetValue(option?.SpecificationAttributeId ?? 0, out var attribute);
                return (Mapping: mapping, Option: option, Attribute: attribute);
            })
            .Where(entry => entry.Attribute != null)
            .Select(entry => (entry.Mapping, entry.Option, entry.Attribute))
            .ToList();
    }

    protected virtual async Task<string> LoadLegacySalaryRangeAsync(int productId)
    {
        foreach (var mapping in await GetProductSpecificationMappingsAsync(productId))
        {
            if (!IsSpecificationAliasMatch(mapping.Attribute?.Name ?? string.Empty, GetSalaryRangeAttributeAliases()))
                continue;

            var value = mapping.Mapping.AttributeType == SpecificationAttributeType.CustomText
                ? mapping.Mapping.CustomValue
                : mapping.Option?.Name;
            var sanitizedValue = AIInterviewJobDisplayService.SanitizeSalaryDisplay(value);
            if (!string.IsNullOrWhiteSpace(sanitizedValue))
                return sanitizedValue;
        }

        return string.Empty;
    }

    protected virtual bool TryParseLegacySalaryRange(string legacySalaryText, out decimal? minCtcPa, out decimal? maxCtcPa)
    {
        minCtcPa = null;
        maxCtcPa = null;

        if (string.IsNullOrWhiteSpace(legacySalaryText))
            return false;

        var normalized = legacySalaryText.Trim().ToLowerInvariant();
        var matches = Regex.Matches(normalized, @"\d+(?:[.,]\d+)?\s*(?:lpa|lac|lakh|lakhs|k|cr|crore)?", RegexOptions.IgnoreCase)
            .Select(match => ParseLegacySalaryToken(match.Value))
            .Where(value => value.HasValue)
            .Select(value => value.Value)
            .ToList();

        if (!matches.Any())
            return false;

        if (normalized.Contains("up to", StringComparison.Ordinal))
        {
            maxCtcPa = matches.Last();
            return true;
        }

        if ((normalized.Contains('+') || normalized.Contains("above", StringComparison.Ordinal) || normalized.Contains("from", StringComparison.Ordinal)) && matches.Count == 1)
        {
            minCtcPa = matches[0];
            return true;
        }

        if (matches.Count >= 2)
        {
            minCtcPa = matches[0];
            maxCtcPa = matches[1];
            if (maxCtcPa < minCtcPa)
                (minCtcPa, maxCtcPa) = (maxCtcPa, minCtcPa);
            return true;
        }

        minCtcPa = matches[0];
        return true;
    }

    protected virtual decimal? ParseLegacySalaryToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var normalized = token.Trim().ToLowerInvariant();
        var numberText = Regex.Match(normalized, @"\d+(?:[.,]\d+)?").Value.Replace(",", string.Empty, StringComparison.Ordinal);
        if (!decimal.TryParse(numberText, NumberStyles.Number, CultureInfo.InvariantCulture, out var numericValue))
            return null;

        if (normalized.Contains("lpa", StringComparison.Ordinal) || normalized.Contains("lakh", StringComparison.Ordinal) || normalized.Contains("lac", StringComparison.Ordinal))
            return numericValue * 100000m;

        if (normalized.Contains("cr", StringComparison.Ordinal) || normalized.Contains("crore", StringComparison.Ordinal))
            return numericValue * 10000000m;

        if (normalized.Contains('k'))
            return numericValue * 1000m;

        return numericValue >= 1000m ? numericValue : null;
    }

    protected static decimal? ParseNullableDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    protected static bool IsSpecificationAliasMatch(string attributeName, IEnumerable<string> aliases)
    {
        var normalizedAttributeName = AIInterviewJobDisplayService.NormalizeSpecificationAttributeName(attributeName);
        return aliases.Any(alias => string.Equals(normalizedAttributeName, AIInterviewJobDisplayService.NormalizeSpecificationAttributeName(alias), StringComparison.OrdinalIgnoreCase));
    }

    protected static bool IsCompactEmployerMetadataAttribute(string attributeName)
    {
        return IsSpecificationAliasMatch(attributeName, GetExperienceLevelAttributeAliases()) ||
            IsSpecificationAliasMatch(attributeName, GetWorkArrangementAttributeAliases()) ||
            IsSpecificationAliasMatch(attributeName, GetEmploymentTypeAttributeAliases()) ||
            IsSpecificationAliasMatch(attributeName, GetJobLocationAttributeAliases()) ||
            IsSpecificationAliasMatch(attributeName, GetSalaryRangeAttributeAliases());
    }

    protected virtual async Task<bool> CanManageVendorJobAsync(Customer customer, Product product)
    {
        if (customer == null || product == null)
            return false;

        if (await _customerService.IsAdminAsync(customer))
            return true;

        return customer.VendorId > 0 && product.VendorId == customer.VendorId;
    }

    protected virtual void NormalizeVendorJobModel(VendorJobModel model)
    {
        if (model == null)
            return;

        model.Name = model.Name?.Trim();
        model.Sku = model.Sku?.Trim();
        model.ShortDescription = model.ShortDescription?.Trim();
        model.FullDescription = model.FullDescription?.Trim();
        model.MinimumScore = 0m;
        model.QuestionCount = 3;
    }

    protected virtual async Task<string> GetEmployerChargeModeLabelAsync(bool isCompanySponsored)
    {
        var resourceKey = isCompanySponsored
            ? "Plugins.Misc.AIInterview.Employer.Applications.ChargeMode.CompanySponsored"
            : "Plugins.Misc.AIInterview.Employer.Applications.ChargeMode.CandidatePaid";

        return await _localizationService.GetResourceAsync(resourceKey);
    }

    protected virtual IList<ApplicationModel> ApplyEmployerInterviewFiltersAndSorting(IEnumerable<ApplicationModel> applications, ApplicationListModel model)
    {
        var filteredApplications = applications;

        if (model.OnlyWithInterviewScore)
            filteredApplications = filteredApplications.Where(application => application.InterviewScore.HasValue);

        return (model.InterviewSort ?? "TopScorersFirst") switch
        {
            "LowestScorersFirst" => filteredApplications
                .OrderBy(application => application.InterviewScore.HasValue ? 0 : 1)
                .ThenBy(application => application.InterviewScore ?? decimal.MaxValue)
                .ThenByDescending(application => application.CreatedOn)
                .ToList(),
            "LatestApplied" => filteredApplications
                .OrderByDescending(application => application.CreatedOn)
                .ThenByDescending(application => application.InterviewScore ?? decimal.MinValue)
                .ToList(),
            _ => filteredApplications
                .OrderBy(application => application.InterviewScore.HasValue ? 0 : 1)
                .ThenByDescending(application => application.InterviewScore ?? decimal.MinValue)
                .ThenByDescending(application => application.CreatedOn)
                .ToList()
        };
    }

    protected virtual DateTime? GetInclusiveApplyUntilUtc(DateTime? applyUntilUtc)
    {
        if (!applyUntilUtc.HasValue)
            return null;

        return applyUntilUtc.Value.Date.AddDays(1).AddTicks(-1);
    }

    protected virtual async Task<bool> IsValidSpecificationOptionSelectionAsync(int? optionId, SpecificationAttribute specificationAttribute)
    {
        if (!optionId.HasValue || optionId.Value <= 0)
            return true;

        if (_specificationAttributeService == null || specificationAttribute == null)
            return false;

        var option = await _specificationAttributeService.GetSpecificationAttributeOptionByIdAsync(optionId.Value);
        return option != null && option.SpecificationAttributeId == specificationAttribute.Id;
    }
}
