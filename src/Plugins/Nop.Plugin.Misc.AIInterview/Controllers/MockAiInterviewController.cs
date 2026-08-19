using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Text.Json;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Infrastructure;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Core.Events;
using Nop.Plugin.Misc.AIInterview.Events;
using Nop.Services.Vendors;
using Nop.Services.Seo;
using NopLogger = Nop.Services.Logging.ILogger;
using Nop.Web.Framework.Controllers;
using Microsoft.Extensions.Logging;
using Nop.Core.Domain.Media;
using NopLogLevel = Nop.Core.Domain.Logging.LogLevel;
using Nop.Services.Logging;
using Nop.Services.Media;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using System.Globalization;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

public class MockAiInterviewController : BasePluginController
{
    private const string InsufficientCreditsMessage = "You do not have enough credits to start this interview. Please upgrade your plan or buy additional credits.";
    private const string CreditRefundNotice = "Credit charge reversed because interview start did not complete.";
    private const string FirstQuestionStartFailureReasonCode = "FIRST_QUESTION_START_FAILED";
    public const long MaxRecordingUploadBytes = 250L * 1024L * 1024L;

    private const string VoiceUnavailableMessage = "Voice mode is unavailable. Please type your answer below.";
    private const string FeedbackIssueSupport = "Talk to support team";
    private const string FeedbackIssueOther = "Other issue";
    private const int MaxFeedbackCommentLength = 4000;
    private const long MaxFeedbackAttachmentBytes = 5 * 1024 * 1024;
    private static readonly string[] FeedbackIssues =
    [
        "AI is not speaking",
        "Typing is not working",
        "Loading issues",
        "Taking too much time for result generation",
        FeedbackIssueSupport,
        FeedbackIssueOther
    ];

    private static readonly HashSet<string> FeedbackHelpfulnessValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "helpful",
        "not_helpful"
    };

    private static readonly HashSet<string> FeedbackAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".pdf",
        ".doc",
        ".docx",
        ".txt"
    };

    private static readonly string[] PracticeDifficultyKeywords =
    [
        "practice difficulty",
        "difficulty",
        "interview difficulty"
    ];

    private static readonly string[] PracticeSkillKeywords =
    [
        "practice skill",
        "skill",
        "skills",
        "interview skill"
    ];

    private sealed record SelectedProductAttributeValueSnapshot(int AttributeId, string AttributeName, string TextPrompt, int ValueId, string Value);
    private sealed record SelectedProductAttributesSnapshot(IList<SelectedProductAttributeValueSnapshot> Attributes);
    private sealed record MockPracticeSelectionResult(string SelectedProductAttributesJson, string Difficulty, bool HasPracticeSkill, IList<string> Errors);

    private readonly IInterviewSessionService _interviewSessionService;
    private readonly ILocalizationService _localizationService;
    private readonly IWorkContext _workContext;
    private readonly ISponsorInviteService _inviteService;
    private readonly ICreditService _creditService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly IVendorService _vendorService;
    private readonly IApplicationService _applicationService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IJobInterviewExperienceService _jobInterviewExperienceService;
    private readonly IUrlRecordService _urlRecordService;
    private readonly IInterviewTurnService _turnService;
    private readonly IInterviewRuntimeService _interviewRuntimeService;
    private readonly IJobRequirementService _jobRequirementService;
    private readonly NopLogger _nopLogger;
    private readonly ILogger<MockAiInterviewController> _logger;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly IResumeFileService _resumeFileService;
    private readonly IResumeProfileService _resumeProfileService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly IProductAttributeParser _productAttributeParser;
    private readonly IProductAttributeService _productAttributeService;
    private readonly IJobProductAccessService _jobProductAccessService;
    private readonly ICustomerActivityService _customerActivityService;
    private readonly IDownloadService _downloadService;
    private readonly AIInterviewSettings _aiInterviewSettings;
    private readonly IInterviewStartCreditService _interviewStartCreditService;
    private readonly INotificationService _notificationService;

    public MockAiInterviewController(IInterviewSessionService interviewSessionService,
        ILocalizationService localizationService,
        IWorkContext workContext,
        ISponsorInviteService inviteService,
        ICreditService creditService,
        ICustomerService customerService,
        IProductService productService,
        IVendorService vendorService,
        IApplicationService applicationService,
        IEventPublisher eventPublisher = null,
        IJobInterviewExperienceService jobInterviewExperienceService = null,
        IUrlRecordService urlRecordService = null,
        IInterviewTurnService turnService = null,
        IInterviewRuntimeService interviewRuntimeService = null,
        IJobRequirementService jobRequirementService = null,
        NopLogger nopLogger = null,
        ILogger<MockAiInterviewController> logger = null,
        INopUrlHelper nopUrlHelper = null,
        IResumeFileService resumeFileService = null,
        IResumeProfileService resumeProfileService = null,
        IProductTemplateService productTemplateService = null,
        IProductAttributeParser productAttributeParser = null,
        IProductAttributeService productAttributeService = null,
        IJobProductAccessService jobProductAccessService = null,
        ICustomerActivityService customerActivityService = null,
        IDownloadService downloadService = null,
        AIInterviewSettings aiInterviewSettings = null,
        IInterviewStartCreditService interviewStartCreditService = null,
        INotificationService notificationService = null)
    {
        _interviewSessionService = interviewSessionService;
        _localizationService = localizationService;
        _workContext = workContext;
        _inviteService = inviteService;
        _creditService = creditService;
        _customerService = customerService;
        _productService = productService;
        _vendorService = vendorService;
        _applicationService = applicationService;
        _eventPublisher = eventPublisher;
        _jobInterviewExperienceService = jobInterviewExperienceService;
        _urlRecordService = urlRecordService;
        _turnService = turnService;
        _interviewRuntimeService = interviewRuntimeService;
        _jobRequirementService = jobRequirementService;
        _nopLogger = nopLogger;
        _logger = logger;
        _nopUrlHelper = nopUrlHelper;
        _resumeFileService = resumeFileService;
        _resumeProfileService = resumeProfileService;
        _productTemplateService = productTemplateService;
        _productAttributeParser = productAttributeParser;
        _productAttributeService = productAttributeService;
        _jobProductAccessService = jobProductAccessService;
        _customerActivityService = customerActivityService;
        _downloadService = downloadService;
        _aiInterviewSettings = aiInterviewSettings ?? new AIInterviewSettings();
        _interviewStartCreditService = interviewStartCreditService;
        _notificationService = notificationService;
    }

    protected virtual async Task<Customer> ResolveLogCustomerAsync(InterviewSession session = null, Customer customer = null)
    {
        if (customer != null)
            return customer;

        if (session?.CustomerId > 0)
        {
            var sessionCustomer = await _customerService.GetCustomerByIdAsync(session.CustomerId);
            if (sessionCustomer != null)
                return sessionCustomer;
        }

        return _workContext == null ? null : await _workContext.GetCurrentCustomerAsync();
    }

    protected virtual async Task LogRuntimeIssueAsync(string shortMessage, string fullMessage = "", Customer customer = null)
    {
        if (_nopLogger == null)
            return;

        await _nopLogger.InsertLogAsync(NopLogLevel.Warning, shortMessage, fullMessage, customer ?? (_workContext == null ? null : await _workContext.GetCurrentCustomerAsync()));
    }

    protected virtual async Task LogRuntimeActivityAsync(InterviewSession session, string systemKeyword, string comment, Customer customer = null)
    {
        if (_customerActivityService == null || session == null || string.IsNullOrWhiteSpace(systemKeyword))
            return;

        var activityCustomer = await ResolveLogCustomerAsync(session, customer);
        if (activityCustomer == null)
            return;

        try
        {
            await _customerActivityService.InsertActivityAsync(activityCustomer, systemKeyword, comment, session);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AI Interview activity logging failed for keyword {SystemKeyword}, session {SessionId}.", systemKeyword, session.Id);
        }
    }

    protected static string BuildRuntimeActivityComment(InterviewSession session, string message, string acknowledgedTimestamp = null, string screenSize = null, string viewportSize = null)
    {
        var details = new List<string>
        {
            $"SessionId={session?.Id ?? 0}",
            $"CustomerId={session?.CustomerId ?? 0}",
            $"ProductId={session?.ProductId ?? 0}"
        };

        if (!string.IsNullOrWhiteSpace(message))
            details.Add($"Message={message.Trim()}");
        if (!string.IsNullOrWhiteSpace(acknowledgedTimestamp))
            details.Add($"AcknowledgedTimestamp={acknowledgedTimestamp.Trim()}");
        if (!string.IsNullOrWhiteSpace(screenSize))
            details.Add($"ScreenSize={screenSize.Trim()}");
        if (!string.IsNullOrWhiteSpace(viewportSize))
            details.Add($"ViewportSize={viewportSize.Trim()}");

        return string.Join("; ", details);
    }

    protected static string NormalizeRuntimeClientRequestName(string requestName)
    {
        var normalized = (requestName ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "submit-answer" => "submit-answer",
            "begin" => "begin",
            "prepare" => "prepare",
            "prepare-response" => "prepare-response",
            "begin-response" => "begin-response",
            "first-question-rendered" => "first-question-rendered",
            "speech-token-ready" => "speech-token-ready",
            "tts-started" => "tts-started",
            "tts-completed" => "tts-completed",
            "recording-started" => "recording-started",
            "speech-token" => "speech-token",
            "speech-usage" => "speech-usage",
            "upload-recording" => "upload-recording",
            "refresh-token" => "refresh-token",
            "feedback" => "feedback",
            "acknowledge-guidelines" => "acknowledge-guidelines",
            "stop" => "stop",
            "runtime-client-event" => "runtime-client-event",
            _ => "unknown"
        };
    }

    protected static string NormalizeRuntimeClientStageName(string stageName)
    {
        var normalized = (stageName ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "prepare-response" => "prepare-response",
            "begin-response" => "begin-response",
            "first-question-rendered" => "first-question-rendered",
            "speech-token-ready" => "speech-token-ready",
            "tts-started" => "tts-started",
            "tts-completed" => "tts-completed",
            "recording-started" => "recording-started",
            _ => "unknown"
        };
    }

    protected static string NormalizeRuntimeClientFailureKind(string failureKind)
    {
        var normalized = (failureKind ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "http-status" => "http-status",
            "invalid-json" => "invalid-json",
            "non-json-response" => "non-json-response",
            "fetch-exception" => "fetch-exception",
            "network-error" => "network-error",
            "completion-upload-timeout-mismatch" => "completion-upload-timeout-mismatch",
            _ => "unknown"
        };
    }

    protected static string SanitizeRuntimeClientMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        var sanitized = new string(message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) || ".,:;!?()_-".Contains(character) ? character : ' ')
            .ToArray());

        sanitized = string.Join(" ", sanitized.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Length <= 180 ? sanitized : sanitized[..180];
    }

    protected static string BuildRuntimeClientFailureMessage(string failureKind, int? statusCode, string requestName = null)
    {
        if (NormalizeRuntimeClientRequestName(requestName) == "upload-recording" && statusCode == 413)
            return "Recording upload exceeded the configured request limit or an upstream host/proxy size limit.";

        return NormalizeRuntimeClientFailureKind(failureKind) switch
        {
            "http-status" => statusCode.HasValue
                ? $"Runtime request returned HTTP {Math.Clamp(statusCode.Value, 0, 999)}."
                : "Runtime request returned a failed HTTP status.",
            "invalid-json" => "Runtime request returned invalid JSON.",
            "non-json-response" => "Runtime request returned a non-JSON response.",
            "fetch-exception" or "network-error" => "Unable to reach the interview service.",
            "completion-upload-timeout-mismatch" => "Completion finalization timeout is lower than recording upload timeout + 5000ms. Check RecordingUploadTimeoutMs and FinalizationWaitTimeoutMs settings.",
            _ => "Runtime request failed."
        };
    }

    protected static string BuildRuntimeClientFailureActivityComment(InterviewSession session, string requestName, int? statusCode, string message, string failureKind, long? elapsedMilliseconds)
    {
        var details = new List<string>
        {
            $"SessionId={session?.Id ?? 0}",
            $"CustomerId={session?.CustomerId ?? 0}",
            $"ProductId={session?.ProductId ?? 0}",
            $"Request={NormalizeRuntimeClientRequestName(requestName)}"
        };

        if (statusCode.HasValue)
            details.Add($"StatusCode={Math.Clamp(statusCode.Value, 0, 999)}");

        details.Add($"FailureKind={NormalizeRuntimeClientFailureKind(failureKind)}");

        if (elapsedMilliseconds.HasValue)
            details.Add($"ElapsedMs={Math.Max(0, elapsedMilliseconds.Value)}");

        var safeMessage = SanitizeRuntimeClientMessage(string.IsNullOrWhiteSpace(message) ? BuildRuntimeClientFailureMessage(failureKind, statusCode, requestName) : message);
        if (!string.IsNullOrWhiteSpace(safeMessage))
            details.Add($"Message={safeMessage}");

        return string.Join("; ", details);
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
        if (HttpContext != null)
            Response.StatusCode = statusCode;

        var text = await GetLocalizedTextAsync(resourceKey, defaultValue);
        return Json(new { success = false, message = text, error = text });
    }

    protected IActionResult SafeSpeechUnavailable(string message = VoiceUnavailableMessage, int statusCode = 400)
    {
        if (HttpContext != null)
            Response.StatusCode = statusCode;

        var safeMessage = string.IsNullOrWhiteSpace(message) ? VoiceUnavailableMessage : message;
        return Json(new { success = false, message = safeMessage, error = safeMessage });
    }

    protected virtual string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        if (token.Length <= 6) return "*****";
        return token.Substring(0, 6) + "...";
    }

    protected virtual bool IsSessionExpired(InterviewSession session, DateTime? currentUtc = null)
    {
        var now = currentUtc ?? DateTime.UtcNow;
        return session != null &&
               session.TokenExpiryUtc.HasValue &&
               session.TokenExpiryUtc.Value <= now;
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

    protected virtual bool IsSessionUsable(InterviewSession session, DateTime? currentUtc = null)
    {
        if (session == null)
            return false;

        if (!session.IsActive || session.CompletedOnUtc.HasValue)
            return false;

        return session.TokenExpiryUtc.HasValue && !IsSessionExpired(session, currentUtc);
    }

    protected virtual string GetMockReportUrl(int sessionId)
    {
        return sessionId > 0
            ? Url?.RouteUrl(AIInterviewDefaults.MockReportRouteName, new { sessionId }) ?? string.Empty
            : string.Empty;
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

    protected virtual async Task<string> BuildStartLoginRedirectUrlAsync(int productId, string sponsorToken = null)
    {
        string returnUrl = null;
        if (productId > 0)
        {
            var product = await _productService.GetProductByIdAsync(productId);
            if (product != null)
            {
                returnUrl = await BuildProductRedirectUrlAsync(product, new Dictionary<string, string>
                {
                    ["sponsorToken"] = sponsorToken
                });
            }
        }

        if (string.IsNullOrWhiteSpace(returnUrl))
            returnUrl = Url?.RouteUrl(AIInterviewDefaults.IndexRouteName);

        return Url?.RouteUrl(global::Nop.Core.Http.NopRouteNames.General.LOGIN, new
        {
            returnUrl,
            interviewNotice = string.IsNullOrWhiteSpace(sponsorToken) ? null : "companySponsored"
        });
    }

    protected virtual string BuildCreateInviteLoginRedirectUrl()
    {
        var returnUrl = Url?.RouteUrl(AIInterviewDefaults.MockCreateInvitePageRouteName)
            ?? "/mockaiinterview/create-invite";
        return Url?.RouteUrl(global::Nop.Core.Http.NopRouteNames.General.LOGIN, new
        {
            returnUrl,
            interviewNotice = "companySponsored"
        }) ?? QueryHelpers.AddQueryString("/login", new Dictionary<string, string>
        {
            ["returnUrl"] = returnUrl,
            ["interviewNotice"] = "companySponsored"
        });
    }

    protected virtual bool IsAjaxRequest()
    {
        return string.Equals(Request?.Headers?.XRequestedWith.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }

    protected virtual IActionResult InviteMutationResult(bool success, string message)
    {
        if (IsAjaxRequest())
        {
            if (success)
                return Json(new { success = true, message });

            return Json(new { success = false, error = message });
        }

        if (success)
            _notificationService?.SuccessNotification(message);
        else
            _notificationService?.ErrorNotification(message);

        var referer = Request?.Headers?.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var absoluteReferer) &&
            Request?.Host.HasValue == true &&
            string.Equals(absoluteReferer.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return Redirect(absoluteReferer.PathAndQuery);
        }

        if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Relative, out _))
            return Redirect(referer);

        return RedirectToRoute(AIInterviewDefaults.MockEmployerManageRouteName);
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

    protected virtual int NormalizeQuestionCount(int questionCount)
    {
        return Math.Clamp(questionCount <= 0 ? 5 : questionCount, 1, 10);
    }

    protected virtual int ResolveMockQuestionCount()
    {
        return NormalizeQuestionCount(_aiInterviewSettings?.MockInterviewQuestionCount ?? 5);
    }

    protected virtual async Task<int> ResolveQuestionCountAsync(int productId)
    {
        if (productId <= 0 || _jobRequirementService == null)
            return 5;

        var requirements = await _jobRequirementService.GetRequirementsAsync(productId);
        return NormalizeQuestionCount(requirements?.QuestionCount ?? 5);
    }

    protected virtual async Task<JobApplication> GetLatestApplicationForStartAsync(Customer customer, Product product)
    {
        return ((await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id)) ?? new List<JobApplication>())
            .Where(a => a.ProductId == (product?.Id ?? 0))
            .OrderByDescending(a => a.CreatedOnUtc)
            .ThenByDescending(a => a.Id)
            .FirstOrDefault();
    }

    protected virtual async Task<JobRequirementsModel> GetStartRequirementsAsync(int productId)
    {
        if (productId <= 0 || _jobRequirementService == null)
            return new JobRequirementsModel();

        return await _jobRequirementService.GetRequirementsAsync(productId) ?? new JobRequirementsModel();
    }

    protected virtual async Task<bool> HasAnsweredTurnsAsync(InterviewSession session)
    {
        if (session == null || session.Id <= 0 || _turnService == null)
            return false;

        var turns = await _turnService.GetTurnsBySessionIdAsync(session.Id) ?? new List<InterviewTurn>();
        return turns.Any(turn => !string.IsNullOrWhiteSpace(turn.AnswerText));
    }

    protected virtual async Task ResetUnstartedPlannedTurnsAsync(InterviewSession session)
    {
        if (session == null || session.Id <= 0 || _turnService == null)
            return;

        var turns = (await _turnService.GetTurnsBySessionIdAsync(session.Id) ?? new List<InterviewTurn>()).ToList();
        if (!turns.Any() || turns.Any(turn => !string.IsNullOrWhiteSpace(turn.AnswerText)))
            return;

        await _turnService.DeleteInterviewTurnsAsync(turns);
    }

    protected virtual async Task<HashSet<int>> GetOwnedResumeDownloadIdsAsync(Customer customer)
    {
        if (customer == null)
            return new HashSet<int>();

        var applications = await _applicationService.GetJobApplicationsByCustomerIdAsync(customer.Id) ?? new List<JobApplication>();
        var sessions = await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id) ?? new List<InterviewSession>();
        return ResumeSelectionHelper.GetOwnedResumeDownloadIds(applications, sessions);
    }

    protected virtual async Task<bool> IsMockPracticeProductAsync(Product product)
    {
        if (product == null || product.ProductTemplateId <= 0 || _productTemplateService == null)
            return false;

        var productTemplate = await _productTemplateService.GetProductTemplateByIdAsync(product.ProductTemplateId);
        if (productTemplate == null)
            return false;

        return string.Equals(productTemplate.ViewPath, AIInterviewDefaults.MockPracticeProductTemplateViewPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(productTemplate.Name, AIInterviewDefaults.MockPracticeProductTemplateName, StringComparison.OrdinalIgnoreCase);
    }

    protected virtual string NormalizeInterviewType(InterviewSession session)
    {
        if (!string.IsNullOrWhiteSpace(session?.InterviewType))
            return session.InterviewType;

        return session?.JobApplicationId > 0 || session?.ProductId > 0
            ? AIInterviewDefaults.InterviewTypeJob
            : string.Empty;
    }

    protected virtual bool MatchesStartInterviewType(InterviewSession session, bool isMockPracticeProduct)
    {
        var normalizedInterviewType = NormalizeInterviewType(session);
        return isMockPracticeProduct
            ? string.Equals(normalizedInterviewType, AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase)
            : !string.Equals(normalizedInterviewType, AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase);
    }

    protected virtual int NormalizeRuntimeQuestionCount(InterviewSession session)
    {
        if (session?.QuestionCount > 0)
            return Math.Clamp(session.QuestionCount, 1, 10);

        return (session?.Difficulty ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "easy" => 2,
            "hard" => 4,
            _ => 3
        };
    }

    private static string NormalizeAttributeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = new string(value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')
            .ToArray());

        return string.Join(" ", sanitized
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private static bool MatchesAttributeKeyword(IEnumerable<string> candidates, IEnumerable<string> keywords)
    {
        var normalizedCandidates = (candidates ?? Enumerable.Empty<string>())
            .Select(NormalizeAttributeLabel)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return normalizedCandidates.Any(candidate => keywords.Any(keyword =>
        {
            var normalizedKeyword = NormalizeAttributeLabel(keyword);
            return string.Equals(candidate, normalizedKeyword, StringComparison.Ordinal) ||
                   candidate.Contains(normalizedKeyword, StringComparison.Ordinal);
        }));
    }

    private async Task<MockPracticeSelectionResult> SerializeSelectedProductAttributesAsync(Product product, IFormCollection form)
    {
        if (product == null || form == null || _productAttributeParser == null || _productAttributeService == null)
            return new MockPracticeSelectionResult(null, null, false, new List<string>());

        var errors = new List<string>();
        var attributesXml = await _productAttributeParser.ParseProductAttributesAsync(product, form, errors);
        if (errors.Count > 0)
            return new MockPracticeSelectionResult(null, null, false, errors);

        if (string.IsNullOrWhiteSpace(attributesXml))
            return new MockPracticeSelectionResult(null, null, false, new List<string>());

        var values = await _productAttributeParser.ParseProductAttributeValuesAsync(attributesXml);
        if (values == null || values.Count == 0)
            return new MockPracticeSelectionResult(null, null, false, new List<string>());

        var snapshots = new List<SelectedProductAttributeValueSnapshot>();
        var snapshotLabelsByValueId = new Dictionary<int, string[]>();
        foreach (var value in values.Where(value => value != null))
        {
            var mapping = await _productAttributeService.GetProductAttributeMappingByIdAsync(value.ProductAttributeMappingId);
            if (mapping == null)
                continue;

            var attribute = await _productAttributeService.GetProductAttributeByIdAsync(mapping.ProductAttributeId);
            if (attribute == null)
                continue;

            snapshots.Add(new SelectedProductAttributeValueSnapshot(
                attribute.Id,
                attribute.Name,
                mapping.TextPrompt,
                value.Id,
                value.Name));
            snapshotLabelsByValueId[value.Id] =
            [
                attribute.Name,
                mapping.TextPrompt
            ];
        }

        if (snapshots.Count == 0)
            return new MockPracticeSelectionResult(null, null, false, new List<string>());

        string difficulty = null;
        var hasPracticeSkill = false;

        foreach (var snapshot in snapshots)
        {
            snapshotLabelsByValueId.TryGetValue(snapshot.ValueId, out var attributeLabels);
            attributeLabels ??=
            [
                snapshot.AttributeName,
                snapshot.TextPrompt
            ];

            if (string.IsNullOrWhiteSpace(difficulty) &&
                MatchesAttributeKeyword(attributeLabels, PracticeDifficultyKeywords) &&
                !string.IsNullOrWhiteSpace(snapshot.Value))
            {
                difficulty = snapshot.Value;
            }

            if (!hasPracticeSkill &&
                MatchesAttributeKeyword(attributeLabels, PracticeSkillKeywords) &&
                !string.IsNullOrWhiteSpace(snapshot.Value))
            {
                hasPracticeSkill = true;
            }
        }

        if (string.IsNullOrWhiteSpace(difficulty) && !hasPracticeSkill)
        {
            _logger?.LogWarning(
                "AI Interview mock practice attributes were parsed but not identified for product {ProductId}. Posted form keys: {FormKeys}. Snapshot labels: {AttributeLabels}.",
                product.Id,
                string.Join(", ", form.Keys),
                string.Join(", ", snapshotLabelsByValueId.Values.SelectMany(labels => labels).Where(label => !string.IsNullOrWhiteSpace(label)).Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        return new MockPracticeSelectionResult(
            JsonSerializer.Serialize(new SelectedProductAttributesSnapshot(snapshots), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            difficulty,
            hasPracticeSkill,
            new List<string>());
    }

    protected virtual string ResolveMockPracticeDifficulty(string selectedProductAttributesJson, string difficultyFallback)
    {
        if (!string.IsNullOrWhiteSpace(selectedProductAttributesJson))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<SelectedProductAttributesSnapshot>(selectedProductAttributesJson);
                var selectedDifficulty = snapshot?.Attributes?.FirstOrDefault(attribute =>
                    MatchesAttributeKeyword([attribute.AttributeName], PracticeDifficultyKeywords));
                if (!string.IsNullOrWhiteSpace(selectedDifficulty?.Value))
                    return selectedDifficulty.Value;
            }
            catch
            {
            }
        }

        return !string.IsNullOrWhiteSpace(difficultyFallback) ? difficultyFallback : AIInterviewDefaults.DefaultInterviewDifficulty;
    }

    private async Task<(string ResourceKey, string ErrorMessage)> ValidateMockPracticeStartAsync(
        MockPracticeSelectionResult selectionResult,
        IFormFile resumeFile,
        InterviewSession reusableSession,
        int selectedResumeDownloadId,
        ISet<int> ownedResumeDownloadIds)
    {
        var validationErrors = new List<string>();
        var isDifficultyMissing = string.IsNullOrWhiteSpace(selectionResult?.Difficulty);

        if (selectionResult?.Errors?.Count > 0)
            validationErrors.AddRange(selectionResult.Errors.Where(error => !string.IsNullOrWhiteSpace(error)));

        if (isDifficultyMissing)
        {
            validationErrors.Add(await GetLocalizedTextAsync(
                "Plugins.Misc.AIInterview.MockPractice.DifficultyRequired",
                "Please select a practice difficulty."));
        }

        var hasOwnedSelectedResume = selectedResumeDownloadId > 0 &&
            ownedResumeDownloadIds != null &&
            ownedResumeDownloadIds.Contains(selectedResumeDownloadId);
        var hasResumeSource = resumeFile != null || hasOwnedSelectedResume || reusableSession?.ResumeDownloadId > 0;
        var isSkillOrResumeMissing = !(selectionResult?.HasPracticeSkill ?? false) && !hasResumeSource;
        if (isSkillOrResumeMissing)
        {
            validationErrors.Add(await GetLocalizedTextAsync(
                "Plugins.Misc.AIInterview.MockPractice.SkillOrResumeRequired",
                "Select a practice skill or provide a resume to start the practice interview."));
        }

        if (validationErrors.Count == 0)
            return (null, null);

        if (isDifficultyMissing && isSkillOrResumeMissing)
        {
            return (
                "Plugins.Misc.AIInterview.MockPractice.SelectionRequired",
                "We couldn't start your AI interview. Please select a difficulty level, a skill, or upload your resume before continuing.");
        }

        var distinctMessage = string.Join(" ", validationErrors.Distinct(StringComparer.OrdinalIgnoreCase));
        return ("Plugins.Misc.AIInterview.MockPractice.StartValidationFailed", distinctMessage);
    }

    protected virtual async Task<(string ResourceKey, string ErrorMessage)> ValidateStartResumePreconditionsAsync(
        JobRequirementsModel requirements,
        JobApplication application,
        IFormFile resumeFile,
        InterviewSession reusableSession,
        int selectedResumeDownloadId,
        ISet<int> ownedResumeDownloadIds)
    {
        if (resumeFile != null)
        {
            if (_resumeFileService == null)
                return ("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid", "Resume upload is unavailable right now.");

            var validation = _resumeFileService.ValidateResumeFile(resumeFile);
            if (!validation.Success)
            {
                return ("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid",
                    string.IsNullOrWhiteSpace(validation.ErrorMessage)
                        ? await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid", "Allowed resume file types: PDF, DOCX. Maximum size: 5 MB.")
                        : validation.ErrorMessage);
            }
        }

        if (resumeFile == null && selectedResumeDownloadId > 0 && (ownedResumeDownloadIds == null || !ownedResumeDownloadIds.Contains(selectedResumeDownloadId)))
        {
            return ("Plugins.Misc.AIInterview.Apply.PreviousResume.Invalid",
                await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Apply.PreviousResume.Invalid", "Please select a valid previous resume."));
        }

        var hasStoredResume = application?.ResumeDownloadId > 0 || (selectedResumeDownloadId > 0 && ownedResumeDownloadIds != null && ownedResumeDownloadIds.Contains(selectedResumeDownloadId));
        if (requirements?.ResumeRequired == true && !hasStoredResume && resumeFile == null)
        {
            var canContinueStartedInterview = reusableSession != null && await HasAnsweredTurnsAsync(reusableSession);
            if (!canContinueStartedInterview)
            {
                return ("Plugins.Misc.AIInterview.Apply.ResumeFile.Required",
                    await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Required", "Resume required. Upload a resume or select a previous resume."));
            }
        }

        return (null, null);
    }

    protected virtual async Task<(JobApplication Application, string ErrorMessage, bool ResumeChanged)> ResolveApplicationForStartAsync(
        Customer customer,
        Product product,
        IFormFile resumeFile,
        int selectedResumeDownloadId = 0,
        JobApplication application = null)
    {
        application ??= await GetLatestApplicationForStartAsync(customer, product);

        if (resumeFile != null)
        {
            if (_resumeFileService == null)
                return (null, "Resume upload is unavailable right now.", false);

            var validation = _resumeFileService.ValidateResumeFile(resumeFile);
            if (!validation.Success)
                return (null, validation.ErrorMessage, false);

            var storedResume = await _resumeFileService.StoreResumeAsync(resumeFile);
            if (!storedResume.Success)
                return (null, storedResume.ErrorMessage, false);

            if (application == null)
            {
                application = new JobApplication
                {
                    CustomerId = customer.Id,
                    ProductId = product?.Id ?? 0,
                    JobTitle = product?.Name ?? "Interview",
                    ResumeDownloadId = storedResume.DownloadId,
                    Status = JobApplicationStatuses.Applied,
                    CreatedOnUtc = DateTime.UtcNow
                };
                await _applicationService.InsertJobApplicationAsync(application);
            }
            else
            {
                application.ResumeDownloadId = storedResume.DownloadId;
                application.ResumeProfileJson = null;
                application.ResumeProfileGeneratedOnUtc = null;
                application.ResumeProfileError = null;
                await _applicationService.UpdateJobApplicationAsync(application);
            }

            if (_resumeProfileService != null)
                await _resumeProfileService.EnsureResumeProfileAsync(application, product, forceRegenerate: true);

            return (application, null, true);
        }

        if (selectedResumeDownloadId > 0)
        {
            var resumeChanged = application == null || application.ResumeDownloadId != selectedResumeDownloadId;
            if (application == null)
            {
                application = new JobApplication
                {
                    CustomerId = customer.Id,
                    ProductId = product?.Id ?? 0,
                    JobTitle = product?.Name ?? "Interview",
                    ResumeDownloadId = selectedResumeDownloadId,
                    Status = JobApplicationStatuses.Applied,
                    CreatedOnUtc = DateTime.UtcNow
                };
                await _applicationService.InsertJobApplicationAsync(application);
            }
            else if (resumeChanged)
            {
                application.ResumeDownloadId = selectedResumeDownloadId;
                application.ResumeProfileJson = null;
                application.ResumeProfileGeneratedOnUtc = null;
                application.ResumeProfileError = null;
                await _applicationService.UpdateJobApplicationAsync(application);
            }

            if (application.ResumeDownloadId > 0 && _resumeProfileService != null)
                await _resumeProfileService.EnsureResumeProfileAsync(application, product, forceRegenerate: resumeChanged);

            return (application, null, resumeChanged);
        }

        if (application != null && application.ResumeDownloadId > 0 && string.IsNullOrWhiteSpace(application.ResumeProfileJson) && _resumeProfileService != null)
            await _resumeProfileService.EnsureResumeProfileAsync(application, product);

        return (application, null, false);
    }

    protected virtual async Task<(int ResumeDownloadId, string ErrorMessage, bool ResumeChanged)> ResolvePracticeResumeForStartAsync(
        Product product,
        IFormFile resumeFile,
        int selectedResumeDownloadId,
        InterviewSession session = null)
    {
        if (resumeFile != null)
        {
            if (_resumeFileService == null)
                return (0, "Resume upload is unavailable right now.", false);

            var validation = _resumeFileService.ValidateResumeFile(resumeFile);
            if (!validation.Success)
                return (0, validation.ErrorMessage, false);

            var storedResume = await _resumeFileService.StoreResumeAsync(resumeFile);
            if (!storedResume.Success)
                return (0, storedResume.ErrorMessage, false);

            if (session != null)
            {
                session.ResumeDownloadId = storedResume.DownloadId;
                session.ResumeProfileJson = null;
                session.ResumeProfileGeneratedOnUtc = null;
                session.ResumeProfileError = null;
                await _interviewSessionService.UpdateInterviewSessionAsync(session);
            }

            return (storedResume.DownloadId, null, true);
        }

        if (selectedResumeDownloadId > 0)
        {
            var resumeChanged = session == null || session.ResumeDownloadId != selectedResumeDownloadId;
            if (session != null && resumeChanged)
            {
                session.ResumeDownloadId = selectedResumeDownloadId;
                session.ResumeProfileJson = null;
                session.ResumeProfileGeneratedOnUtc = null;
                session.ResumeProfileError = null;
                await _interviewSessionService.UpdateInterviewSessionAsync(session);
            }

            return (selectedResumeDownloadId, null, resumeChanged);
        }

        return (session?.ResumeDownloadId ?? 0, null, false);
    }

    protected virtual async Task<InterviewSession> ValidateActiveRuntimeTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        return IsSessionUsable(session) ? session : null;
    }

    protected virtual async Task<string> ResolveRuntimeTokenFailureReasonCodeAsync(string token, InterviewSession session = null)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "session-not-found";

        session ??= await _interviewSessionService.GetSessionByTokenAsync(token);
        if (session == null)
            return "session-not-found";

        if (session.CompletedOnUtc.HasValue)
            return "session-completed";

        if (!session.IsActive)
            return "session-inactive";

        if (!session.TokenExpiryUtc.HasValue)
            return "token-expiry-missing";

        if (session.TokenExpiryUtc.HasValue && session.TokenExpiryUtc.Value <= DateTime.UtcNow)
            return "token-expired";

        return "session-not-found";
    }

    protected virtual DateTime? NormalizeRuntimeTokenExpiryForClient(DateTime? tokenExpiryUtc)
    {
        if (!tokenExpiryUtc.HasValue)
            return null;

        var value = tokenExpiryUtc.Value;
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    public async Task<IActionResult> Start(int productId = 0, string sponsorToken = null)
    {
        if (productId > 0)
        {
            var product = await _productService.GetProductByIdAsync(productId);
            if (product != null)
            {
                if (_jobProductAccessService != null && !await _jobProductAccessService.CanAcceptJobApplicationsAsync(product))
                    return NotFound();

                var redirectUrl = await BuildProductRedirectUrlAsync(product, new Dictionary<string, string>
                {
                    ["sponsorToken"] = sponsorToken
                });
                if (!string.IsNullOrWhiteSpace(redirectUrl))
                    return Redirect(redirectUrl);
            }
        }

        return RedirectToRoute("Homepage");
    }

    [HttpPost]
    [ActionName("Start")]
    public async Task<IActionResult> StartPost(Microsoft.AspNetCore.Http.IFormCollection form, int productId = 0, string difficulty = AIInterviewDefaults.DefaultInterviewDifficulty, string sponsorToken = null)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var isRegisteredCustomer = customer != null &&
            await _customerService.IsRegisteredAsync(customer) &&
            !string.IsNullOrWhiteSpace(customer.Email);
        if (!isRegisteredCustomer)
        {
            var redirectUrl = await BuildStartLoginRedirectUrlAsync(productId, sponsorToken);
            return Json(new
            {
                success = false,
                message = await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized", "Unauthorized runtime request."),
                error = await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized", "Unauthorized runtime request."),
                requiresLogin = true,
                redirect = redirectUrl
            });
        }

        var product = productId > 0 ? await _productService.GetProductByIdAsync(productId) : null;
        if (_jobProductAccessService != null && !await _jobProductAccessService.CanAcceptJobApplicationsAsync(product))
            return await LocalizedErrorAsync("Common.NotAvailable", "The requested job is not available.", 404);

        var isMockPracticeProduct = await IsMockPracticeProductAsync(product);
        MockPracticeSelectionResult mockPracticeSelection = null;
        string selectedProductAttributesJson = null;
        if (isMockPracticeProduct)
        {
            mockPracticeSelection = await SerializeSelectedProductAttributesAsync(product, form);
            selectedProductAttributesJson = mockPracticeSelection.SelectedProductAttributesJson;
            difficulty = ResolveMockPracticeDifficulty(selectedProductAttributesJson,
                !string.IsNullOrWhiteSpace(mockPracticeSelection.Difficulty) ? mockPracticeSelection.Difficulty : (!string.IsNullOrWhiteSpace(form["difficulty"]) ? form["difficulty"] : difficulty));
        }
        else if (product != null && _jobInterviewExperienceService != null)
            difficulty = await _jobInterviewExperienceService.ResolveInterviewDifficultyAsync(product, form) ?? AIInterviewDefaults.DefaultInterviewDifficulty;
        else
            difficulty = !string.IsNullOrWhiteSpace(form["difficulty"]) ? form["difficulty"] : difficulty ?? AIInterviewDefaults.DefaultInterviewDifficulty;

        var resumeFile = form?.Files?.GetFile("ResumeFile");
        var selectedResumeDownloadId = int.TryParse(form?["SelectedResumeDownloadId"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSelectedResumeDownloadId)
            ? parsedSelectedResumeDownloadId
            : 0;
        var existingApplication = isMockPracticeProduct ? null : await GetLatestApplicationForStartAsync(customer, product);
        var ownedResumeDownloadIds = await GetOwnedResumeDownloadIdsAsync(customer);
        var customerSessions = (await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id) ?? new List<InterviewSession>())
            .Where(s => s.ProductId == productId)
            .OrderByDescending(s => s.CreatedOnUtc)
            .ThenByDescending(s => s.Id)
            .ToList();

        var now = DateTime.UtcNow;
        var reusableSession = customerSessions.FirstOrDefault(s =>
            IsSessionUsable(s, now) &&
            MatchesStartInterviewType(s, isMockPracticeProduct));
        var requirements = await GetStartRequirementsAsync(productId);
        var resumeValidation = await ValidateStartResumePreconditionsAsync(requirements, existingApplication, resumeFile, reusableSession, selectedResumeDownloadId, ownedResumeDownloadIds);
        if (!string.IsNullOrWhiteSpace(resumeValidation.ErrorMessage))
            return await LocalizedErrorAsync(resumeValidation.ResourceKey, resumeValidation.ErrorMessage);

        if (isMockPracticeProduct)
        {
            var mockPracticeValidation = await ValidateMockPracticeStartAsync(mockPracticeSelection, resumeFile, reusableSession, selectedResumeDownloadId, ownedResumeDownloadIds);
            if (!string.IsNullOrWhiteSpace(mockPracticeValidation.ErrorMessage))
                return await LocalizedErrorAsync(mockPracticeValidation.ResourceKey, mockPracticeValidation.ErrorMessage);
        }

        if (reusableSession != null)
        {
            var sessionUpdated = false;
            if (isMockPracticeProduct)
            {
                var mockQuestionCount = ResolveMockQuestionCount();
                var resumeStorageStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var resumeResolution = await ResolvePracticeResumeForStartAsync(product, resumeFile, selectedResumeDownloadId, reusableSession);
                await LogRuntimeIssueAsync(
                    "AI Interview preparation stage",
                    $"SessionId={reusableSession.Id}; ProductId={productId}; CustomerId={customer.Id}; Stage=resume-storage; ElapsedMs={resumeStorageStopwatch.ElapsedMilliseconds}; Success={string.IsNullOrWhiteSpace(resumeResolution.ErrorMessage).ToString().ToLowerInvariant()}; Reason={SanitizeRuntimeClientMessage(resumeResolution.ErrorMessage)}.",
                    customer);
                if (!string.IsNullOrWhiteSpace(resumeResolution.ErrorMessage))
                    return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid", resumeResolution.ErrorMessage);

                if (!reusableSession.StartedOnUtc.HasValue && reusableSession.QuestionCount != mockQuestionCount)
                {
                    reusableSession.QuestionCount = mockQuestionCount;
                    sessionUpdated = true;
                }

                if (!string.Equals(reusableSession.InterviewType, AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.Ordinal))
                {
                    reusableSession.InterviewType = AIInterviewDefaults.InterviewTypeMockPractice;
                    sessionUpdated = true;
                }

                if (reusableSession.SourceProductId != productId)
                {
                    reusableSession.SourceProductId = productId;
                    sessionUpdated = true;
                }

                if (!string.Equals(reusableSession.Difficulty, difficulty, StringComparison.Ordinal))
                {
                    reusableSession.Difficulty = difficulty;
                    sessionUpdated = true;
                }

                if (!string.Equals(reusableSession.SelectedProductAttributesJson, selectedProductAttributesJson, StringComparison.Ordinal))
                {
                    reusableSession.SelectedProductAttributesJson = selectedProductAttributesJson;
                    sessionUpdated = true;
                }

                if (resumeResolution.ResumeChanged)
                    await ResetUnstartedPlannedTurnsAsync(reusableSession);
            }
            else
            {
                var applicationResolution = await ResolveApplicationForStartAsync(customer, product, resumeFile, selectedResumeDownloadId, existingApplication);
                if (!string.IsNullOrWhiteSpace(applicationResolution.ErrorMessage))
                    return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid", applicationResolution.ErrorMessage);

                var application = applicationResolution.Application;
                if (application != null && reusableSession.JobApplicationId != application.Id)
                {
                    reusableSession.JobApplicationId = application.Id;
                    sessionUpdated = true;
                }

                if (applicationResolution.ResumeChanged)
                    await ResetUnstartedPlannedTurnsAsync(reusableSession);
            }

            if (sessionUpdated)
                await _interviewSessionService.UpdateInterviewSessionAsync(reusableSession);

            return Json(new
            {
                sessionKey = reusableSession.SessionKey,
                token = reusableSession.Token,
                runtimeUrl = Url?.RouteUrl(AIInterviewDefaults.MockRuntimeRouteName, new { token = reusableSession.Token })
            });
        }

        int sponsorInviteId = 0;
        if (!string.IsNullOrEmpty(sponsorToken))
        {
            var invite = await _inviteService.GetSponsorInviteByCodeAsync(sponsorToken);
            var sponsoredAttempts = invite == null || _interviewSessionService == null
                ? 0
                : await _interviewSessionService.GetSponsorInviteAttemptCountAsync(invite.Id);
            if (invite != null &&
                invite.ProductId == productId &&
                invite.IsActive &&
                (!invite.ExpiryDateUtc.HasValue || invite.ExpiryDateUtc > DateTime.UtcNow) &&
                string.Equals(invite.Email, customer.Email, StringComparison.OrdinalIgnoreCase) &&
                sponsoredAttempts < invite.MaxAttempts)
            {
                // Persist the sponsor context now; eligibility and charging are finalized in runtime Begin.
                sponsorInviteId = invite.Id;
            }
        }

        JobApplication newSessionApplication = null;
        var sessionResumeDownloadId = 0;
        if (isMockPracticeProduct)
        {
            var resumeStorageStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var newSessionResumeResolution = await ResolvePracticeResumeForStartAsync(product, resumeFile, selectedResumeDownloadId);
            await LogRuntimeIssueAsync(
                "AI Interview preparation stage",
                $"SessionId=0; ProductId={productId}; CustomerId={customer.Id}; Stage=resume-storage; ElapsedMs={resumeStorageStopwatch.ElapsedMilliseconds}; Success={string.IsNullOrWhiteSpace(newSessionResumeResolution.ErrorMessage).ToString().ToLowerInvariant()}; Reason={SanitizeRuntimeClientMessage(newSessionResumeResolution.ErrorMessage)}.",
                customer);
            if (!string.IsNullOrWhiteSpace(newSessionResumeResolution.ErrorMessage))
            {
                await LogRuntimeIssueAsync("AI Interview start resume persistence failure",
                    $"ProductId={productId}; CustomerId={customer.Id}; Reason={newSessionResumeResolution.ErrorMessage}.",
                    customer);
                return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid", newSessionResumeResolution.ErrorMessage);
            }

            sessionResumeDownloadId = newSessionResumeResolution.ResumeDownloadId;
        }
        else
        {
            var newSessionApplicationResolution = await ResolveApplicationForStartAsync(customer, product, resumeFile, selectedResumeDownloadId, existingApplication);
            if (!string.IsNullOrWhiteSpace(newSessionApplicationResolution.ErrorMessage))
            {
                await LogRuntimeIssueAsync("AI Interview start resume persistence failure",
                    $"ProductId={productId}; CustomerId={customer.Id}; Reason={newSessionApplicationResolution.ErrorMessage}.",
                    customer);
                return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Apply.ResumeFile.Invalid", newSessionApplicationResolution.ErrorMessage);
            }

            newSessionApplication = newSessionApplicationResolution.Application;
        }

        var sessionCreatedUtc = DateTime.UtcNow;
        var session = new InterviewSession
        {
            CustomerId = customer.Id,
            ProductId = productId,
            JobApplicationId = newSessionApplication?.Id ?? 0,
            InterviewType = isMockPracticeProduct ? AIInterviewDefaults.InterviewTypeMockPractice : AIInterviewDefaults.InterviewTypeJob,
            SourceProductId = productId,
            SessionKey = Guid.NewGuid().ToString("N"),
            Difficulty = difficulty,
            ResumeDownloadId = sessionResumeDownloadId,
            SelectedProductAttributesJson = selectedProductAttributesJson,
            Token = Guid.NewGuid().ToString("N"),
            TokenExpiryUtc = sessionCreatedUtc.AddMinutes(AIInterviewDefaults.RuntimeTokenLifetimeMinutes),
            IsActive = true,
            QuestionCount = isMockPracticeProduct ? ResolveMockQuestionCount() : await ResolveQuestionCountAsync(productId),
            SponsorInviteId = sponsorInviteId,
            StartedOnUtc = null,
            CreatedOnUtc = sessionCreatedUtc
        };
        await _interviewSessionService.InsertInterviewSessionAsync(session);
        _logger?.LogInformation("AIInterview new session created for customer {CustomerId}, product {ProductId}, session {SessionId}.",
            customer.Id, productId, session.Id);

        return Json(new
        {
            sessionKey = session.SessionKey,
            token = session.Token,
            runtimeUrl = Url?.RouteUrl(AIInterviewDefaults.MockRuntimeRouteName, new { token = session.Token })
        });
    }

    public async Task<IActionResult> Runtime(string token)
    {
        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        if (!IsSessionUsable(session))
            return Redirect(await GetRestartUrlAsync(session));

        Nop.Plugin.Misc.AIInterview.Models.InterviewRuntimeModel model;
        if (_interviewRuntimeService == null)
        {
            var productName = (await _productService.GetProductByIdAsync(session.ProductId))?.Name ?? "Interview";
            model = new Nop.Plugin.Misc.AIInterview.Models.InterviewRuntimeModel
            {
                IsPracticeInterview = string.Equals(session.InterviewType, AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase),
                SessionId = session.Id,
                ProductId = session.ProductId,
                QuestionCount = NormalizeRuntimeQuestionCount(session),
                SessionKey = session.SessionKey,
                Token = session.Token,
                Difficulty = session.Difficulty,
                ProductName = productName,
                RuntimeTopic = productName,
                CurrentQuestion = string.Empty,
                IsMockMode = true
            };
        }
        else
        {
            model = await _interviewRuntimeService.GetRuntimeModelAsync(token);
        }

        await ApplyRuntimeClientSettingsAsync(model, session);

        if (_interviewStartCreditService != null)
        {
            var eligibility = await _interviewStartCreditService.CheckEligibilityAsync(session);
            model.ClientSettings.CreditEligible = eligibility.Eligible;
            model.ClientSettings.CreditWarningMessage = eligibility.Eligible ? string.Empty : InsufficientCreditsMessage;
            model.ClientSettings.PricingUrl = "/pricing";
        }

        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/Runtime.cshtml", model);
    }

    protected virtual async Task ApplyRuntimeClientSettingsAsync(Nop.Plugin.Misc.AIInterview.Models.InterviewRuntimeModel model, InterviewSession session)
    {
        if (model == null)
            return;

        model.ClientSettings ??= new Nop.Plugin.Misc.AIInterview.Models.RuntimeClientSettingsModel();
        model.QuestionCount = session != null ? NormalizeRuntimeQuestionCount(session) : model.QuestionCount > 0 ? Math.Clamp(model.QuestionCount, 1, 10) : 5;
        model.ClientSettings.QuestionCount = model.QuestionCount;
        model.ClientSettings.SubmitAnswerUrl = Url?.RouteUrl(AIInterviewDefaults.MockSubmitAnswerRouteName);
        model.ClientSettings.BeginInterviewUrl = Url?.RouteUrl(AIInterviewDefaults.MockBeginRouteName);
        model.ClientSettings.PrepareInterviewUrl = Url?.RouteUrl(AIInterviewDefaults.MockPrepareRouteName);
        model.ClientSettings.CompletionStatusUrl = Url?.Action("CompletionStatus", "MockAiInterview");
        model.ClientSettings.CompleteInterviewUrl = Url?.RouteUrl(AIInterviewDefaults.MockStopRouteName);
        model.ClientSettings.RefreshTokenUrl = Url?.RouteUrl(AIInterviewDefaults.MockRefreshTokenRouteName);
        model.ClientSettings.StopInterviewUrl = Url?.RouteUrl(AIInterviewDefaults.MockStopRouteName);
        model.ClientSettings.FeedbackUrl = Url?.RouteUrl(AIInterviewDefaults.MockFeedbackRouteName);
        model.ClientSettings.SpeechTokenUrl = Url?.RouteUrl(AIInterviewDefaults.MockSpeechTokenRouteName);
        model.ClientSettings.SpeechUsageUrl = Url?.RouteUrl(AIInterviewDefaults.MockSpeechUsageRouteName);
        model.ClientSettings.AcknowledgeGuidelinesUrl = Url?.RouteUrl(AIInterviewDefaults.MockAcknowledgeGuidelinesRouteName);
        model.ClientSettings.RuntimeClientEventUrl = Url?.RouteUrl(AIInterviewDefaults.MockRuntimeClientEventRouteName);
        model.ClientSettings.ProductName = model.ProductName;
        model.ClientSettings.FinalCompletionSpeech = await GetLocalizedTextAsync(AIInterviewPlugin.FinalCompletionSpeechResourceKey, AIInterviewPlugin.DefaultFinalCompletionSpeech);
        model.ClientSettings.Token = session?.Token;
        var reportReady = session?.CompletedOnUtc.HasValue == true && !string.IsNullOrWhiteSpace(session.ReportData);
        model.ReportUrl = reportReady ? GetMockReportUrl(session?.Id ?? model.SessionId) : string.Empty;
        model.ClientSettings.ReportUrl = model.ReportUrl;
        model.ClientSettings.TokenExpiryUtc = NormalizeRuntimeTokenExpiryForClient(session?.TokenExpiryUtc);
        model.ClientSettings.SpeechAvailable = model.ClientSettings.SpeechAvailable && !string.IsNullOrWhiteSpace(model.ClientSettings.SpeechTokenUrl);
        model.ClientSettings.RecordingUploadUrl = Url?.RouteUrl(AIInterviewDefaults.MockRecordingUploadRouteName);
        model.ClientSettings.RecordingAvailable = model.ClientSettings.RecordingAvailable && !string.IsNullOrWhiteSpace(model.ClientSettings.RecordingUploadUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Prepare(string token)
    {
        if (_interviewRuntimeService == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unavailable", "Interview preparation is unavailable.");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null || !await _customerService.IsRegisteredAsync(customer))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized", "Unauthorized runtime request.", 401);

        var session = await ValidateActiveRuntimeTokenAsync(token);
        if (session == null || session.CustomerId != customer.Id)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        var result = await _interviewRuntimeService.PrepareInterviewAsync(token, customer);
        if (result == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        return Json(new
        {
            success = result.Success,
            ready = result.Ready,
            message = result.Message,
            elapsedMilliseconds = result.ElapsedMilliseconds,
            expectedQuestionCount = result.ExpectedQuestionCount,
            persistedQuestionCount = result.PersistedQuestionCount
        });
    }

    [HttpPost]
    public async Task<IActionResult> Begin(string token)
    {
        if (_interviewRuntimeService == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unavailable", "Interview start is unavailable.");

        var customer = await _workContext.GetCurrentCustomerAsync();
        var session = await ValidateActiveRuntimeTokenAsync(token);
        if (session == null)
        {
            var reasonCode = await ResolveRuntimeTokenFailureReasonCodeAsync(token);
            await LogRuntimeIssueAsync(
                "AI Interview begin rejected invalid session",
                $"Endpoint=begin; Token={MaskToken(token)}; ReasonCode={reasonCode}; SessionId=0; CustomerId=0; ProductId=0;",
                await ResolveLogCustomerAsync(null, customer));

            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");
        }

        if (customer == null || !await _customerService.IsRegisteredAsync(customer) || session.CustomerId != customer.Id)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized", "Unauthorized runtime request.", 401);

        var wasStartedBeforeBegin = session.StartedOnUtc.HasValue;
        if (_interviewStartCreditService != null)
        {
            var chargeResult = await _interviewStartCreditService.ChargeAsync(session);
            if (!chargeResult.Eligible)
            {
                return Json(new
                {
                    success = false,
                    insufficientCredits = true,
                    message = InsufficientCreditsMessage,
                    redirect = "/pricing",
                    redirectDelayMilliseconds = 5000
                });
            }
        }

        Nop.Plugin.Misc.AIInterview.Models.InterviewRuntimeModel model;
        try
        {
            model = await _interviewRuntimeService.BeginInterviewAsync(token, customer);
        }
        catch (Exception exception)
        {
            await RefundFailedInterviewStartAsync(session, wasStartedBeforeBegin);
            await LogRuntimeIssueAsync(
                "AI Interview begin failed after credit authorization",
                $"Endpoint=begin; ReasonCode={FirstQuestionStartFailureReasonCode}; SessionId={session.Id}; CustomerId={session.CustomerId}; ProductId={session.ProductId}; Error={SanitizeRuntimeClientMessage(exception.Message)}.",
                customer);
            return Json(new { success = false, message = "AI service unavailable. Please try again later." });
        }
        if (model == null)
        {
            await LogRuntimeIssueAsync(
                "AI Interview begin rejected invalid session",
                $"Endpoint=begin; Token={MaskToken(token)}; ReasonCode=runtime-model-not-found; SessionId={session.Id}; CustomerId={session.CustomerId}; ProductId={session.ProductId};",
                await ResolveLogCustomerAsync(session, customer));

            await RefundFailedInterviewStartAsync(session, wasStartedBeforeBegin);
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");
        }

        var currentQuestion = model.CurrentQuestion?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentQuestion) ||
            string.Equals(currentQuestion, "AI service unavailable. Please try again later.", StringComparison.OrdinalIgnoreCase))
        {
            await RefundFailedInterviewStartAsync(session, wasStartedBeforeBegin);
            return Json(new
            {
                success = false,
                message = string.IsNullOrWhiteSpace(currentQuestion) ? "AI service unavailable. Please try again later." : currentQuestion
            });
        }

        var currentTurn = model.Turns?.LastOrDefault(turn => string.IsNullOrWhiteSpace(turn.AnswerText))
            ?? model.Turns?.LastOrDefault();

        return Json(new
        {
            success = true,
            message = "Interview started.",
            question = currentQuestion,
            turn = currentTurn,
            turns = model.Turns
        });
    }

    protected virtual async Task RefundFailedInterviewStartAsync(InterviewSession session, bool wasStartedBeforeBegin)
    {
        if (_interviewStartCreditService == null || session == null || wasStartedBeforeBegin)
            return;

        var persistedSession = await _interviewSessionService.GetInterviewSessionByIdAsync(session.Id) ?? session;
        if (persistedSession.StartedOnUtc.HasValue)
            return;

        var refunded = await _interviewStartCreditService.RefundAsync(
            persistedSession,
            FirstQuestionStartFailureReasonCode,
            CreditRefundNotice);
        if (!refunded)
            return;

        await _interviewStartCreditService.NotifyRefundAsync(persistedSession, FirstQuestionStartFailureReasonCode);
        _logger?.LogWarning(
            "AIInterview start credit reversed. ReasonCode={ReasonCode}; SessionId={SessionId}; CustomerId={CustomerId}; Message={Message}",
            FirstQuestionStartFailureReasonCode, session.Id, session.CustomerId, CreditRefundNotice);
    }

    protected async Task<string> GetRestartUrlAsync(InterviewSession session)
    {
        var restartUrl = Url?.RouteUrl("Homepage") ?? "/";
        if (session?.ProductId > 0 && _urlRecordService != null)
        {
                var product = await _productService.GetProductByIdAsync(session.ProductId);
                if (product != null)
                {
                    var seName = await _urlRecordService.GetSeNameAsync(product);
                    if (!string.IsNullOrWhiteSpace(seName))
                        restartUrl = $"/{seName}?interviewError=expired";
                }
            }

        return restartUrl;
    }

    protected async Task<IActionResult> RuntimeErrorAsync(string resourceKey, string defaultValue, int statusCode, InterviewSession session = null)
    {
        if (HttpContext != null)
            Response.StatusCode = statusCode;

        var restartUrl = await GetRestartUrlAsync(session);

        return View("~/Plugins/Misc.AIInterview/Views/RuntimeError.cshtml", new Models.RuntimeErrorModel
        {
            Message = await GetLocalizedTextAsync(resourceKey, defaultValue),
            StatusCode = statusCode,
            RestartUrl = restartUrl
        });
    }

    [HttpPost]
    public async Task<IActionResult> AcknowledgeGuidelines(string token, string acknowledgedTimestamp, string userAgent, string screenSize, string viewportSize)
    {
        var session = await _interviewSessionService.GetSessionByTokenAsync(token);
        var isValidSession = IsSessionUsable(session);
        var reasonCode = isValidSession ? "valid" : await ResolveRuntimeTokenFailureReasonCodeAsync(token, session);
        var maskedToken = MaskToken(token);
        var fullMessage = $"Event=RuntimeGuidelinesAcknowledged; Token={maskedToken}; ReasonCode={reasonCode}; SessionId={session?.Id ?? 0}; CustomerId={session?.CustomerId ?? 0}; ProductId={session?.ProductId ?? 0}; AcknowledgedTimestamp={acknowledgedTimestamp ?? string.Empty}; UserAgent={userAgent ?? string.Empty}; ScreenSize={screenSize ?? string.Empty}; ViewportSize={viewportSize ?? string.Empty};";

        var logCustomer = await ResolveLogCustomerAsync(session);
        if (_nopLogger != null)
            await _nopLogger.InsertLogAsync(NopLogLevel.Information, "AI Interview runtime guidelines acknowledged", fullMessage, logCustomer);
        _logger?.LogInformation("AIInterview runtime guidelines acknowledged for session {SessionId}, customer {CustomerId}, product {ProductId}, token {Token}.",
            session?.Id ?? 0, session?.CustomerId ?? 0, session?.ProductId ?? 0, maskedToken);
        await LogRuntimeActivityAsync(
            isValidSession ? session : null,
            "AIInterview.Runtime.GuidelinesAcknowledged",
            BuildRuntimeActivityComment(session, "Guidelines acknowledged.", acknowledgedTimestamp, screenSize, viewportSize),
            logCustomer);

        if (!isValidSession)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        return Json(new { success = true, message = "Guidelines acknowledgement logged." });
    }

    [HttpPost]
    public async Task<IActionResult> RuntimeClientEvent(string token, string eventType, string requestName, int? statusCode, string message, string failureKind, long? elapsedMilliseconds, bool? success = null)
    {
        var resolvedSession = await _interviewSessionService.GetSessionByTokenAsync(token);
        var isValidSession = IsSessionUsable(resolvedSession);
        var reasonCode = isValidSession ? "valid" : await ResolveRuntimeTokenFailureReasonCodeAsync(token, resolvedSession);
        var session = isValidSession ? resolvedSession : null;

        var safeRequestName = NormalizeRuntimeClientRequestName(requestName);
        var safeEventType = (eventType ?? string.Empty).Trim().ToLowerInvariant();
        var safeFailureKind = NormalizeRuntimeClientFailureKind(failureKind);
        var safeMessage = BuildRuntimeClientFailureMessage(safeFailureKind, statusCode, safeRequestName);

        if (string.Equals(safeRequestName, "runtime-client-event", StringComparison.OrdinalIgnoreCase))
            return Json(new { success = false, message = "Runtime client-event logging is not recursive." });

        if (session == null)
        {
            if (string.Equals(safeEventType, "stage-timing", StringComparison.OrdinalIgnoreCase))
            {
                var safeStageName = NormalizeRuntimeClientStageName(requestName);
                if (_nopLogger != null)
                {
                    await _nopLogger.InsertLogAsync(
                        NopLogLevel.Information,
                        "AI Interview runtime client stage timing ignored",
                        $"Token={MaskToken(token)}; Stage={safeStageName}; ElapsedMs={Math.Max(0, elapsedMilliseconds ?? 0)}; ReasonCode={reasonCode};",
                        await ResolveLogCustomerAsync());
                }

                return Json(new { success = false, message = "Runtime client event ignored for invalid session." });
            }

            await LogRuntimeIssueAsync(
                "AI Interview runtime client request failure",
                $"Event=RuntimeClientRequestFailed; Token={MaskToken(token)}; ReasonCode={reasonCode}; Request={safeRequestName}; StatusCode={statusCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}; FailureKind={safeFailureKind}; ElapsedMs={Math.Max(0, elapsedMilliseconds ?? 0)}; Message={safeMessage};",
                await ResolveLogCustomerAsync());

            return Json(new { success = false, message = "Runtime client event ignored for invalid session." });
        }

        if (string.Equals(safeEventType, "stage-timing", StringComparison.OrdinalIgnoreCase))
        {
            var elapsed = Math.Max(0, elapsedMilliseconds ?? 0);
            var stageSucceeded = success == true;
            var safeStageName = NormalizeRuntimeClientStageName(requestName);
            var stageComment = $"SessionId={session.Id}; CustomerId={session.CustomerId}; ProductId={session.ProductId}; Stage={safeStageName}; ElapsedMs={elapsed}; Success={stageSucceeded.ToString().ToLowerInvariant()}";
            await LogRuntimeActivityAsync(session, "AIInterview.Runtime.ClientStageTiming", stageComment);

            return Json(new { success = true });
        }

        var comment = BuildRuntimeClientFailureActivityComment(session, safeRequestName, statusCode, safeMessage, safeFailureKind, elapsedMilliseconds);
        await LogRuntimeActivityAsync(session, "AIInterview.Runtime.NetworkRequestFailed", comment);
        await LogRuntimeIssueAsync("AI Interview runtime client request failure", comment, await ResolveLogCustomerAsync(session));

        return Json(new { success = true });
    }

    [NonAction]
    public Task<IActionResult> SubmitAnswer(string token, string answer)
    {
        return SubmitAnswer(new SubmitInterviewAnswerRequest
        {
            Token = token,
            Answer = answer
        });
    }

    [HttpPost]
    public async Task<IActionResult> SubmitAnswer(SubmitInterviewAnswerRequest request)
    {
        var token = request?.Token;
        _logger?.LogInformation("SubmitAnswer called with session token {Token}", MaskToken(token));

        var session = string.IsNullOrWhiteSpace(token)
            ? null
            : await _interviewSessionService.GetSessionByTokenAsync(token);
        if (session?.CompletedOnUtc.HasValue == true)
        {
            await LogRuntimeIssueAsync("AI Interview token validation failure", $"SubmitAnswer rejected completed session for token {MaskToken(token)}. ReasonCode=session-completed.", await ResolveLogCustomerAsync(session));
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");
        }

        if (session == null || (!IsSessionUsable(session) && !InterviewRuntimeService.IsCompletionPending(session)))
        {
            await LogRuntimeIssueAsync("AI Interview token validation failure", $"SubmitAnswer rejected invalid session for token {MaskToken(token)}. ReasonCode={await ResolveRuntimeTokenFailureReasonCodeAsync(token)}.", await ResolveLogCustomerAsync());
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");
        }

        if (_interviewRuntimeService != null)
        {
            var runtimeResponse = await _interviewRuntimeService.SubmitAnswerAsync(request);
            var reportUrl = runtimeResponse?.ReportReady == true && session.CompletedOnUtc.HasValue && !string.IsNullOrWhiteSpace(session.ReportData)
                ? GetMockReportUrl(session.Id)
                : string.Empty;
            if (runtimeResponse != null && !runtimeResponse.Success)
            {
                _logger?.LogWarning("SubmitAnswer failed for session {SessionId}, customer {CustomerId}, product {ProductId}: {Message}",
                    session.Id, session.CustomerId, session.ProductId, runtimeResponse.Message);
            }

            return Json(new
            {
                runtimeResponse.Success,
                runtimeResponse.IsTerminated,
                ReportUrl = reportUrl,
                runtimeResponse.Question,
                runtimeResponse.Turn,
                runtimeResponse.Turns,
                runtimeResponse.Interrupted,
                runtimeResponse.Message,
                runtimeResponse.ReportReady,
                runtimeResponse.ReportGenerationInProgress,
                runtimeResponse.ReportGenerationFailed,
                runtimeResponse.EstimatedWaitSeconds
            });
        }

        if (string.IsNullOrEmpty(request?.Answer))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidAnswer", "Answer cannot be empty.");

        // Mock answer processing
        return Json(new { success = true, nextQuestion = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.NextQuestionMock") });
    }

    [HttpPost]
    public async Task<IActionResult> CompletionStatus(string token)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null || !await _customerService.IsRegisteredAsync(customer))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unauthorized", "Unauthorized runtime request.", 401);

        var session = string.IsNullOrWhiteSpace(token)
            ? null
            : await _interviewSessionService.GetSessionByTokenAsync(token);
        if (session == null || session.CustomerId != customer.Id || !string.Equals(session.Token, token, StringComparison.Ordinal))
        {
            await LogRuntimeIssueAsync("AI Interview token validation failure", $"CompletionStatus rejected invalid session for token {MaskToken(token)}. ReasonCode={await ResolveRuntimeTokenFailureReasonCodeAsync(token, session)}.", await ResolveLogCustomerAsync(session, customer));
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");
        }

        var reportReady = InterviewRuntimeService.IsCompletionReady(session);
        if (reportReady)
        {
            var reportUrl = GetMockReportUrl(session.Id);
            return Json(new CompletionStatusResponseModel
            {
                Success = true,
                Message = "Your report & video recording link have been sent to your email and saved in My Profile -> My Activity -> AI Interviews.",
                ReportReady = !string.IsNullOrWhiteSpace(reportUrl),
                ReportGenerationInProgress = false,
                ReportGenerationFailed = string.IsNullOrWhiteSpace(reportUrl),
                ReportUrl = reportUrl,
                EstimatedWaitSeconds = 0
            });
        }

        if (string.Equals(session.CompletionState, InterviewCompletionStates.Failed, StringComparison.Ordinal))
        {
            return Json(new CompletionStatusResponseModel
            {
                Success = true,
                Message = string.IsNullOrWhiteSpace(session.CompletionFailureMessage)
                    ? "Report generation failed. Please contact support if the report is not available from your interview history."
                    : session.CompletionFailureMessage,
                ReportReady = false,
                ReportGenerationInProgress = false,
                ReportGenerationFailed = true,
                ReportUrl = string.Empty,
                EstimatedWaitSeconds = 0
            });
        }

        if (InterviewRuntimeService.IsCompletionPending(session))
        {
            return Json(new CompletionStatusResponseModel
            {
                Success = true,
                Message = "Generating your report...",
                ReportReady = false,
                ReportGenerationInProgress = true,
                ReportGenerationFailed = false,
                ReportUrl = string.Empty,
                EstimatedWaitSeconds = 120
            });
        }

        return Json(new CompletionStatusResponseModel
        {
            Success = true,
            Message = "Report generation has not started.",
            ReportReady = false,
            ReportGenerationInProgress = false,
            ReportGenerationFailed = false,
            ReportUrl = string.Empty,
            EstimatedWaitSeconds = 0
        });
    }

    [HttpPost]
    public async Task<IActionResult> Stop(string token)
    {
        _logger?.LogInformation("Stop called with session token {Token}", MaskToken(token));

        var session = await ValidateActiveRuntimeTokenAsync(token);
        if (session == null)
        {
            await LogRuntimeIssueAsync("AI Interview token validation failure", $"Stop rejected invalid session for token {MaskToken(token)}. ReasonCode={await ResolveRuntimeTokenFailureReasonCodeAsync(token)}.", await ResolveLogCustomerAsync());
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");
        }

        if (_interviewRuntimeService != null)
        {
            var response = await _interviewRuntimeService.CompleteInterviewAsync(token, "Stopped by user");
            session = await _interviewSessionService.GetSessionByTokenAsync(token) ?? session;
            var reportUrl = response?.ReportReady == true && session.CompletedOnUtc.HasValue && !string.IsNullOrWhiteSpace(session.ReportData)
                ? GetMockReportUrl(session.Id)
                : string.Empty;
            if (response != null && !response.Success)
            {
                _logger?.LogWarning("Stop failed for session {SessionId}, customer {CustomerId}, product {ProductId}: {Message}",
                    session.Id, session.CustomerId, session.ProductId, response.Message);
            }

            return Json(new
            {
                response.Success,
                response.IsTerminated,
                response.Message,
                ReportUrl = reportUrl,
                response.ReportReady,
                response.ReportGenerationInProgress,
                response.ReportGenerationFailed,
                response.EstimatedWaitSeconds,
                response.Turns
            });
        }

        session.IsActive = false;
        session.CompletedOnUtc = DateTime.UtcNow;

        // Mock question scores
        var questionScores = new[] { 80, 90, 85 };
        session.QuestionScores = System.Text.Json.JsonSerializer.Serialize(questionScores);
        session.Score = (decimal)questionScores.Average();

        session.ReportData = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Runtime.ReportContentMock");
        await _interviewSessionService.UpdateInterviewSessionAsync(session);

        await PublishCompletionAsync(session);

        return Json(new { success = true, score = session.Score });
    }

    [HttpPost]
    public async Task<IActionResult> Feedback(string token, string issue, string helpfulness, string comment, IFormFile attachment)
    {
        var session = await ValidateActiveRuntimeTokenAsync(token);
        if (session == null)
        {
            await LogRuntimeIssueAsync("AI Interview feedback submission failure", $"Feedback rejected invalid session for token {MaskToken(token)}.", await ResolveLogCustomerAsync());
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");
        }

        var normalizedIssue = NormalizeFeedbackIssue(issue);
        if (string.IsNullOrWhiteSpace(normalizedIssue))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Feedback.InvalidIssue", "Select a valid issue.");

        var normalizedHelpfulness = NormalizeFeedbackHelpfulness(helpfulness);
        if (normalizedHelpfulness == null && !string.IsNullOrWhiteSpace(helpfulness))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Feedback.InvalidHelpfulness", "Select a valid helpfulness option.");

        var normalizedComment = NormalizeFeedbackComment(comment);
        var isOtherIssue = string.Equals(normalizedIssue, FeedbackIssueOther, StringComparison.Ordinal);
        var hasAttachment = attachment?.Length > 0;

        if (!isOtherIssue && hasAttachment)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Feedback.AttachmentOnlyOther", "Attachments are only available for Other issue reports.");

        if (isOtherIssue && string.IsNullOrWhiteSpace(normalizedComment))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Feedback.CommentRequired", "Please describe your issue before submitting.");

        var attachmentDownloadId = 0;
        if (isOtherIssue && hasAttachment)
        {
            var attachmentValidationMessage = ValidateFeedbackAttachment(attachment);
            if (!string.IsNullOrWhiteSpace(attachmentValidationMessage))
                return Json(new { success = false, message = attachmentValidationMessage, error = attachmentValidationMessage });

            if (_downloadService == null)
                return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Feedback.UploadUnavailable", "Attachment upload is unavailable.");

            var download = await StoreFeedbackAttachmentAsync(attachment);
            attachmentDownloadId = download.Id;
        }

        session.CandidateFeedbackIssue = normalizedIssue;
        session.CandidateFeedbackHelpfulness = normalizedHelpfulness;
        session.CandidateFeedbackComment = normalizedComment;
        session.CandidateFeedbackAttachmentDownloadId = attachmentDownloadId;
        session.CandidateFeedbackSubmittedOnUtc = DateTime.UtcNow;
        await _interviewSessionService.UpdateInterviewSessionAsync(session);

        try
        {
            await _interviewSessionService.SendRuntimeFeedbackSubmittedAdminNotificationAsync(session, 0);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to send runtime feedback admin notification for interview session {SessionId}.", session.Id);
        }

        await LogRuntimeActivityAsync(
            session,
            "AIInterview.Runtime.FeedbackSubmitted",
            BuildRuntimeActivityComment(session, $"Feedback submitted. Issue={normalizedIssue}; Helpfulness={normalizedHelpfulness ?? string.Empty}; AttachmentDownloadId={attachmentDownloadId}."));

        return Json(new { success = true, message = await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Runtime.Feedback.Success", "Thanks for the report. Your feedback has been submitted.") });
    }

    protected static string NormalizeFeedbackIssue(string issue)
    {
        var normalized = (issue ?? string.Empty).Trim();
        return FeedbackIssues.FirstOrDefault(allowed => string.Equals(allowed, normalized, StringComparison.Ordinal));
    }

    protected static string NormalizeFeedbackHelpfulness(string helpfulness)
    {
        var normalized = (helpfulness ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return FeedbackHelpfulnessValues.Contains(normalized) ? normalized : null;
    }

    protected static string NormalizeFeedbackComment(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return string.Empty;

        var normalized = string.Join(" ", comment.Replace('\r', ' ').Replace('\n', ' ').Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= MaxFeedbackCommentLength ? normalized : normalized[..MaxFeedbackCommentLength];
    }

    protected static string ValidateFeedbackAttachment(IFormFile attachment)
    {
        if (attachment == null || attachment.Length <= 0)
            return string.Empty;

        var extension = Path.GetExtension(attachment.FileName ?? string.Empty);
        if (!FeedbackAttachmentExtensions.Contains(extension))
            return "Allowed attachment types: PNG, JPG, PDF, DOC, DOCX, TXT.";

        if (attachment.Length > MaxFeedbackAttachmentBytes)
            return "Attachment size must be 5 MB or smaller.";

        return string.Empty;
    }

    protected virtual async Task<Download> StoreFeedbackAttachmentAsync(IFormFile attachment)
    {
        var download = new Download
        {
            DownloadGuid = Guid.NewGuid(),
            UseDownloadUrl = false,
            DownloadBinary = await _downloadService.GetDownloadBitsAsync(attachment),
            ContentType = string.IsNullOrWhiteSpace(attachment.ContentType) ? GetFeedbackAttachmentContentType(attachment.FileName) : attachment.ContentType,
            Filename = attachment.FileName,
            Extension = Path.GetExtension(attachment.FileName),
            IsNew = true
        };

        await _downloadService.InsertDownloadAsync(download);
        return download;
    }

    protected static string GetFeedbackAttachmentContentType(string fileName)
    {
        return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    protected async Task PublishCompletionAsync(InterviewSession session)
    {
        var languageId = (await _workContext.GetWorkingLanguageAsync()).Id;
        if (_eventPublisher != null)
            await _eventPublisher.PublishAsync(new MockAiInterviewCompletedEvent(session, languageId));
        else
            await _interviewSessionService.SendInterviewCompletionNotificationAsync(session, languageId);
    }

    [HttpPost]
    public async Task<IActionResult> RefreshToken(string token)
    {
        var session = await ValidateActiveRuntimeTokenAsync(token);
        if (session == null)
        {
            await LogRuntimeIssueAsync("AI Interview token validation failure", $"Token refresh failed for token {MaskToken(token)}. ReasonCode={await ResolveRuntimeTokenFailureReasonCodeAsync(token)}.", await ResolveLogCustomerAsync());
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");
        }

        return Json(new { success = true, newToken = session.Token, tokenExpiryUtc = session.TokenExpiryUtc });
    }

    [HttpPost]
    public async Task<IActionResult> SpeechToken(string token)
    {
        if (_interviewRuntimeService == null)
            return SafeSpeechUnavailable();

        var session = await ValidateActiveRuntimeTokenAsync(token);
        if (session == null)
            return SafeSpeechUnavailable("Invalid or expired session token.");

        var result = await _interviewRuntimeService.GetSpeechTokenAsync(token);
        if (result == null || !result.Success)
            return SafeSpeechUnavailable(result?.Message);

        return Json(new { success = true, token = result.Token, region = result.Region, expiresInSeconds = result.ExpiresInSeconds });
    }

    [HttpPost]
    public async Task<IActionResult> SpeechUsage(SpeechSynthesisUsageRequest request)
    {
        if (_interviewRuntimeService == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.Unavailable", "Speech usage service is unavailable.");

        if (request == null || string.IsNullOrWhiteSpace(request.Token))
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        var session = await ValidateActiveRuntimeTokenAsync(request.Token);
        if (session == null)
            return await LocalizedErrorAsync("Plugins.Misc.AIInterview.Runtime.Error.InvalidToken", "Invalid or expired session token.");

        await _interviewRuntimeService.TrackSpeechSynthesisUsageAsync(request);

        return Json(new { success = true });
    }

    [HttpPost]
    [RequestSizeLimit(MaxRecordingUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRecordingUploadBytes)]
    public async Task<IActionResult> UploadRecording(string token, IFormFile recording)
    {
        if (_interviewRuntimeService == null)
            return Json(new { success = false, message = "Recording upload is unavailable." });

        var session = await _interviewSessionService.GetSessionByTokenAsync(token);

        var result = await _interviewRuntimeService.UploadRecordingAsync(token, recording);
        if (result == null || !result.Success)
        {
            await LogRuntimeIssueAsync("AI Interview recording upload failure", $"Recording upload failed for token {MaskToken(token)}.", await ResolveLogCustomerAsync(session));
            return Json(new { success = false, message = result?.Message ?? "Recording upload failed." });
        }

        return Json(new { success = true, message = result.Message, recordingUrl = result.RecordingUrl });
    }

    public async Task<IActionResult> History()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        var fallbackInterviewTitle = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Common.Interview");
        var sessions = ((await _interviewSessionService.GetSessionsByCustomerIdAsync(customer.Id)) ?? new List<InterviewSession>())
            .Where(session => string.Equals(NormalizeInterviewType(session), AIInterviewDefaults.InterviewTypeMockPractice, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var model = await Task.WhenAll((sessions ?? new List<InterviewSession>()).Select(async session =>
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
                    ? GetMockReportUrl(session.Id)
                    : null,
                InterviewReportPanelUrl = session.CompletedOnUtc.HasValue && !string.IsNullOrWhiteSpace(session.ReportData)
                    ? Url?.Action("ReportPanel", "AIInterview", new { sessionId = session.Id })
                    : null,
                RecordingUrl = session.CompletedOnUtc.HasValue && !string.IsNullOrWhiteSpace(session.RecordingUrl)
                    ? Url?.Action("Recording", "AIInterview", new { sessionId = session.Id })
                    : null,
                RecordingShareUrl = session.CompletedOnUtc.HasValue
                    ? await BuildRecordingShareUrlAsync(session)
                    : null
            };
        }));

        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/History.cshtml", model.ToList());
    }

    public async Task<IActionResult> Report(int sessionId)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Challenge();

        if (!await _interviewSessionService.CanAccessReportAsync(customer.Id, sessionId))
            return await RuntimeErrorAsync("Plugins.Misc.AIInterview.Report.AccessDenied", "You do not have access to this interview report.", 403);

        var session = await _interviewSessionService.GetInterviewSessionByIdAsync(sessionId);
        if (session == null || string.IsNullOrEmpty(session.ReportData))
            return await RuntimeErrorAsync("Plugins.Misc.AIInterview.Report.NotFound", "Interview report not found.", 404);

        var turns = new List<InterviewTurn>();
        if (_turnService != null)
        {
            try
            {
                turns = (await _turnService.GetTurnsBySessionIdAsync(session.Id))?.ToList() ?? new List<InterviewTurn>();
            }
            catch
            {
                turns = new List<InterviewTurn>();
            }
        }
        var normalizedQuestionCount = NormalizeRuntimeQuestionCount(session);
        var normalizedTurns = InterviewTurnNormalizationHelper.GetCompletedReportTurns(turns, normalizedQuestionCount).ToList();
        var parsedQuestionScores = ParseQuestionScores(session.QuestionScores);
        if (parsedQuestionScores.Count != normalizedTurns.Count(turn => turn.Score.HasValue))
            parsedQuestionScores = normalizedTurns.Where(turn => turn.Score.HasValue).Select(turn => turn.Score.Value).ToList();

        var model = new InterviewReportModel
        {
            SessionId = session.Id,
            ProductId = session.ProductId,
            SessionKey = session.SessionKey,
            Token = session.Token,
            Difficulty = session.Difficulty,
            ProductName = (await _productService.GetProductByIdAsync(session.ProductId))?.Name ?? "Interview",
            JobTitle = (await _productService.GetProductByIdAsync(session.ProductId))?.Name ?? "Interview",
            Score = session.Score,
            IsCompleted = session.CompletedOnUtc.HasValue,
            QuestionScores = session.QuestionScores,
            ParsedQuestionScores = parsedQuestionScores,
            ReportData = InterviewReportSummaryHelper.NormalizePersistedReportData(session.ReportData, normalizedTurns, session.Score),
            RecordingUrl = !string.IsNullOrWhiteSpace(session.RecordingUrl) ? Url?.Action("Recording", "AIInterview", new { sessionId = session.Id }) : null,
            RecordingShareUrl = await BuildRecordingShareUrlAsync(session),
            Turns = normalizedTurns.Select(turn => new InterviewTurnViewModel
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
            }).ToList(),
            CreatedOnUtc = session.CreatedOnUtc,
            ReportDateUtc = session.CompletedOnUtc ?? session.StartedOnUtc ?? session.CreatedOnUtc,
            CompletedOnUtc = session.CompletedOnUtc
        };

        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/Report.cshtml", model);
    }

    protected static IList<decimal> ParseQuestionScores(string questionScores)
    {
        if (string.IsNullOrWhiteSpace(questionScores))
            return new List<decimal>();

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<decimal>>(questionScores);
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

    protected async Task<bool> IsAuthorizedAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        return customer != null && (await _customerService.IsAdminAsync(customer) || customer.VendorId > 0);
    }

    public async Task<IActionResult> EmployerManage()
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var invites = await _inviteService.GetSponsorInvitesAsync(customer.Id);
        var wallet = await _creditService.GetOrCreateWalletAsync(customer.Id);
        var inviteStatuses = new Dictionary<int, string>();
        foreach (var invite in invites)
            inviteStatuses[invite.Id] = await GetInviteStatusTextAsync(invite);

        ViewBag.AvailableProducts = await BuildEmployerInviteProductSelectListAsync(customer);

        ViewBag.CreditBalance = wallet.Balance;
        ViewBag.CreditBalanceDisplay = decimal.Truncate(wallet.Balance).ToString("0", CultureInfo.InvariantCulture);
        ViewBag.SponsorInviteStatuses = inviteStatuses;

        return View("~/Plugins/Misc.AIInterview/Views/MockAiInterview/EmployerManage.cshtml", invites);
    }

    [HttpGet]
    public async Task<IActionResult> CreateInvitePage()
    {
        if (!await IsAuthorizedAsync())
            return Redirect(BuildCreateInviteLoginRedirectUrl());

        return await EmployerManage();
    }

    [HttpPost]
    public async Task<IActionResult> CreateInvite(string email, int productId, int maxAttempts, DateTime? expiryDateUtc)
    {
        if (!await IsAuthorizedAsync())
            return Redirect(BuildCreateInviteLoginRedirectUrl());

        if (string.IsNullOrWhiteSpace(email))
            return InviteMutationResult(false, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.EmailRequired"));

        if (maxAttempts <= 0)
            return InviteMutationResult(false, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidAttempts"));

        expiryDateUtc = NormalizeInviteExpiryDateUtc(expiryDateUtc);

        if (expiryDateUtc.HasValue && expiryDateUtc.Value <= DateTime.UtcNow)
            return InviteMutationResult(false, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidExpiry"));

        try
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null)
                return InviteMutationResult(false, await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Invite.ProductNotFound", "Product not found."));

            if (customer?.VendorId > 0 && product.VendorId != customer.VendorId)
                return InviteMutationResult(false, await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Invite.ProductNotFound", "Product not found."));

            if (_jobProductAccessService != null && !await _jobProductAccessService.CanAcceptJobApplicationsAsync(product))
                return InviteMutationResult(false, await GetLocalizedTextAsync("Common.NotAvailable", "The requested job is not available."));

            var emails = email.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(e => e.Trim())
                              .Where(e => !string.IsNullOrEmpty(e))
                              .Distinct()
                              .ToList();

            int createdCount = 0;
            int invalidCount = 0;
            var failureMessages = new List<string>();

            foreach (var e in emails)
            {
                if (!CommonHelper.IsValidEmail(e))
                {
                    invalidCount++;
                    continue;
                }

                try
                {
                    await _inviteService.CreateInviteAsync(customer.Id, e, productId, maxAttempts, expiryDateUtc);
                    createdCount++;
                }
                catch (NopException ex)
                {
                    failureMessages.Add(ex.Message);
                }
                catch
                {
                    failureMessages.Add(await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Employer.Invite.Error", "Error creating invite."));
                }
            }

            if (createdCount == 0)
            {
                if (failureMessages.Count == 1)
                    return InviteMutationResult(false, failureMessages[0]);

                if (invalidCount > 0 && failureMessages.Count == 0)
                    return InviteMutationResult(false,
                        await GetLocalizedTextAsync("Plugins.Misc.AIInterview.Admin.Invite.EmailInvalid", "Enter a valid email address."));

                var noInvitesMessage = failureMessages.FirstOrDefault()
                    ?? $"No invites were created. {invalidCount} email(s) were invalid.";
                return InviteMutationResult(false, noInvitesMessage);
            }

            var bulkSuccessMessageFormat = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.BulkSuccess");
            var message = string.Format(bulkSuccessMessageFormat ?? "Successfully created {0} invites. {1} emails were invalid.", createdCount, invalidCount);
            if (failureMessages.Count > 0)
                message = $"{message} {failureMessages.Count} invite(s) failed: {string.Join("; ", failureMessages.Distinct())}";

            return InviteMutationResult(true, message);
        }
        catch (NopException ex)
        {
            return InviteMutationResult(false, ex.Message);
        }
        catch (Exception)
        {
            return InviteMutationResult(false, await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.Error"));
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeactivateInvite(int id)
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        await _inviteService.DeactivateInviteAsync(id, customer.Id);
        return Json(new { success = true, message = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.Deactivated") });
    }

    protected virtual DateTime? NormalizeInviteExpiryDateUtc(DateTime? expiryDateUtc)
    {
        if (!expiryDateUtc.HasValue)
            return null;

        return DateTime.SpecifyKind(expiryDateUtc.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
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

        return await GetLocalizedTextAsync(statusKey, statusKey);
    }

    protected virtual async Task<IList<SelectListItem>> BuildEmployerInviteProductSelectListAsync(Nop.Core.Domain.Customers.Customer customer)
    {
        var products = await _productService.SearchProductsAsync(pageSize: int.MaxValue, showHidden: true)
            ?? new Nop.Core.PagedList<Product>(new List<Product>(), 0, 1, 1);

        var filteredProducts = products.AsEnumerable();
        if (customer?.VendorId > 0)
            filteredProducts = filteredProducts.Where(product => product.VendorId == customer.VendorId);

        var items = new List<SelectListItem>
        {
            new()
            {
                Value = string.Empty,
                Text = await GetLocalizedTextAsync("Plugins.Misc.AIInterview.VendorJobCreation.Select", "Select")
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
}
