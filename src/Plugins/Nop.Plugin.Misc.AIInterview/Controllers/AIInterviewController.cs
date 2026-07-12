using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Http;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
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
using Nop.Core.Http;
using Nop.Plugin.Misc.AIInterview.Infrastructure;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

public class AIInterviewController : BasePluginController
{
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
        IDateTimeHelper dateTimeHelper = null)
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
        IDateTimeHelper dateTimeHelper = null)
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
            dateTimeHelper)
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

    protected virtual string BuildAuthenticatedRecordingUrl(int sessionId)
    {
        return sessionId > 0 ? Url?.Action("Recording", "AIInterview", new { sessionId }) : null;
    }

    protected virtual async Task<string> BuildRecordingShareUrlAsync(InterviewSession session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.RecordingUrl))
            return null;

        var token = await _interviewSessionService.EnsureRecordingShareTokenAsync(session);
        return string.IsNullOrWhiteSpace(token)
            ? null
            : BuildRouteUrl(AIInterviewDefaults.RecordingShareRouteName, new { token });
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
            RecordingUrl = !string.IsNullOrWhiteSpace(session.RecordingUrl) ? BuildAuthenticatedRecordingUrl(session.Id) : null,
            RecordingShareUrl = await BuildRecordingShareUrlAsync(session),
            CreatedOnUtc = session.CreatedOnUtc,
            ReportDateUtc = session.CompletedOnUtc ?? session.StartedOnUtc ?? session.CreatedOnUtc,
            CompletedOnUtc = session.CompletedOnUtc,
            Turns = MapTurns(turns)
        };
    }

    protected virtual async Task<IActionResult> ProxyRecordingAsync(string recordingUrl)
    {
        var playbackUrl = BuildRecordingPlaybackUrl(recordingUrl);
        if (string.IsNullOrWhiteSpace(playbackUrl))
            return NotFound();

        using var client = _httpClientFactory?.CreateClient(nameof(AIInterviewController)) ?? new HttpClient();
        var response = await client.GetAsync(playbackUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return NotFound();
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "video/webm";
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

    public async Task<IActionResult> MyApplications(string sortOrder, string status = null, decimal? minScore = null, decimal? maxScore = null)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var applications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id);
        var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id);

        var applicationModels = await Task.WhenAll(applications.Select(async a =>
        {
            var appSessions = sessions.Where(s => SessionMatchesApplication(s, a)).ToList();
            var latestSession = appSessions
                .Where(s => s.CompletedOnUtc.HasValue)
                .OrderByDescending(s => s.CompletedOnUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefault();
            var normalizedStatus = JobApplicationStatuses.Normalize(a.Status);
            var questionScores = ParseQuestionScores(latestSession?.QuestionScores);
            var reportSections = SplitReportSections(latestSession?.ReportData);
            var turns = latestSession != null && _interviewTurnService != null
                ? ((await _interviewTurnService.GetTurnsBySessionIdAsync(latestSession.Id)) ?? new List<InterviewTurn>()).ToList()
                : new List<InterviewTurn>();

            return new ApplicationModel
            {
                Id = a.Id,
                InterviewSessionId = latestSession?.Id ?? 0,
                JobTitle = a.JobTitle,
                InterviewScore = latestSession?.Score,
                InterviewReportUrl = latestSession != null ? BuildAuthenticatedReportUrl(latestSession.Id) : null,
                InterviewReportPanelUrl = latestSession != null ? BuildReportPanelUrl(latestSession.Id) : null,
                RecordingUrl = latestSession != null && !string.IsNullOrWhiteSpace(latestSession.RecordingUrl) ? BuildAuthenticatedRecordingUrl(latestSession.Id) : null,
                RecordingShareUrl = latestSession != null ? await BuildRecordingShareUrlAsync(latestSession) : null,
                Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{normalizedStatus}"),
                RawStatus = normalizedStatus,
                CreatedOn = a.CreatedOnUtc,
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
            "OldestApplied" => query.OrderBy(a => a.CreatedOn),
            "HighestScore" => query.OrderByDescending(a => a.InterviewScore ?? 0),
            "LowestScore" => query.OrderBy(a => a.InterviewScore ?? 0),
            "LatestInterviewDate" => query.OrderByDescending(a => a.LatestScoreDate ?? DateTime.MinValue),
            _ => query.OrderByDescending(a => a.CreatedOn)
        };

        var model = new ApplicationListModel
        {
            Applications = query.ToList(),
            SortOrder = normalizedSortOrder,
            Status = status,
            MinScore = minScore,
            MaxScore = maxScore,
            TotalCount = query.Count()
        };

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
        return PartialView("~/Plugins/Misc.AIInterview/Views/Shared/_InterviewReportContent.cshtml", model);
    }

    [HttpGet]
    public async Task<IActionResult> Recording(int sessionId)
    {
        if (!_aiInterviewSettings.Enabled)
            return RedirectToRoute("Homepage");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        if (!await _interviewSessionService.CanAccessReportAsync(customer.Id, sessionId))
            return Challenge();

        var session = await _interviewSessionService.GetInterviewSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrWhiteSpace(session.RecordingUrl))
            return NotFound();

        return await ProxyRecordingAsync(session.RecordingUrl);
    }

    [HttpGet]
    public async Task<IActionResult> RecordingShare(string token)
    {
        if (!_aiInterviewSettings.Enabled)
            return NotFound();

        var session = await _interviewSessionService.GetSessionByRecordingShareTokenAsync(token);
        if (session == null ||
            !session.RecordingShareEnabled ||
            string.IsNullOrWhiteSpace(session.RecordingShareToken) ||
            !string.Equals(session.RecordingShareToken, token, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(session.RecordingUrl))
        {
            return NotFound();
        }

        return await ProxyRecordingAsync(session.RecordingUrl);
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
        if (string.IsNullOrWhiteSpace(recordingUrl) ||
            string.IsNullOrWhiteSpace(_aiInterviewSettings.AzureBlobStorageContainerUrl) ||
            string.IsNullOrWhiteSpace(_aiInterviewSettings.AzureBlobStorageSasToken))
            return null;

        if (!Uri.TryCreate(_aiInterviewSettings.AzureBlobStorageContainerUrl.Trim(), UriKind.Absolute, out var containerUri) ||
            !Uri.TryCreate(recordingUrl.Trim(), UriKind.Absolute, out var recordingUri))
            return null;

        if (!string.IsNullOrWhiteSpace(recordingUri.Query) || !string.IsNullOrWhiteSpace(recordingUri.Fragment))
            return null;

        if (!string.Equals(containerUri.Scheme, recordingUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(containerUri.Host, recordingUri.Host, StringComparison.OrdinalIgnoreCase) ||
            containerUri.Port != recordingUri.Port)
            return null;

        var containerPath = containerUri.AbsolutePath.TrimEnd('/');
        var recordingPath = recordingUri.AbsolutePath.TrimEnd('/');
        var isMatchingContainer = string.Equals(recordingPath, containerPath, StringComparison.OrdinalIgnoreCase) ||
            recordingPath.StartsWith(containerPath + "/", StringComparison.OrdinalIgnoreCase);
        if (!isMatchingContainer)
            return null;

        var sasToken = _aiInterviewSettings.AzureBlobStorageSasToken.Trim();
        if (!sasToken.StartsWith("?", StringComparison.Ordinal))
            sasToken = sasToken.StartsWith("&", StringComparison.Ordinal) ? "?" + sasToken[1..] : "?" + sasToken;

        return $"{recordingUri.GetLeftPart(UriPartial.Path).TrimEnd('/')}{sasToken}";
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
            return Json(new { success = false, error = result.Message, redirect = result.RedirectUrl, requiresLogin = result.RequiresLogin });

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
            returnUrl = Url.RouteUrl(AIInterviewDefaults.ApplyRouteName, new { productId = model?.ProductId, jobTitle = model?.JobTitle });

        return Url.RouteUrl(NopRouteNames.General.LOGIN, new { returnUrl });
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
        if (model == null || _specificationAttributeService == null)
            return;

        var selectText = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Select");

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

    public async Task<IActionResult> EmployerApplications(ApplicationListModel model, int pageIndex = 0, int pageSize = 10)
    {
        if (!await IsAuthorizedForEmployerActionsAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var isEmployer = !await _customerService.IsAdminAsync(customer) && customer.VendorId > 0;
        var (startDateUtc, endDateUtc) = await ConvertApplicationFilterDatesToUtcAsync(model.StartDate, model.EndDate);

        pageSize = model.PageSize > 0 ? model.PageSize : pageSize;

        var applications = await _applicationService.GetApplicationsAsync(
            candidateNameOrEmail: model.CandidateNameOrEmail,
            status: model.Status,
            minScore: model.MinScore,
            maxScore: model.MaxScore,
            startDate: startDateUtc,
            endDate: endDateUtc,
            vendorId: isEmployer ? customer.VendorId : 0,
            pageIndex: pageIndex,
            pageSize: pageSize,
            sortByScore: model.SortByScore);

        var customerIds = applications.Select(a => a.CustomerId).Distinct().ToList();
        var customers = await _customerService.GetCustomersByIdsAsync(customerIds.ToArray());

        // Optimize session fetching by getting all sessions for these applications
        // Note: IInterviewSessionService doesn't have GetByApplicationIds, so we'll just fetch by customer if needed,
        // or just leave as is if we want to avoid modifying service for now.
        // Actually, for a small page size, fetching per item is acceptable but let's try to be better.

        model.Applications = await Task.WhenAll(applications.Select(async a =>
        {
            var appCustomer = customers.FirstOrDefault(c => c.Id == a.CustomerId);
            var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(a.CustomerId);
            var appSessions = sessions.Where(s => SessionMatchesApplication(s, a)).ToList();
            var session = appSessions
                .Where(s => s.CompletedOnUtc.HasValue)
                .OrderByDescending(s => s.CompletedOnUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefault();
            var normalizedStatus = JobApplicationStatuses.Normalize(a.Status);
            var questionScores = ParseQuestionScores(session?.QuestionScores);
            var reportSections = SplitReportSections(session?.ReportData);

            return new ApplicationModel
            {
                Id = a.Id,
                JobTitle = a.JobTitle,
                CandidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown"),
                CandidateEmail = appCustomer?.Email ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown"),
                CandidatePhone = string.IsNullOrWhiteSpace(appCustomer?.Phone) ? "+1 555 201 001" : appCustomer.Phone,
                Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{normalizedStatus}"),
                RawStatus = normalizedStatus,
                StatusComment = a.StatusComment,
                ResumeDownloadId = a.ResumeDownloadId,
                HasResume = a.ResumeDownloadId > 0,
                ResumeDownloadUrl = a.ResumeDownloadId > 0 ? Url.RouteUrl(AIInterviewDefaults.EmployerDownloadResumeRouteName, new { applicationId = a.Id }) : null,
                InterviewScore = session?.Score,
                InterviewReportUrl = session != null ? Url.Action("Report", "AIInterview", new { sessionId = session.Id }) : null,
                InterviewReportPanelUrl = session != null ? BuildReportPanelUrl(session.Id) : null,
                CreatedOn = a.CreatedOnUtc,
                AttemptCount = appSessions.Count,
                CompletedOn = session?.CompletedOnUtc,
                ChargeMode = await GetEmployerChargeModeLabelAsync(session != null && session.SponsorInviteId > 0),
                PromptSource = string.IsNullOrWhiteSpace(_aiInterviewSettings.Provider) && string.IsNullOrWhiteSpace(_aiInterviewSettings.Model)
                    ? "Resume-backed template"
                    : $"Provider: {_aiInterviewSettings.Provider}, Model: {_aiInterviewSettings.Model}",
                CoverMessage = !string.IsNullOrWhiteSpace(a.StatusComment)
                    ? a.StatusComment
                    : $"Applied for {a.JobTitle} with a resume-backed profile and interview history.",
                QuestionScores = session?.QuestionScores,
                QuestionScoreValues = questionScores,
                ReportSummary = reportSections.Summary,
                FeedbackSummary = reportSections.Feedback
            };
        }));

        model.Applications = ApplyEmployerInterviewFiltersAndSorting(model.Applications, model);

        if (!string.IsNullOrWhiteSpace(model.JobTitleOrKeyword))
            model.Applications = model.Applications
                .Where(application => (application.JobTitle ?? string.Empty).Contains(model.JobTitleOrKeyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

        model.TotalCount = model.Applications.Count;

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
            return RedirectToAction("EmployerApplications");
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

        return RedirectToAction("EmployerApplications");
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
                    RecordingUrl = latestCompletedSession != null && !string.IsNullOrWhiteSpace(latestCompletedSession.RecordingUrl)
                        ? Url?.Action("Recording", "AIInterview", new { sessionId = latestCompletedSession.Id })
                        : null,
                    CreatedOn = application.CreatedOnUtc,
                    CompletedOn = latestCompletedSession?.CompletedOnUtc
                };
            }));

        var model = new VendorScoreboardModel
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
            RecentApplications = recentApplications.ToList()
        };

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

        NormalizeVendorJobModel(model);

        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(model.Name), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Name.Required"));

        if (model.ApplyUntilUtc.HasValue && model.ApplyUntilUtc.Value.Date < DateTime.UtcNow.Date)
            ModelState.AddModelError(nameof(model.ApplyUntilUtc), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.ApplyUntilUtc.Past"));

        if (_productTemplateService == null || _urlRecordService == null)
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Unavailable"));

        var productTemplate = _productTemplateService == null
            ? null
            : (await _productTemplateService.GetAllProductTemplatesAsync()).FirstOrDefault(template =>
                string.Equals(template.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.OrdinalIgnoreCase));
        if (productTemplate == null)
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
        if (!string.IsNullOrWhiteSpace(model.SalaryRange))
        {
            salaryRangeOptionId = await ResolveSalaryRangeSpecificationOptionIdAsync();
            if (salaryRangeOptionId <= 0)
                ModelState.AddModelError(nameof(model.SalaryRange), await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.SalaryRange.Unsupported"));
        }

        if (!ModelState.IsValid)
        {
            await PrepareVendorJobModelAsync(model);
            return View("~/Plugins/Misc.AIInterview/Views/VendorJobCreation.cshtml", model);
        }

        var now = DateTime.UtcNow;
        var product = new Product
        {
            ProductType = ProductType.SimpleProduct,
            ProductTemplateId = productTemplate.Id,
            VendorId = customer.VendorId,
            Name = model.Name.Trim(),
            ShortDescription = model.ShortDescription,
            FullDescription = model.FullDescription,
            Sku = string.IsNullOrWhiteSpace(model.Sku) ? $"AIJOB-{DateTime.UtcNow:yyyyMMddHHmmssfff}" : model.Sku,
            VisibleIndividually = true,
            Published = model.Published,
            DisableBuyButton = true,
            IsShipEnabled = false,
            ManageInventoryMethod = ManageInventoryMethod.DontManageStock,
            OrderMinimumQuantity = 1,
            OrderMaximumQuantity = 1,
            AvailableEndDateTimeUtc = GetInclusiveApplyUntilUtc(model.ApplyUntilUtc),
            CreatedOnUtc = now,
            UpdatedOnUtc = now
        };

        await _productService.InsertProductAsync(product);

        await InsertProductSpecificationAttributeAsync(product.Id, model.ExperienceLevelOptionId ?? 0, displayOrder: 0);
        await InsertProductSpecificationAttributeAsync(product.Id, model.WorkModeOptionId ?? 0, displayOrder: 1);
        await InsertProductSpecificationAttributeAsync(product.Id, model.EmploymentTypeOptionId ?? 0, displayOrder: 2);

        await InsertProductSpecificationAttributeAsync(product.Id, model.JobLocationOptionId ?? 0, displayOrder: 3);

        if (!string.IsNullOrWhiteSpace(model.SalaryRange))
        {
            await InsertProductSpecificationAttributeAsync(product.Id, salaryRangeOptionId,
                SpecificationAttributeType.CustomText, model.SalaryRange, 4);
        }

        if (_jobInterviewExperienceService != null)
            await _jobInterviewExperienceService.EnsureInterviewDifficultyAttributeAsync(product);
        if (_jobRequirementService != null)
            await _jobRequirementService.SaveRequirementsAsync(product, model.ResumeRequired, model.InterviewRequired, 0m, 3);
        var seName = await _urlRecordService.ValidateSeNameAsync(product, string.Empty, product.Name, true);
        await _urlRecordService.SaveSlugAsync(product, seName, 0);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.VendorJobCreation.Success"));
        return RedirectToRoute(AIInterviewDefaults.VendorScoreboardRouteName);
    }

    protected virtual void NormalizeVendorJobModel(VendorJobModel model)
    {
        if (model == null)
            return;

        model.Name = model.Name?.Trim();
        model.Sku = model.Sku?.Trim();
        model.ShortDescription = model.ShortDescription?.Trim();
        model.FullDescription = model.FullDescription?.Trim();
        model.SalaryRange = model.SalaryRange?.Trim();
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
