using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Cms;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Logging;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Misc.AIInterview;

/// <summary>
/// Represents AI Interview plugin
/// </summary>
public class AIInterviewPlugin : BasePlugin, IMiscPlugin, IWidgetPlugin
{
    public static string FinalCompletionSpeechResourceKey => $"{AIInterviewDefaults.LocalizationPrefix}.Runtime.FinalCompletionSpeech";

    public const string DefaultFinalCompletionSpeech = "Thank you for completing your interview. Your responses have been submitted successfully. We are now preparing your interview report. Best wishes.";

    #region Fields

    private readonly ILocalizationService _localizationService;
    private readonly ICustomerService _customerService;
    private readonly ISettingService _settingService;
    private readonly IWebHelper _webHelper;
    private readonly IMessageTemplateService _messageTemplateService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly ICategoryTemplateService _categoryTemplateService;
    private readonly IRepository<ActivityLogType> _activityLogTypeRepository;
    private readonly IRepository<CustomerCustomerRoleMapping> _customerCustomerRoleMappingRepository;
    private readonly WidgetSettings _widgetSettings;
    private readonly IScheduleTaskService _scheduleTaskService;

    #endregion

    #region Ctor

    public AIInterviewPlugin(ILocalizationService localizationService,
        ISettingService settingService,
        IWebHelper webHelper,
        IMessageTemplateService messageTemplateService,
        ICustomerService customerService = null,
        IProductTemplateService productTemplateService = null,
        ICategoryTemplateService categoryTemplateService = null,
        WidgetSettings widgetSettings = null,
        IRepository<ActivityLogType> activityLogTypeRepository = null,
        IScheduleTaskService scheduleTaskService = null,
        IRepository<CustomerCustomerRoleMapping> customerCustomerRoleMappingRepository = null)
    {
        _localizationService = localizationService;
        _customerService = customerService;
        _settingService = settingService;
        _webHelper = webHelper;
        _messageTemplateService = messageTemplateService;
        _productTemplateService = productTemplateService;
        _categoryTemplateService = categoryTemplateService;
        _activityLogTypeRepository = activityLogTypeRepository;
        _customerCustomerRoleMappingRepository = customerCustomerRoleMappingRepository;
        _widgetSettings = widgetSettings;
        _scheduleTaskService = scheduleTaskService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a value indicating whether to hide this plugin on the widget list page in the admin area
    /// </summary>
    public bool HideInWidgetList => false;

    /// <summary>
    /// Gets widget zones where this widget should be rendered
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the widget zones
    /// </returns>
    public Task<IList<string>> GetWidgetZonesAsync()
    {
        return Task.FromResult<IList<string>>(new List<string>
        {
            AIInterviewDefaults.HomepageTopPerformersWidgetZone,
            "productdetails_before_collateral",
            AdminWidgetZones.ProductDetailsBlock,
            "body_start_html_tag_after",
            "header_links_after"
        });
    }

    /// <summary>
    /// Gets a type of a view component for displaying widget
    /// </summary>
    /// <param name="widgetZone">Name of the widget zone</param>
    /// <returns>View component type</returns>
    public Type GetWidgetViewComponent(string widgetZone)
    {
        if (string.Equals(widgetZone, AdminWidgetZones.ProductDetailsBlock, StringComparison.OrdinalIgnoreCase))
            return typeof(Components.AIInterviewAdminProductRequirementsViewComponent);

        if (string.Equals(widgetZone, AIInterviewDefaults.HomepageTopPerformersWidgetZone, StringComparison.OrdinalIgnoreCase))
            return typeof(Components.AIInterviewHomepageTopPerformersViewComponent);

        if (string.Equals(widgetZone, "body_start_html_tag_after",
            StringComparison.OrdinalIgnoreCase))
            return typeof(Components.AIInterviewInstituteRedirectViewComponent);

        if (string.Equals(widgetZone, "header_links_after", StringComparison.OrdinalIgnoreCase))
            return typeof(Components.VendorPortalHeaderLinksViewComponent);

        return typeof(Components.AIInterviewProductDetailsViewComponent);
    }

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}Admin/AIInterview/Configure";
    }

    protected static IReadOnlyDictionary<string, string> RuntimeActivityLogTypes { get; } = new Dictionary<string, string>
    {
        ["AIInterview.Runtime.GuidelinesAcknowledged"] = "AI Interview runtime guidelines acknowledged",
        ["AIInterview.Runtime.InterviewStarted"] = "AI Interview runtime interview started",
        ["AIInterview.Runtime.AnswerSubmitted"] = "AI Interview runtime answer submitted",
        ["AIInterview.Runtime.InterviewCompleted"] = "AI Interview runtime interview completed",
        ["AIInterview.Runtime.RecordingUploaded"] = "AI Interview runtime recording uploaded",
        ["AIInterview.Runtime.SpeechUnavailable"] = "AI Interview runtime speech unavailable",
        ["AIInterview.Runtime.AnswerSubmitFailed"] = "AI Interview runtime answer submit failed",
        ["AIInterview.Runtime.NetworkRequestFailed"] = "AI Interview runtime network request failed",
        ["AIInterview.Runtime.FeedbackSubmitted"] = "AI Interview runtime feedback submitted",
        ["AIInterview.Runtime.RecordingUploadFailed"] = "AI Interview runtime recording upload failed",
        ["AIInterview.Runtime.CompletionFinalizationFailed"] = "AI Interview runtime completion finalization failed"
    };

    protected virtual async Task EnsureRuntimeActivityLogTypesAsync()
    {
        if (_activityLogTypeRepository == null)
            return;

        var systemKeywords = RuntimeActivityLogTypes.Keys.ToArray();
        var existingTypes = await _activityLogTypeRepository.GetAllAsync(query => query
            .Where(type => systemKeywords.Contains(type.SystemKeyword)));
        var existingByKeyword = existingTypes.ToDictionary(type => type.SystemKeyword, StringComparer.OrdinalIgnoreCase);

        foreach (var (systemKeyword, name) in RuntimeActivityLogTypes)
        {
            if (existingByKeyword.TryGetValue(systemKeyword, out var existingType))
            {
                var changed = false;
                if (!existingType.Enabled)
                {
                    existingType.Enabled = true;
                    changed = true;
                }

                if (!string.Equals(existingType.Name, name, StringComparison.Ordinal))
                {
                    existingType.Name = name;
                    changed = true;
                }

                if (changed)
                    await _activityLogTypeRepository.UpdateAsync(existingType);

                continue;
            }

            await _activityLogTypeRepository.InsertAsync(new ActivityLogType
            {
                SystemKeyword = systemKeyword,
                Name = name,
                Enabled = true
            });
        }
    }

    protected virtual async Task DeleteRuntimeActivityLogTypesAsync()
    {
        if (_activityLogTypeRepository == null)
            return;

        var systemKeywords = RuntimeActivityLogTypes.Keys.ToArray();
        var existingTypes = await _activityLogTypeRepository.GetAllAsync(query => query
            .Where(type => systemKeywords.Contains(type.SystemKeyword)));
        if (existingTypes.Any())
            await _activityLogTypeRepository.DeleteAsync(existingTypes);
    }

    private async Task EnsureEmployerRoleAsync()
    {
        await EnsureCustomerRoleAsync(AIInterviewDefaults.EmployerCustomerRoleSystemName);
    }

    private async Task EnsureInstituteRoleAsync()
    {
        await EnsureCustomerRoleAsync(AIInterviewDefaults.InstituteCustomerRoleSystemName);
    }

    private async Task EnsureCustomerRoleAsync(string roleSystemName)
    {
        if (_customerService == null)
            return;

        var allRoles = await _customerService.GetAllCustomerRolesAsync(showHidden: true) ?? new List<CustomerRole>();
        var canonicalRole = await _customerService.GetCustomerRoleBySystemNameAsync(roleSystemName)
            ?? allRoles.FirstOrDefault(role => string.Equals(role.SystemName?.Trim(), roleSystemName, StringComparison.OrdinalIgnoreCase))
            ?? allRoles.FirstOrDefault(role => string.Equals(role.Name?.Trim(), roleSystemName, StringComparison.OrdinalIgnoreCase));

        if (canonicalRole == null)
        {
            canonicalRole = new CustomerRole
            {
                Name = roleSystemName,
                SystemName = roleSystemName,
                Active = true,
                IsSystemRole = false
            };
            await _customerService.InsertCustomerRoleAsync(canonicalRole);
        }
        else if (!canonicalRole.Active
            || !string.Equals(canonicalRole.Name, roleSystemName, StringComparison.Ordinal)
            || !string.Equals(canonicalRole.SystemName, roleSystemName, StringComparison.Ordinal))
        {
            canonicalRole.Active = true;
            canonicalRole.Name = roleSystemName;
            canonicalRole.SystemName = roleSystemName;
            await _customerService.UpdateCustomerRoleAsync(canonicalRole);
        }

        var duplicateRoles = allRoles
            .Where(role => role.Id != canonicalRole.Id
                && (string.Equals(role.SystemName?.Trim(), roleSystemName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(role.Name?.Trim(), roleSystemName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var duplicateRole in duplicateRoles)
            await MigrateCustomerRoleMappingsAsync(canonicalRole, duplicateRole);
    }

    private async Task MigrateCustomerRoleMappingsAsync(CustomerRole canonicalRole, CustomerRole duplicateRole)
    {
        if (_customerCustomerRoleMappingRepository == null)
            return;

        var mappings = await _customerCustomerRoleMappingRepository.GetAllAsync(query => query.Where(mapping =>
            mapping.CustomerRoleId == canonicalRole.Id || mapping.CustomerRoleId == duplicateRole.Id));
        var canonicalCustomerIds = mappings
            .Where(mapping => mapping.CustomerRoleId == canonicalRole.Id)
            .Select(mapping => mapping.CustomerId)
            .ToHashSet();

        foreach (var duplicateMapping in mappings.Where(mapping => mapping.CustomerRoleId == duplicateRole.Id))
        {
            if (!canonicalCustomerIds.Add(duplicateMapping.CustomerId))
            {
                await _customerCustomerRoleMappingRepository.DeleteAsync(duplicateMapping);
                continue;
            }

            duplicateMapping.CustomerRoleId = canonicalRole.Id;
            await _customerCustomerRoleMappingRepository.UpdateAsync(duplicateMapping);
        }

        if (!duplicateRole.IsSystemRole)
        {
            await _customerService.DeleteCustomerRoleAsync(duplicateRole);
            return;
        }

        if (duplicateRole.Active)
        {
            duplicateRole.Active = false;
            await _customerService.UpdateCustomerRoleAsync(duplicateRole);
        }
    }

    /// <summary>
    /// Install plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>


    /// <summary>
    /// Update plugin
    /// </summary>
    /// <param name="currentVersion">Current version of the plugin</param>
    /// <param name="targetVersion">Target version of the plugin</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        var settings = await _settingService.LoadSettingAsync<AIInterviewSettings>();

        // NopCommerce setting service loads default values from the type if they haven't been saved yet.
        // But for booleans default is false. If it's false, and we want it to be true by default on upgrade...
        // Actually, NopCommerce settings are stored as Key/Value pairs. We can check if the key exists to see if it was explicitly set.
        var enabledSetting = await _settingService.GetSettingAsync("aiinterviewsettings.enabled");
        if (enabledSetting == null)
            settings.Enabled = true;

        if (await _settingService.GetSettingAsync("aiinterviewsettings.trackazureopenaiusage") == null)
            settings.TrackAzureOpenAiUsage = true;

        if (await _settingService.GetSettingAsync("aiinterviewsettings.trackazurespeechusage") == null)
            settings.TrackAzureSpeechUsage = true;

        if (await _settingService.GetSettingAsync("aiinterviewsettings.calculateazurecostperinterview") == null)
            settings.CalculateAzureCostPerInterview = true;

        if (await _settingService.GetSettingAsync("aiinterviewsettings.enablefinalscoringatcompletion") == null)
            settings.EnableFinalScoringAtCompletion = true;

        if (string.IsNullOrWhiteSpace(settings.CreditProductSkuMappingsJson))
            settings.CreditProductSkuMappingsJson = AIInterviewDefaults.DefaultCreditProductSkuMappingsJson;

        if (string.IsNullOrWhiteSpace(settings.CreditPurchasePageUrl))
            settings.CreditPurchasePageUrl = AIInterviewDefaults.DefaultCreditPurchasePageUrl;

        if (string.IsNullOrWhiteSpace(settings.AzureUsageCurrencyCode))
            settings.AzureUsageCurrencyCode = "USD";

        if (string.IsNullOrWhiteSpace(settings.AzureDocumentIntelligenceModelId))
            settings.AzureDocumentIntelligenceModelId = AIInterviewDefaults.DefaultAzureDocumentIntelligenceModelId;

        if (settings.AzureDocumentIntelligenceTimeoutSeconds <= 0)
            settings.AzureDocumentIntelligenceTimeoutSeconds = AIInterviewDefaults.DefaultAzureDocumentIntelligenceTimeoutSeconds;

        if (string.IsNullOrWhiteSpace(settings.SupportPhoneNumber))
            settings.SupportPhoneNumber = AIInterviewDefaults.DefaultSupportPhoneNumber;

        settings.StrengthsSummaryMaxCompletionTokens = NormalizeStrengthsSummaryMaxCompletionTokens(settings.StrengthsSummaryMaxCompletionTokens);
        settings.QuestionPlanMaxCompletionTokens = NormalizeQuestionPlanMaxCompletionTokens(settings.QuestionPlanMaxCompletionTokens);
        settings.QuestionPlanRetryMaxCompletionTokens = NormalizeQuestionPlanRetryMaxCompletionTokens(settings.QuestionPlanRetryMaxCompletionTokens);
        settings.RecordingUploadMaxMb = NormalizeRecordingUploadMaxMb(settings.RecordingUploadMaxMb);
        settings.RecordingVideoBitsPerSecond = NormalizeRecordingVideoBitsPerSecond(settings.RecordingVideoBitsPerSecond);
        settings.RecordingAudioBitsPerSecond = NormalizeRecordingAudioBitsPerSecond(settings.RecordingAudioBitsPerSecond);
        settings.RecordingSourceMode = NormalizeRecordingSourceMode(settings.RecordingSourceMode);
        settings.RecordingUploadTimeoutMs = NormalizeRecordingUploadTimeoutMs(settings.RecordingUploadTimeoutMs);
        settings.FinalizationWaitTimeoutMs = NormalizeFinalizationWaitTimeoutMs(settings.FinalizationWaitTimeoutMs, settings.RecordingUploadTimeoutMs);

        await _settingService.SaveSettingAsync(settings);
        await EnsureJobProductTemplateAsync();
        await EnsureFixedQuestionProductTemplateAsync();
        await EnsureMockPracticeProductTemplateAsync();
        await EnsurePricingCategoryTemplateAsync();
        await EnsureWidgetActiveAsync();
        await EnsureMessageTemplatesAsync();
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetEmployerApplicationsLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetUpgradeLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetAdminLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetMyActivityCreditLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetRuntimeTourLocaleResources());
        await EnsureRuntimeActivityLogTypesAsync();
        await EnsureEmployerRoleAsync();
        await EnsureInstituteRoleAsync();

        await base.UpdateAsync(currentVersion, targetVersion);
    }

    protected async Task EnsureMessageTemplatesAsync()
    {
        var applicantTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.ApplicantInterviewCompletion", 0);
        if (!(applicantTemplates?.Any() ?? false))
        {
            await _messageTemplateService.InsertMessageTemplateAsync(new Nop.Core.Domain.Messages.MessageTemplate
            {
                Name = "AIInterview.ApplicantInterviewCompletion",
                Subject = "Interview Completion: %AIInterview.JobTitle%",
                Body = "<p>Hello %Customer.FullName%,</p><p>You have completed the interview for %AIInterview.JobTitle% on %AIInterview.CompletionDate%.</p><p>Overall Score: %AIInterview.OverallScore%</p><p>Question-level Summary: %AIInterview.QuestionSummary%</p><p><a href=\"%AIInterview.ReportUrl%\">View Full Report</a></p><p><a href=\"%AIInterview.MyApplicationsUrl%\">View My Applications</a></p>",
                IsActive = true
            });
        }

        var vendorTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.VendorInterviewCompletion", 0);
        if (!(vendorTemplates?.Any() ?? false))
        {
            await _messageTemplateService.InsertMessageTemplateAsync(new Nop.Core.Domain.Messages.MessageTemplate
            {
                Name = "AIInterview.VendorInterviewCompletion",
                Subject = "Candidate Interview Completion: %AIInterview.JobTitle%",
                Body = "<p>Hello %Vendor.Name%,</p><p>Candidate %Customer.FullName% (%Customer.Email%) has completed the interview for %AIInterview.JobTitle% on %AIInterview.CompletionDate%.</p><p>Overall Score: %AIInterview.OverallScore%</p><p>Question-level Summary: %AIInterview.QuestionSummary%</p><p><a href=\"%AIInterview.CandidateReportUrl%\">View Candidate Report</a></p>",
                IsActive = true
            });
        }

        var statusTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.ApplicationStatusUpdate", 0);
        if (!(statusTemplates?.Any() ?? false))
        {
            await _messageTemplateService.InsertMessageTemplateAsync(new Nop.Core.Domain.Messages.MessageTemplate
            {
                Name = "AIInterview.ApplicationStatusUpdate",
                Subject = "Application Status Update: %AIInterview.JobTitle%",
                Body = "<p>Hello %Customer.FullName%,</p><p>The status of your application for %AIInterview.JobTitle% has been updated to %AIInterview.NewStatus%.</p><p>Updated on: %AIInterview.UpdateTimestamp%</p><p><a href=\"%AIInterview.MyApplicationsUrl%\">View My Applications</a></p>",
                IsActive = true
            });
        }

        var submittedTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.ApplicationSubmitted", 0);
        if (!(submittedTemplates?.Any() ?? false))
        {
            await _messageTemplateService.InsertMessageTemplateAsync(new Nop.Core.Domain.Messages.MessageTemplate
            {
                Name = "AIInterview.ApplicationSubmitted",
                Subject = "Application Submitted: %AIInterview.JobTitle%",
                Body = "<p>Hello %Customer.FullName%,</p><p>Your application for %AIInterview.JobTitle% has been successfully submitted.</p><p><a href=\"%AIInterview.MyApplicationsUrl%\">View My Applications</a></p>",
                IsActive = true
            });
        }

        var sponsorInviteTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.SponsorInviteCreated", 0);
        if (!(sponsorInviteTemplates?.Any() ?? false))
        {
            await _messageTemplateService.InsertMessageTemplateAsync(new Nop.Core.Domain.Messages.MessageTemplate
            {
                Name = "AIInterview.SponsorInviteCreated",
                Subject = "Interview Invite: %AIInterview.JobTitle%",
                Body = "<p>Hello,</p><p>You have been invited to interview for %AIInterview.JobTitle%.</p><p>Invite Code: %AIInterview.InviteCode%</p><p>Max Attempts: %AIInterview.MaxAttempts%</p><p>Expiry Date: %AIInterview.ExpiryDate%</p><p><a href=\"%AIInterview.InviteUrl%\">Start Interview</a></p>",
                IsActive = true
            });
        }

        var creditDepositedTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.CreditDeposited", 0);
        if (!(creditDepositedTemplates?.Any() ?? false))
        {
            await _messageTemplateService.InsertMessageTemplateAsync(new Nop.Core.Domain.Messages.MessageTemplate
            {
                Name = "AIInterview.CreditDeposited",
                Subject = "Credits deposited: %AIInterview.CreditsDeposited% credits",
                Body = @"<div style=""font-family:Arial,Helvetica,sans-serif;background:#f6f8fb;padding:24px;color:#1f2937;"">
  <div style=""max-width:640px;margin:0 auto;background:#ffffff;border:1px solid #e5e7eb;border-radius:8px;overflow:hidden;"">
    <div style=""padding:22px 24px;border-bottom:1px solid #e5e7eb;"">
      <h1 style=""margin:0;font-size:22px;line-height:1.3;color:#111827;"">Credits deposited</h1>
      <p style=""margin:8px 0 0;color:#4b5563;font-size:14px;"">Hello %Customer.FullName%, your AIInterview credits have been updated.</p>
    </div>
    <div style=""padding:20px 24px;"">
      <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""border:1px solid #e5e7eb;border-radius:6px;background:#fafafa;margin-bottom:14px;"">
        <tr>
          <td style=""padding:12px;border:1px solid #e5e7eb;border-radius:6px;background:#fafafa;"">
            <div style=""font-size:12px;color:#6b7280;text-transform:uppercase;"">Deposited</div>
            <div style=""font-size:24px;font-weight:700;color:#111827;"">%AIInterview.CreditsDeposited%</div>
            <div style=""font-size:13px;color:#4b5563;"">%AIInterview.DepositSource%</div>
          </td>
        </tr>
      </table>
      <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""border-collapse:collapse;margin-top:14px;"">
        <tr>
          <td style=""width:50%;padding:12px;border:1px solid #e5e7eb;background:#ffffff;"">
            <div style=""font-size:12px;color:#6b7280;text-transform:uppercase;"">Total credits</div>
            <div style=""font-size:20px;font-weight:700;color:#111827;"">%AIInterview.TotalCredits%</div>
          </td>
          <td style=""width:50%;padding:12px;border:1px solid #e5e7eb;background:#ffffff;"">
            <div style=""font-size:12px;color:#6b7280;text-transform:uppercase;"">Withdrawn credits</div>
            <div style=""font-size:20px;font-weight:700;color:#111827;"">%AIInterview.WithdrawnCredits%</div>
          </td>
        </tr>
      </table>
      <p style=""margin:20px 0 0;""><a href=""%AIInterview.CreditPageUrl%"" style=""display:inline-block;background:#111827;color:#ffffff;text-decoration:none;padding:10px 16px;border-radius:6px;font-size:14px;"">View credits</a></p>
    </div>
  </div>
</div>",
                IsActive = true
            });
        }

        var startRefundTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync(Services.InterviewStartCreditService.RefundNotificationTemplateName, 0);
        if (!(startRefundTemplates?.Any() ?? false))
        {
            await _messageTemplateService.InsertMessageTemplateAsync(new Nop.Core.Domain.Messages.MessageTemplate
            {
                Name = Services.InterviewStartCreditService.RefundNotificationTemplateName,
                Subject = "Interview credit refunded for session %AIInterview.SessionId%",
                Body = @"<div style=""font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.5;"">
  <h1 style=""font-size:22px;color:#111827;"">Interview credit refunded</h1>
  <p>Your interview could not start, so the credit charge was reversed.</p>
  <table role=""presentation"" cellspacing=""0"" cellpadding=""6"" style=""border-collapse:collapse;"">
    <tr><td><strong>Session ID</strong></td><td>%AIInterview.SessionId%</td></tr>
    <tr><td><strong>Interview</strong></td><td>%AIInterview.ProductName%</td></tr>
    <tr><td><strong>Refund amount</strong></td><td>%AIInterview.RefundAmount%</td></tr>
    <tr><td><strong>Reason</strong></td><td>%AIInterview.RefundReason%</td></tr>
    <tr><td><strong>Occurred (UTC)</strong></td><td>%AIInterview.OccurredUtc%</td></tr>
  </table>
</div>",
                IsActive = true
            });
        }

        var runtimeFeedbackTemplates = await _messageTemplateService.GetMessageTemplatesByNameAsync("AIInterview.RuntimeFeedbackSubmitted.AdminNotification", 0);
        if (!(runtimeFeedbackTemplates?.Any() ?? false))
        {
            await _messageTemplateService.InsertMessageTemplateAsync(new Nop.Core.Domain.Messages.MessageTemplate
            {
                Name = "AIInterview.RuntimeFeedbackSubmitted.AdminNotification",
                Subject = "Runtime Feedback Submitted: %AIInterview.FeedbackIssue%",
                Body = @"<p>Runtime feedback was submitted.</p>
<table>
  <tr><td>Session ID</td><td>%AIInterview.SessionId%</td></tr>
  <tr><td>Candidate</td><td>%Customer.FullName%</td></tr>
  <tr><td>Candidate email</td><td>%Customer.Email%</td></tr>
  <tr><td>Job / interview</td><td>%AIInterview.JobTitle%</td></tr>
  <tr><td>Issue</td><td>%AIInterview.FeedbackIssue%</td></tr>
  <tr><td>Helpfulness</td><td>%AIInterview.FeedbackHelpfulness%</td></tr>
  <tr><td>Comment</td><td>%AIInterview.FeedbackComment%</td></tr>
  <tr><td>Submitted</td><td>%AIInterview.FeedbackSubmittedOn%</td></tr>
  <tr><td>Attachment uploaded</td><td>%AIInterview.FeedbackHasAttachment%</td></tr>
</table>
<p><a href=""%AIInterview.FeedbackReportsUrl%"">Open Feedback Reports</a></p>
<p><a href=""%AIInterview.CandidateDetailsUrl%"">Open Candidate Details</a></p>",
                IsActive = true
            });
        }
    }

    protected async Task EnsureJobProductTemplateAsync()
    {
        if (_productTemplateService == null)
            return;

        var templates = await _productTemplateService.GetAllProductTemplatesAsync();
        var template = templates.FirstOrDefault(item =>
            string.Equals(item.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.OrdinalIgnoreCase)) ??
            templates.FirstOrDefault(item =>
                string.Equals(item.Name, AIInterviewDefaults.JobProductTemplateName, StringComparison.OrdinalIgnoreCase));

        if (template == null)
        {
            await _productTemplateService.InsertProductTemplateAsync(new ProductTemplate
            {
                Name = AIInterviewDefaults.JobProductTemplateName,
                ViewPath = AIInterviewDefaults.JobProductTemplateViewPath,
                DisplayOrder = 20,
                IgnoredProductTypes = ((int)ProductType.GroupedProduct).ToString()
            });
            return;
        }

        var changed = false;
        if (!string.Equals(template.Name, AIInterviewDefaults.JobProductTemplateName, StringComparison.Ordinal))
        {
            template.Name = AIInterviewDefaults.JobProductTemplateName;
            changed = true;
        }

        if (!string.Equals(template.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.Ordinal))
        {
            template.ViewPath = AIInterviewDefaults.JobProductTemplateViewPath;
            changed = true;
        }

        if (changed)
            await _productTemplateService.UpdateProductTemplateAsync(template);
    }

    protected async Task EnsureMockPracticeProductTemplateAsync()
    {
        if (_productTemplateService == null)
            return;

        var templates = await _productTemplateService.GetAllProductTemplatesAsync();
        var template = templates.FirstOrDefault(item =>
            string.Equals(item.ViewPath, AIInterviewDefaults.MockPracticeProductTemplateViewPath, StringComparison.OrdinalIgnoreCase)) ??
            templates.FirstOrDefault(item =>
                string.Equals(item.Name, AIInterviewDefaults.MockPracticeProductTemplateName, StringComparison.OrdinalIgnoreCase));

        if (template == null)
        {
            await _productTemplateService.InsertProductTemplateAsync(new ProductTemplate
            {
                Name = AIInterviewDefaults.MockPracticeProductTemplateName,
                ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath,
                DisplayOrder = 21,
                IgnoredProductTypes = ((int)ProductType.GroupedProduct).ToString()
            });
            return;
        }

        var changed = false;
        if (!string.Equals(template.Name, AIInterviewDefaults.MockPracticeProductTemplateName, StringComparison.Ordinal))
        {
            template.Name = AIInterviewDefaults.MockPracticeProductTemplateName;
            changed = true;
        }

        if (!string.Equals(template.ViewPath, AIInterviewDefaults.MockPracticeProductTemplateViewPath, StringComparison.Ordinal))
        {
            template.ViewPath = AIInterviewDefaults.MockPracticeProductTemplateViewPath;
            changed = true;
        }

        if (template.DisplayOrder != 21)
        {
            template.DisplayOrder = 21;
            changed = true;
        }

        var ignoredProductTypes = ((int)ProductType.GroupedProduct).ToString();
        if (!string.Equals(template.IgnoredProductTypes, ignoredProductTypes, StringComparison.Ordinal))
        {
            template.IgnoredProductTypes = ignoredProductTypes;
            changed = true;
        }

        if (changed)
            await _productTemplateService.UpdateProductTemplateAsync(template);
    }

    protected async Task EnsurePricingCategoryTemplateAsync()
    {
        if (_categoryTemplateService == null)
            return;

        var templates = await _categoryTemplateService.GetAllCategoryTemplatesAsync();
        var template = templates.FirstOrDefault(item =>
            string.Equals(item.ViewPath, AIInterviewDefaults.PricingCategoryTemplateViewPath, StringComparison.OrdinalIgnoreCase)) ??
            templates.FirstOrDefault(item =>
                string.Equals(item.Name, AIInterviewDefaults.PricingCategoryTemplateName, StringComparison.OrdinalIgnoreCase));

        if (template == null)
        {
            await _categoryTemplateService.InsertCategoryTemplateAsync(new CategoryTemplate
            {
                Name = AIInterviewDefaults.PricingCategoryTemplateName,
                ViewPath = AIInterviewDefaults.PricingCategoryTemplateViewPath,
                DisplayOrder = 20
            });
            return;
        }

        var changed = false;
        if (!string.Equals(template.Name, AIInterviewDefaults.PricingCategoryTemplateName, StringComparison.Ordinal))
        {
            template.Name = AIInterviewDefaults.PricingCategoryTemplateName;
            changed = true;
        }

        if (!string.Equals(template.ViewPath, AIInterviewDefaults.PricingCategoryTemplateViewPath, StringComparison.Ordinal))
        {
            template.ViewPath = AIInterviewDefaults.PricingCategoryTemplateViewPath;
            changed = true;
        }

        if (changed)
            await _categoryTemplateService.UpdateCategoryTemplateAsync(template);
    }

    protected async Task EnsureWidgetActiveAsync()
    {
        if (_widgetSettings == null ||
            _widgetSettings.ActiveWidgetSystemNames.Contains(AIInterviewDefaults.SystemName, StringComparer.OrdinalIgnoreCase))
            return;

        _widgetSettings.ActiveWidgetSystemNames.Add(AIInterviewDefaults.SystemName);
        await _settingService.SaveSettingAsync(_widgetSettings);
    }

    protected static Dictionary<string, string> GetUpgradeLocaleResources()
    {
        return new Dictionary<string, string>
        {
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeFile.Help"] = "Upload a PDF or DOCX file up to 5 MB. If you already used a resume before, you can select it below.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeRequired"] = "Resume required",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.PreviousResume"] = "Use a previous resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.PreviousResume.Placeholder"] = "Select a previous resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.PreviousResume.Invalid"] = "Please select a valid previous resume.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.SortBy"] = "Sort by",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AccessDenied"] = "You do not have access to this interview report.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.Title"] = "Interview Session Error",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.StartAgain"] = "Start Again",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.ExpiredLink"] = "Your previous interview link expired. Start the interview again from this page.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.Unavailable"] = "The interview service is temporarily unavailable. Please try again.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Email.InterviewStartRefund.Subject"] = "Interview credit refunded for session %AIInterview.SessionId%",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Email.InterviewStartRefund.Heading"] = "Interview credit refunded",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Email.InterviewStartRefund.Message"] = "Your interview could not start, so the credit charge was reversed.",
            [FinalCompletionSpeechResourceKey] = DefaultFinalCompletionSpeech,
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Questions"] = "Questions",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Recording"] = "Recording",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.OpenRecording"] = "Open recording",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AverageTechnicalScore"] = "Average Technical Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AverageCommunication"] = "Average Communication",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AverageProfessionalism"] = "Average Professionalism",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AveragePositiveAttitude"] = "Average Positive Attitude",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.QuestionScores"] = "Question Scores",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Question"] = "Question",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.InterviewTurns"] = "Interview Turns",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Answer"] = "Answer",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Feedback"] = "Feedback",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.TechnicalScore"] = "Technical Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Communication"] = "Communication",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Professionalism"] = "Professionalism",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.PositiveAttitude"] = "Positive Attitude",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Pending"] = "(pending)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Asked"] = "Asked",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Answered"] = "Answered",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.OpenReport"] = "Open report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Overall"] = "Overall",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.QuestionCount"] = "Question Count",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Completed"] = "Completed",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Assessment"] = "Assessment",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Summary"] = "Summary",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AiFeedback"] = "AI feedback",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.CopyShareLink"] = "Copy share link",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Share"] = "Share",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.OpenSharePage"] = "Open share page",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.ViewReport"] = "View Report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Back"] = "Back",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.DrawerTitle"] = "Interview report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.ClosePanel"] = "Close report panel",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Loading"] = "Loading report...",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.LoadFailed"] = "Failed to load report.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Copied"] = "Copied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.CopyFailed"] = "Copy failed",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.RecordingShareTitle"] = "Interview recording",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.JobReportTitle"] = "{0} report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.JobRecordingTitle"] = "{0} recording",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.LinkCopied"] = "Link copied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.ShareTitle"] = "Interview Report - Skillfinder",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.BrandTagline"] = "AI-Powered Interview Platform",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.BrandCta"] = "Explore Skillfinder hiring CTA",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.TypeMock"] = "Practice Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.TypeJob"] = "Job Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.ContextResumeBased"] = "Evaluated against uploaded resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Common.CompletedUtc"] = "Completed",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Common.Available"] = "Available",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Common.Interview"] = "Interview",
            [AIInterviewDefaults.HomepageTopPerformersTitleResourceKey] = "Top Performers",
            [AIInterviewDefaults.HomepageTopPerformersScoreResourceKey] = "Best score",
            [AIInterviewDefaults.HomepageTopPerformersFallbackSkillResourceKey] = "Not specified",
            [AIInterviewDefaults.HomepageTopPerformersAvatarAltResourceKey] = "Default candidate avatar",
            [AIInterviewDefaults.HomepageTopPerformersPreviousResourceKey] = "Previous performers",
            [AIInterviewDefaults.HomepageTopPerformersNextResourceKey] = "Next performers",
            [AIInterviewDefaults.HomepageTopPerformersEmptyResourceKey] = "Top performers will appear here after completed interviews are evaluated.",
            [AIInterviewDefaults.HomepageTopPerformersUnknownCandidateResourceKey] = "Unknown candidate",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.Active"] = "Active",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.Attempt"] = "Attempt",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.ApplicationsCountLabel"] = "application(s)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.Applied"] = "Applied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Title"] = "My Activity",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.NavigationLabel"] = "My Activity",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Tab.AppliedJobs"] = "Applied Jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Tab.SavedJobs"] = "Saved Jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Tab.MockInterviews"] = "AI Interviews",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Loading"] = "Loading activity...",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.SavedJobs.Empty"] = "Saved jobs will appear here when you bookmark roles.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.MockPracticeLabel"] = "AI Practice",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.PracticeReportTitle"] = "Practice report - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.PracticeRecordingTitle"] = "Practice recording - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.OpenPracticeReport"] = "Open practice report - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.OpenPracticeRecording"] = "Open practice recording - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.HistoryFootnote"] = "Customer-side history includes overall results and per-question AI evaluation details.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.ApplyPanel.Description"] = "Apply for this role and start the AI interview directly from this page. Interview difficulty is handled automatically.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.SignInPrompt"] = "Sign in to apply and start the interview for this role.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.NextQuestion"] = "Next question ready.",
            ["Plugins.Misc.AIInterview.Admin.AiService.SupportPhoneNumber"] = "Support Phone Number",
            ["Plugins.Misc.AIInterview.Admin.AiService.SupportPhoneNumber.Hint"] = "Phone number shown to candidates when they select Talk to support team during an interview.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceEndpointUrl"] = "Azure Document Intelligence Endpoint URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceApiKey"] = "Azure Document Intelligence API Key",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceModelId"] = "Azure Document Intelligence Model ID",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceTimeoutSeconds"] = "Azure Document Intelligence Timeout Seconds",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceEndpointUrl.Hint"] = "Endpoint for the Azure AI Document Intelligence resource used to read candidate resumes.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceApiKey.Hint"] = "Used server-side only for resume text extraction. Leave blank to keep the existing key.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceModelId.Hint"] = "Use prebuilt-read unless Azure support instructs otherwise.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceTimeoutSeconds.Hint"] = "Maximum time to wait for resume reading before returning an extraction failure.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl"] = "Azure Blob Storage Container URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken"] = "Azure Blob Storage SAS Token",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl.Hint"] = "Used for server-side recording uploads and other media persistence.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken.Hint"] = "Paste the SAS token string exactly as issued. It is stored only in admin settings.",
            ["Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummaryMaxCompletionTokens"] = "Strengths Summary Max Completion Tokens",
            ["Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummaryMaxCompletionTokens.Hint"] = "Allowed range: 500 to 3000. Recommended range: 1200 to 1800.",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanMaxCompletionTokens"] = "Question Plan Max Completion Tokens",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanMaxCompletionTokens.Hint"] = "Allowed range: 2000 to 32000. Recommended: 8000. Increase if question plan returns empty content.",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanRetryMaxCompletionTokens"] = "Question Plan Retry Max Completion Tokens",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanRetryMaxCompletionTokens.Hint"] = "Allowed range: 4000 to 64000. Recommended: 16000. Used when the first plan attempt is truncated.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadMaxMb"] = "Recording Upload Max MB",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadMaxMb.Hint"] = "Allowed range: 80 to 250 MB. Uploads larger than this are blocked before submit and rejected server-side.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingVideoBitsPerSecond"] = "Recording Video Bits Per Second",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingVideoBitsPerSecond.Hint"] = "Allowed range: 350000 to 1200000.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingAudioBitsPerSecond"] = "Recording Audio Bits Per Second",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingAudioBitsPerSecond.Hint"] = "Allowed range: 32000 to 128000.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingSourceMode"] = "Recording Source Mode",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingSourceMode.Hint"] = "ScreenPreferred records screen video when available and falls back to camera video.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadTimeoutMs"] = "Recording Upload Timeout MS",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadTimeoutMs.Hint"] = "Allowed range: 5000 to 60000 milliseconds.",
            ["Plugins.Misc.AIInterview.Admin.AiService.FinalizationWaitTimeoutMs"] = "Finalization Wait Timeout MS",
            ["Plugins.Misc.AIInterview.Admin.AiService.FinalizationWaitTimeoutMs.Hint"] = "Allowed range: 5000 to 45000 milliseconds.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Menu.FeedbackReports"] = "Feedback Reports",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Title"] = "Feedback Reports",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Search"] = "Search",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Submitted"] = "Submitted",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.SubmittedFrom"] = "Submitted from",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.SubmittedTo"] = "Submitted to",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Candidate"] = "Candidate",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.CandidateEmail"] = "Candidate Email",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Issue"] = "Issue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Helpfulness"] = "Helpfulness",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Comment"] = "Comment preview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Attachment"] = "Attachment",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.SessionId"] = "Session ID",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Details"] = "Details",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.HasAttachment"] = "Has attachment",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.All"] = "All",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Yes"] = "Yes",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.No"] = "No",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.EmailInvalid"] = "Enter a valid email address.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Status"] = "Status",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.StatusComment"] = "Status comment",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Update"] = "Update",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Filter"] = "Apply filters",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.FilterTitle"] = "Filter applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.FilterSubtitle"] = "Refine candidates by status, score, and application date.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ExportCsv"] = "Export CSV",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.UpdateStatus.Invalid"] = "Select a valid application status.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.JobTitle"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.MinScore"] = "Minimum score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.MaxScore"] = "Maximum score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ChargeMode"] = "Charge Mode",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ChargeMode.CompanySponsored"] = "Company sponsored",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ChargeMode.CandidatePaid"] = "Candidate paid",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Attempts"] = "Attempts",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.PromptSource"] = "Prompt Source",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.InterviewSort"] = "Sort by",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.TopScorersFirst"] = "Top scorers first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.LowestScorersFirst"] = "Lowest scorers first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.LatestApplied"] = "Latest applied first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedFromUtc"] = "Applied from",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedToUtc"] = "Applied to",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.OnlyWithInterviewScore"] = "Only candidates with interview score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ResultsSummary"] = "Showing {0} filtered result(s)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.PageSize"] = "Page size",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Reset"] = "Reset",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.All"] = "All",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Phone"] = "Phone",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.InterviewScore"] = "Interview score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedOn"] = "Applied on",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Resume"] = "Resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.DownloadResume"] = "Download resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.NoResume"] = "No resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.CoverMessage"] = "Cover message",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Resume.NotFound"] = "Resume file was not found.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Title"] = "Employer Interview Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.CreateTitle"] = "Create Employer Invite",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.ActiveTitle"] = "Active Employer Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.ExpiryDate"] = "Expiry Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Deactivate.Tooltip"] = "Deactivate invite",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.BulkSuccess"] = "Successfully created {0} invites. {1} emails were invalid.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.Pending"] = "Pending",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.TotalJobs"] = "Total jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.TotalApplications"] = "Total applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CompletedInterviews"] = "Completed interviews",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Shortlisted"] = "Shortlisted applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.AverageScore"] = "Average score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.HighestScore"] = "Highest score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CreateJob"] = "Create a Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ViewApplications"] = "View Applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.RecentApplications"] = "Recent Applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.NoApplications"] = "No applications have been submitted yet.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Title"] = "Employer Scoreboard",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Eyebrow"] = "Recruitment Analytics Desk",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Intro"] = "Review assessment outcomes, verify interview completion, and move hiring decisions forward from one candidate metrics workspace.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CreateJobAction"] = "Create Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ReviewApplicationsAction"] = "Review Applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ManageInvitesAction"] = "Manage Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.TotalCompletedAssessments"] = "Total Completed Assessments",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.TotalSubmissions"] = "{0} total candidate submissions tracked",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.AverageAnalyticalScore"] = "Average Analytical Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.HighestRecordedScore"] = "Highest recorded score {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ActiveFlaggedViolations"] = "Active Flagged Violations",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ShortlistedDetail"] = "{0} shortlisted for next-step review",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.OpenJobModules"] = "Open Job Modules",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CandidateVolume"] = "Candidate Volume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ShortlistQueue"] = "Shortlist Queue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CandidateAssessmentMatrix"] = "Candidate Assessment Matrix",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CandidateName"] = "Candidate Name",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.AssessmentModule"] = "Assessment Module",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CompletionDate"] = "Completion Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CoreAiRating"] = "Core AI Rating",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.VerificationStatus"] = "Verification Status",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Actions"] = "Actions",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.AssessmentWorkflow"] = "Assessment workflow",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.InterviewCompleted"] = "Interview completed",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ApplicationReceived"] = "Application received",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ViewAnalysis"] = "View Analysis",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ReviewQueue"] = "Review Queue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.NoAssessments"] = "No candidate assessments have been submitted yet.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Verification.Verified"] = "Verified",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Verification.Flagged"] = "Flagged",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Verification.Evaluating"] = "Evaluating",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.Kicker"] = "AI Interview Role",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.WorkArrangement"] = "Work arrangement",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.EmploymentType"] = "Employment type",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.JobLocation"] = "Job location",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.SalaryRange"] = "Salary range",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.ExperienceLevel"] = "Experience level",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.Posted"] = "Posted {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.AppliedCount"] = "{0} applied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.ViewJob"] = "View job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.SaveJob"] = "Save job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.RemoveSavedJob"] = "Remove from saved jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.SavedToSavedJobs"] = "Saved to saved jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.RemovedFromSavedJobs"] = "Removed from saved jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.JobPreview"] = "Job preview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.CloseJobPreview"] = "Close job preview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.LoadingJobDetails"] = "Loading job details...",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.UnableToLoadJobDetails"] = "Unable to load job details.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.SavedJobsUnavailable"] = "Saved jobs are temporarily unavailable.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.JobNotFound"] = "The selected job could not be found.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.InvalidJob"] = "The selected product is not an AI interview job.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.Kicker"] = "AI Interview Role",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.HiringCompany"] = "Hiring Company",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.CandidatesApplied"] = "{0} applied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.ViewJob"] = "View job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.EmailAFriend"] = "Email a friend",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.SaveJob"] = "Save job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.SavedJob"] = "Saved job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.SaveToCustomWishlist"] = "Save to custom wishlist",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.SaveFirstForWishlist"] = "Save the job first, then choose a custom wishlist.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.JobDescription"] = "Job description",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.RoleHighlights"] = "Role highlights",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.RoleHighlightsFallback"] = "Key responsibilities and outcomes will be discussed during the interview process.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.Skills"] = "Skills",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.SkillsFallback"] = "Skills will be evaluated during the AI interview.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.JobDetails"] = "Job details",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Dashboard.Title"] = "Employer Dashboard",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Dashboard.NavigationLabel"] = "Employer dashboard",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Dashboard.Tab.Overview"] = "Overview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Dashboard.Tab.Jobs"] = "Jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Dashboard.Tab.Applications"] = "Applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Dashboard.Tab.Invites"] = "Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Dashboard.Action.ReviewQueue"] = "Review queue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Dashboard.Action.ViewAnalysis"] = "View analysis",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Title"] = "Jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.JobTitle"] = "Job title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Status"] = "Status",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Salary"] = "Salary",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.CreatedOn"] = "Posted on",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.ApplicationCount"] = "Applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Actions"] = "Actions",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Create"] = "Create Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Edit"] = "Edit",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Publish"] = "Publish",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Unpublish"] = "Unpublish",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Published"] = "Published",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Unpublished"] = "Unpublished",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Jobs.Empty"] = "No jobs found yet.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Name"] = "Job title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Name.Required"] = "Job title is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ShortDescription"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.FullDescription"] = "Job Description",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Sku"] = "Reference code",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Published"] = "Published",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ResumeRequired"] = "Resume required",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.InterviewRequired"] = "Interview required",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.MinimumScore"] = "Minimum score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.MinimumScore.Hint"] = "Set the minimum interview score required before the application can proceed.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionCount"] = "Question count",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionCount.Hint"] = "Choose how many interview questions the applicant should answer for this job.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ApplyUntilUtc"] = "Apply until",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ExperienceLevel"] = "Experience level",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.WorkMode"] = "Work mode",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.EmploymentType"] = "Employment type",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.JobLocation"] = "Job location",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.SalaryRange"] = "Salary range",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.SalaryMinCtcPa"] = "Min CTC (LPA)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.SalaryMaxCtcPa"] = "Max CTC (LPA)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Settings"] = "Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Select"] = "Select",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ApplyUntilUtc.Past"] = "Apply until date cannot be in the past.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.MinimumScore.Range"] = "Minimum score must be between 0 and 100.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionCount.Range"] = "Question count must be between 1 and 10 when interview is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ExperienceLevel.Invalid"] = "Select a valid experience level.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.WorkMode.Invalid"] = "Select a valid work mode.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.EmploymentType.Invalid"] = "Select a valid employment type.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.JobLocation.Invalid"] = "Select a valid job location.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.JobLocation.Unsupported"] = "Job location metadata is not configured. Configure the related specification attribute before creating the job.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.SalaryRange.Unsupported"] = "Salary range metadata is not configured. Configure the related specification attribute before creating the job.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.SalaryMinCtcPa.Invalid"] = "Minimum CTC must be zero or greater.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.SalaryMaxCtcPa.Invalid"] = "Maximum CTC must be zero or greater.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.SalaryRange.Invalid"] = "Maximum CTC must be greater than or equal to minimum CTC.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Section.RoleOverview"] = "Role Overview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Section.Requirements"] = "Requirements",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Section.JobContent"] = "Job Content",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Section.InterviewSettings"] = "Interview Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Submit"] = "Create Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.SubmitEdit"] = "Update Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Success"] = "The job was created successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.UpdateSuccess"] = "The job was updated successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Unavailable"] = "Job creation is temporarily unavailable.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Title"] = "Create a Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.EditTitle"] = "Edit Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.BackToJobs"] = "Back to Jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ViewJob"] = "View Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.InterviewMode"] = "Interview mode",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.InterviewMode.AiResumeBased"] = "AI Resume-Based",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.InterviewMode.FixedQuestionBased"] = "Fixed-Question Based",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.InterviewMode.Invalid"] = "Select a valid interview mode.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ResumeRequired.AiMode"] = "AI Resume-Based interviews require the applicant resume flow.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSet"] = "Question set",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSet.Action"] = "Question set action",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSet.ChooseExisting"] = "Choose existing",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSet.CreateNew"] = "Create new",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSet.CloneExisting"] = "Clone existing",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSet.Select"] = "Select a question set",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSet.Required"] = "Select a question set owned by your employer account.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSet.Invalid"] = "The question set could not be saved. Review the set and try again.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSet.Unavailable"] = "Question set management is temporarily unavailable.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSet.Workflow.Invalid"] = "Select a valid question set action.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSetName"] = "Question set name",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionSetName.Required"] = "Question set name is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItems"] = "Questions",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItems.Hint"] = "Create or clone a set with exactly 5 or 10 active questions. Drag questions into the required order.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItems.Required"] = "Add exactly 5 or 10 active questions.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItems.Range"] = "A question set must contain exactly 5 or 10 active questions.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItems.Count"] = "Fixed question sets must contain exactly 5 or 10 active questions.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItems.ExistingReadOnly"] = "Existing reusable sets are linked without changes. Choose Clone existing to edit a private copy.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItem.Add"] = "Add question",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItem.Remove"] = "Remove",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItem.Move"] = "Move question",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItem.Text"] = "Question text",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItem.RubricHint"] = "Rubric hint (optional)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionItem.ExpectedSignals"] = "Expected-signal notes (optional)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.LaunchReady"] = "You can start the interview when ready."
        };
    }

    protected async Task EnsureFixedQuestionProductTemplateAsync()
    {
        if (_productTemplateService == null)
            return;

        var templates = await _productTemplateService.GetAllProductTemplatesAsync();
        var template = templates.FirstOrDefault(item =>
            string.Equals(item.ViewPath, AIInterviewDefaults.FixedQuestionProductTemplateViewPath, StringComparison.OrdinalIgnoreCase)) ??
            templates.FirstOrDefault(item =>
                string.Equals(item.Name, AIInterviewDefaults.FixedQuestionProductTemplateName, StringComparison.OrdinalIgnoreCase));

        if (template == null)
        {
            await _productTemplateService.InsertProductTemplateAsync(new ProductTemplate
            {
                Name = AIInterviewDefaults.FixedQuestionProductTemplateName,
                ViewPath = AIInterviewDefaults.FixedQuestionProductTemplateViewPath,
                DisplayOrder = 21,
                IgnoredProductTypes = ((int)ProductType.GroupedProduct).ToString()
            });
            return;
        }

        var changed = false;
        if (!string.Equals(template.Name, AIInterviewDefaults.FixedQuestionProductTemplateName, StringComparison.Ordinal))
        {
            template.Name = AIInterviewDefaults.FixedQuestionProductTemplateName;
            changed = true;
        }

        if (!string.Equals(template.ViewPath, AIInterviewDefaults.FixedQuestionProductTemplateViewPath, StringComparison.Ordinal))
        {
            template.ViewPath = AIInterviewDefaults.FixedQuestionProductTemplateViewPath;
            changed = true;
        }

        if (changed)
            await _productTemplateService.UpdateProductTemplateAsync(template);
    }

    protected static Dictionary<string, string> GetMyActivityCreditLocaleResources()
    {
        return new Dictionary<string, string>
        {
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Tab.Credits"] = "Credits",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.CurrentBalance"] = "Current balance",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.TotalDeposited"] = "Total deposited",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.TotalWithdrawn"] = "Total withdrawn",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.Date"] = "Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.Type"] = "Type",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.Credits"] = "Credits",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.BalanceAfter"] = "Balance after",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.JobProduct"] = "Job/Product",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.Source"] = "Source",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.Description"] = "Description",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Credits.Empty"] = "No credit activity yet"
        };
    }

    protected static Dictionary<string, string> GetRuntimeTourLocaleResources()
    {
        return new Dictionary<string, string>
        {
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.TriggerLabel"] = "Take a Tour of the interview controls and interview flow",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Intro.Title"] = "Welcome to your interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Intro.Description"] = "Get ready before you begin. This quick tour explains the interview controls, where questions and answers appear, and how to move through the interview from start to completion.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Guidelines.Title"] = "Review guidelines and readiness",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Guidelines.Description"] = "Open the guidelines to review expectations and complete the required camera, microphone, speaker, connection, and speech checks before starting.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Microphone.Title"] = "Control your microphone",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Microphone.Description"] = "Turn your microphone on when you are ready to speak and use this control to mute or unmute during the interview.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Start.Title"] = "Start and continue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Start.Description"] = "After the readiness checks are complete, use this button to start the interview and continue through the expected question flow.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Camera.Title"] = "Control your camera",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Camera.Description"] = "Turn your camera preview on or off here. Confirm that you are clearly visible before the interview begins.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.ScreenShare.Title"] = "Share your screen",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.ScreenShare.Description"] = "Use this control when screen sharing is required. Keep sharing active throughout the interview to avoid interruptions.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Conversation.Title"] = "Follow the conversation",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Conversation.Description"] = "Interview questions, prompts, and conversation updates appear in this panel so you can follow the discussion as it progresses.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Answer.Title"] = "Provide your answer",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Answer.Description"] = "Use the answer area when typed responses are available. Review your response before submitting it for the current question.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Details.Title"] = "Check interview details",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Details.Description"] = "Open this tab to review interview progress, elapsed time, and the current status of your camera, microphone, and screen share.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Completion.Title"] = "Complete the interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Completion.Description"] = "Use this control when you need to finish the interview. Confirm completion only after you have answered all expected questions.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Previous"] = "Previous",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Next"] = "Next",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Done"] = "Done",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Tour.Progress"] = "Step {{current}} of {{total}}"
        };
    }

    protected static Dictionary<string, string> GetAdminLocaleResources()
    {
        return new Dictionary<string, string>
        {
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Menu.Root"] = "AI Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Menu.Configure"] = "Configure",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Menu.AiService"] = "AI Service",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Menu.SponsorInvites"] = "Sponsor Invite Management",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Menu.VendorCredits"] = "Vendor Credits",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Menu.ApplicantCredits"] = "Applicant Credits",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Menu.Scoreboard"] = "Candidate Scoreboard",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Menu.MockPracticeSessions"] = "AI Practice Sessions",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Menu.FeedbackReports"] = "Feedback Reports",
            ["Plugins.Misc.AIInterview.Admin.Configure.Title"] = "AI Interview Configuration",
            ["Plugins.Misc.AIInterview.Admin.Configure.General"] = "General Settings",
            ["Plugins.Misc.AIInterview.Admin.Configure.Service"] = "AI Service Settings",
            ["Plugins.Misc.AIInterview.Admin.Configure.CreditPack"] = "Credit Pack Settings",
            ["Plugins.Misc.AIInterview.Admin.Configure.MockInterviewQuestionCount"] = "AI Interview Question count",
            ["Plugins.Misc.AIInterview.Admin.Configure.MockInterviewQuestionCount.Hint"] = "Applies to mock interviews only. Allowed range: 1 to 10. Default is 5.",
            ["Plugins.Misc.AIInterview.Admin.MockConfigure.Subtitle"] = "Preview-only admin workspace",
            ["Plugins.Misc.AIInterview.Admin.MockConfigure.General.Summary"] = "AI admin experience",
            ["Plugins.Misc.AIInterview.Admin.MockConfigure.Service.Summary"] = "Live settings managed elsewhere",
            ["Plugins.Misc.AIInterview.Admin.MockConfigure.CreditPack.Summary"] = "Informational layout only",
            ["Plugins.Misc.AIInterview.Admin.MockConfigure.General.Body"] = "This AI page keeps the AIInterview admin entry point polished while previewing the plugin-owned workspace layout.",
            ["Plugins.Misc.AIInterview.Admin.MockConfigure.Service.Body"] = "Live AI service settings, routing, and operational changes continue to be managed on the primary configuration screens.",
            ["Plugins.Misc.AIInterview.Admin.MockConfigure.CreditPack.Body"] = "Credit pack setup remains informational here; use the dedicated service and credit screens for operational credit settings.",
            ["Plugins.Misc.AIInterview.Admin.ProductRequirements.Title"] = "AI Interview Job Requirements",
            ["Plugins.Misc.AIInterview.Admin.ProductRequirements.Hint"] = "These settings are saved on the product itself and are used when candidates apply.",
            ["Plugins.Misc.AIInterview.Admin.ProductRequirements.ResumeRequired"] = "Resume Required",
            ["Plugins.Misc.AIInterview.Admin.ProductRequirements.InterviewRequired"] = "Interview Required",
            ["Plugins.Misc.AIInterview.Admin.ProductRequirements.MinimumScore"] = "Minimum Score",
            ["Plugins.Misc.AIInterview.Admin.ProductRequirements.MinimumScore.Hint"] = "Set the minimum interview score required for this job. Leave 0 to use the default fallback.",
            ["Plugins.Misc.AIInterview.Admin.ProductRequirements.QuestionCount"] = "Question Count",
            ["Plugins.Misc.AIInterview.Admin.ProductRequirements.QuestionCount.Hint"] = "Set how many interview questions this job should ask. Allowed range: 1 to 10.",
            ["Plugins.Misc.AIInterview.Admin.AiService.Title"] = "AI Interview Service Settings",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiEndpointUrl"] = "Azure OpenAI Endpoint URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiEndpointUrl.Hint"] = "Use the Azure OpenAI resource endpoint, for example https://your-resource.openai.azure.com/. Do not include /openai/deployments or query-string API versions.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiApiKey"] = "Azure OpenAI API Key",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiDeploymentOrModel"] = "Azure OpenAI Deployment / Model",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiDeploymentOrModel.Hint"] = "Enter the Azure OpenAI deployment name configured for the resource. The SDK resolves the deployment path.",
            ["Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummaryMaxCompletionTokens"] = "Strengths Summary Max Completion Tokens",
            ["Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummaryMaxCompletionTokens.Hint"] = "Allowed range: 500 to 3000. Recommended range: 1200 to 1800.",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanMaxCompletionTokens"] = "Question Plan Max Completion Tokens",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanMaxCompletionTokens.Hint"] = "Allowed range: 2000 to 32000. Recommended: 8000. Increase if question plan returns empty content.",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanRetryMaxCompletionTokens"] = "Question Plan Retry Max Completion Tokens",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanRetryMaxCompletionTokens.Hint"] = "Allowed range: 4000 to 64000. Recommended: 16000. Used when the first plan attempt is truncated.",
            ["Plugins.Misc.AIInterview.Admin.AiService.ResumeProfileExtractionSystemPrompt"] = "Resume Profile Extraction System Prompt",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanSystemPrompt"] = "Question Plan System Prompt",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanBuilderInstructionBlock"] = "Question Plan Builder Instruction Block",
            ["Plugins.Misc.AIInterview.Admin.AiService.RuntimeQuestionGenerationSystemPrompt"] = "Runtime Question Generation System Prompt",
            ["Plugins.Misc.AIInterview.Admin.AiService.RuntimeScoringSystemPrompt"] = "Runtime Scoring System Prompt",
            ["Plugins.Misc.AIInterview.Admin.AiService.RuntimeScoringRetryAddendumPrompt"] = "Runtime Scoring Retry Addendum Prompt",
            ["Plugins.Misc.AIInterview.Admin.AiService.FinalScoringSystemPrompt"] = "Final Scoring System Prompt",
            ["Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummarySystemPrompt"] = "Strengths Summary System Prompt",
            ["Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummaryRetryStrictJsonSystemPrompt"] = "Strengths Summary Retry Strict JSON System Prompt",
            ["Plugins.Misc.AIInterview.Admin.AiService.ResumeProfileExtractionSystemPrompt.Hint"] = "Used as the system prompt when extracting resume facts. Changes can affect resume profile JSON consumed by question planning.",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanSystemPrompt.Hint"] = "Used as the system prompt when creating planned interview questions. Changes can affect question count, categories, and JSON contract compliance.",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanBuilderInstructionBlock.Hint"] = "Inserted into the question plan user prompt after job and resume context. Changes can affect sequencing and duplicate-question handling.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RuntimeQuestionGenerationSystemPrompt.Hint"] = "Used as the system prompt for live next-question generation. Changes can affect runtime JSON parsing and interview flow.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RuntimeScoringSystemPrompt.Hint"] = "Used as the normal runtime scoring system prompt. Changes can affect scoring fields, score scale, and answer-quality handling.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RuntimeScoringRetryAddendumPrompt.Hint"] = "Appended only to the retry user prompt after suspicious zero scoring. Changes can affect retry-only score correction behavior.",
            ["Plugins.Misc.AIInterview.Admin.AiService.FinalScoringSystemPrompt.Hint"] = "Used as the system prompt for completion-time final scoring. Changes can affect final turn scoring and overall score JSON.",
            ["Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummarySystemPrompt.Hint"] = "Used as the system prompt for strengths summaries. Changes can affect summary length, tone, and JSON contract compliance.",
            ["Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummaryRetryStrictJsonSystemPrompt.Hint"] = "Used as the retry system prompt when a strengths summary response is truncated. Changes can affect retry recovery and strict JSON output.",
            ["Plugins.Misc.AIInterview.Admin.AiService.TestConnection"] = "Test Azure OpenAI connection",
            ["Plugins.Misc.AIInterview.Admin.AiService.TestConnection.Progress"] = "Testing Azure OpenAI connection...",
            ["Plugins.Misc.AIInterview.Admin.AiService.TestConnection.Success"] = "Azure OpenAI connection succeeded.",
            ["Plugins.Misc.AIInterview.Admin.AiService.TestConnection.Failure"] = "Azure OpenAI connection failed. {0}",
            ["Plugins.Misc.AIInterview.Admin.AiService.TestConnection.Exception"] = "Azure OpenAI connection failed. Check endpoint, API key, and deployment/model.",
            ["Plugins.Misc.AIInterview.Admin.AiService.TestConnection.ConfigurationIncomplete"] = "Azure OpenAI settings are incomplete. Save endpoint, API key, and deployment/model first.",
            ["Plugins.Misc.AIInterview.Admin.AiService.TestConnection.UnknownFailure"] = "Azure OpenAI connection test failed.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechKey"] = "Azure Speech Key",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechRegion"] = "Azure Speech Region",
            ["Plugins.Misc.AIInterview.Admin.AiService.SupportPhoneNumber"] = "Support Phone Number",
            ["Plugins.Misc.AIInterview.Admin.AiService.SupportPhoneNumber.Hint"] = "Phone number shown to candidates when they select Talk to support team during an interview.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceEndpointUrl"] = "Azure Document Intelligence Endpoint URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceApiKey"] = "Azure Document Intelligence API Key",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceModelId"] = "Azure Document Intelligence Model ID",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceTimeoutSeconds"] = "Azure Document Intelligence Timeout Seconds",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceEndpointUrl.Hint"] = "Endpoint for the Azure AI Document Intelligence resource used to read candidate resumes.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceApiKey.Hint"] = "Used server-side only for resume text extraction. Leave blank to keep the existing key.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceModelId.Hint"] = "Use prebuilt-read unless Azure support instructs otherwise.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceTimeoutSeconds.Hint"] = "Maximum time to wait for resume reading before returning an extraction failure.",
            ["Plugins.Misc.AIInterview.Admin.AiService.TrackAzureOpenAiUsage"] = "Track Azure OpenAI Usage",
            ["Plugins.Misc.AIInterview.Admin.AiService.TrackAzureSpeechUsage"] = "Track Azure Speech Usage",
            ["Plugins.Misc.AIInterview.Admin.AiService.CalculateAzureCostPerInterview"] = "Calculate Azure Cost Per Interview",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiPromptTokenPricePerThousand"] = "Azure OpenAI Prompt Token Price Per Thousand",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiCompletionTokenPricePerThousand"] = "Azure OpenAI Completion Token Price Per Thousand",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechRecognitionPricePerHour"] = "Azure Speech Recognition Price Per Hour",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechSynthesisPricePerThousandCharacters"] = "Azure Speech Synthesis Price Per Thousand Characters",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureUsageCurrencyCode"] = "Azure Usage Currency Code",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl"] = "Azure Blob Storage Container URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken"] = "Azure Blob Storage SAS Token",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl.Hint"] = "Used for server-side recording uploads and other media persistence.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken.Hint"] = "Paste the SAS token string exactly as issued. It is stored only in admin settings.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadMaxMb"] = "Recording Upload Max MB",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadMaxMb.Hint"] = "Allowed range: 80 to 250 MB. Uploads larger than this are blocked before submit and rejected server-side.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingVideoBitsPerSecond"] = "Recording Video Bits Per Second",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingVideoBitsPerSecond.Hint"] = "Allowed range: 350000 to 1200000.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingAudioBitsPerSecond"] = "Recording Audio Bits Per Second",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingAudioBitsPerSecond.Hint"] = "Allowed range: 32000 to 128000.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingSourceMode"] = "Recording Source Mode",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingSourceMode.Hint"] = "ScreenPreferred records screen video when available and falls back to camera video.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadTimeoutMs"] = "Recording Upload Timeout MS",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadTimeoutMs.Hint"] = "Allowed range: 5000 to 60000 milliseconds.",
            ["Plugins.Misc.AIInterview.Admin.AiService.FinalizationWaitTimeoutMs"] = "Finalization Wait Timeout MS",
            ["Plugins.Misc.AIInterview.Admin.AiService.FinalizationWaitTimeoutMs.Hint"] = "Allowed range: 5000 to 45000 milliseconds.",
            ["Plugins.Misc.AIInterview.Admin.AiService.CreditProductSkuMappingsJson"] = "Credit Product SKU Mappings (JSON)",
            ["Plugins.Misc.AIInterview.Admin.AiService.CreditProductSkuMappingsJson.Hint"] = "Map product SKUs to credits granted per unit. Example: {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10,\"AI-CREDIT-20\":20}. Create normal Pricing-category products with those SKUs and prices. Credits are granted only after successful payment for registered customers.",
            ["Plugins.Misc.AIInterview.Admin.AiService.CreditProductSkuMappingsJson.Invalid"] = "The credit product SKU mappings JSON is invalid. Use a JSON object such as {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10}.",
            ["Plugins.Misc.AIInterview.Admin.AiService.CreditPurchasePageUrl"] = "Credit Purchase Page URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.CreditPurchasePageUrl.Hint"] = "Relative or absolute URL used by the job page when the user has no credits. The default is /pricing.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Title"] = "Feedback Reports",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Search"] = "Search",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Submitted"] = "Submitted",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.SubmittedFrom"] = "Submitted from",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.SubmittedTo"] = "Submitted to",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Candidate"] = "Candidate",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.CandidateEmail"] = "Candidate Email",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Issue"] = "Issue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Helpfulness"] = "Helpfulness",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Comment"] = "Comment preview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Attachment"] = "Attachment",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.SessionId"] = "Session ID",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Details"] = "Details",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.HasAttachment"] = "Has attachment",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.All"] = "All",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.Yes"] = "Yes",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.FeedbackReports.No"] = "No",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.Title"] = "Sponsor Invite Management",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.Create"] = "Create Invites",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.List"] = "Existing Invites",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.Email"] = "Email",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.Status"] = "Status",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.BulkEmails"] = "Bulk Emails",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.ProductId"] = "Product ID",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.MaxAttempts"] = "Max Attempts",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.ExpiryDateUtc"] = "Expiry Date",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.CreatedOn"] = "Created On",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.SponsorId"] = "Sponsor ID",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.Deactivate"] = "Deactivate",
            ["Plugins.Misc.AIInterview.Employer.Invite.Exhausted"] = "Exhausted",
            ["Plugins.Misc.AIInterview.Admin.Credits.VendorTitle"] = "Vendor Credits",
            ["Plugins.Misc.AIInterview.Admin.Credits.ApplicantTitle"] = "Applicant Credits",
            ["Plugins.Misc.AIInterview.Admin.Credits.TopUp"] = "Top Up",
            ["Plugins.Misc.AIInterview.Admin.Credits.CustomerId"] = "Customer ID",
            ["Plugins.Misc.AIInterview.Admin.Credits.LoadCustomerId"] = "Load by customer ID",
            ["Plugins.Misc.AIInterview.Admin.Credits.LoadCustomerEmail"] = "Load by customer email",
            ["Plugins.Misc.AIInterview.Admin.Credits.Amount"] = "Amount",
            ["Plugins.Misc.AIInterview.Admin.Credits.Credits"] = "Credits",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.Title"] = "Applicant Credit Activity",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.LoadApplicant"] = "Load Applicant",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.SelectedApplicant"] = "Selected Applicant",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.SelectApplicant"] = "Select applicant",
            ["Plugins.Misc.AIInterview.Admin.Credits.SelectApplicant"] = "Select applicant",
            ["Plugins.Misc.AIInterview.Admin.Credits.SelectVendor"] = "Select vendor",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.WalletBalance"] = "Wallet Balance",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.TotalDeposited"] = "Total Deposited",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.TotalWithdrawn"] = "Total Withdrawn",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.LastCreditActivityUtc"] = "Last Credit Activity",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.LastCreditActivity"] = "Last Credit Activity",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.ViewLedger"] = "View Ledger",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.Empty"] = "No applicant credit activity found.",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Empty"] = "No ledger entries found for the selected applicant.",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Title"] = "Ledger",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Customer"] = "Customer",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Amount"] = "Amount",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Type"] = "Type",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Remarks"] = "Remarks",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Utc"] = "Created On",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.CreatedOn"] = "Created On",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.SearchKeyword"] = "Applicant name or email",
            ["Plugins.Misc.AIInterview.Admin.Credits.CustomerRequired"] = "Customer is required.",
            ["Plugins.Misc.AIInterview.Admin.Credits.InvalidVendorScope"] = "The selected customer is not a vendor account.",
            ["Plugins.Misc.AIInterview.Admin.Credits.InvalidApplicantScope"] = "The selected customer is not an applicant account.",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.Title"] = "Candidate Scoreboard",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.Filter"] = "Apply Filters",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.Export"] = "Export CSV",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.Report"] = "Open report",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.Candidate"] = "Candidate",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.Vendor"] = "Vendor / Company",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.JobPosting"] = "Job Posting",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.Status"] = "Status",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.MinScore"] = "Minimum Score",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.MaxScore"] = "Maximum Score",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.StartDate"] = "Start Date",
            ["Plugins.Misc.AIInterview.Admin.Scoreboard.EndDate"] = "End Date",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Title"] = "AI Practice Sessions",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Search"] = "Search",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.SessionId"] = "Session ID",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Customer"] = "Customer",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.CustomerEmail"] = "Customer Email",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Product"] = "Product",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Difficulty"] = "Difficulty",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Status"] = "Status",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.HasResume"] = "Has Resume",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.QuestionCount"] = "Questions",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Score"] = "Score",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.CreatedOn"] = "Created On",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.StartedOn"] = "Started On",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.CompletedOn"] = "Completed On",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.ViewReport"] = "View Report",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.ViewCustomer"] = "View Customer",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.SelectedInputs"] = "Selected Inputs",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.DateFrom"] = "Date From",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.DateTo"] = "Date To",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.MinScore"] = "Min Score",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.MaxScore"] = "Max Score",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Status.All"] = "All",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Status.Active"] = "Active",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Status.Completed"] = "Completed",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.HasResume.All"] = "All",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.HasResume.Yes"] = "Yes",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.HasResume.No"] = "No",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Difficulty.All"] = "All",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Difficulty.Low"] = "Low",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Difficulty.Medium"] = "Medium",
            ["Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Difficulty.Advanced"] = "Advanced",
            ["Plugins.Misc.AIInterview.Admin.AiService.SupportPhoneNumber"] = "Support Phone Number",
            ["Plugins.Misc.AIInterview.Admin.AiService.SupportPhoneNumber.Hint"] = "Phone number shown to candidates when they select Talk to support team during an interview.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceEndpointUrl"] = "Azure Document Intelligence Endpoint URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceApiKey"] = "Azure Document Intelligence API Key",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceModelId"] = "Azure Document Intelligence Model ID",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceTimeoutSeconds"] = "Azure Document Intelligence Timeout Seconds",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceEndpointUrl.Hint"] = "Endpoint for the Azure AI Document Intelligence resource used to read candidate resumes.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceApiKey.Hint"] = "Used server-side only for resume text extraction. Leave blank to keep the existing key.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceModelId.Hint"] = "Use prebuilt-read unless Azure support instructs otherwise.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceTimeoutSeconds.Hint"] = "Maximum time to wait for resume reading before returning an extraction failure.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl"] = "Azure Blob Storage Container URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken"] = "Azure Blob Storage SAS Token",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl.Hint"] = "Used for server-side recording uploads and other media persistence.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken.Hint"] = "Paste the SAS token string exactly as issued. It is stored only in admin settings.",
            ["Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummaryMaxCompletionTokens"] = "Strengths Summary Max Completion Tokens",
            ["Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummaryMaxCompletionTokens.Hint"] = "Allowed range: 500 to 3000. Recommended range: 1200 to 1800.",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanMaxCompletionTokens"] = "Question Plan Max Completion Tokens",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanMaxCompletionTokens.Hint"] = "Allowed range: 2000 to 32000. Recommended: 8000. Increase if question plan returns empty content.",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanRetryMaxCompletionTokens"] = "Question Plan Retry Max Completion Tokens",
            ["Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanRetryMaxCompletionTokens.Hint"] = "Allowed range: 4000 to 64000. Recommended: 16000. Used when the first plan attempt is truncated.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadMaxMb"] = "Recording Upload Max MB",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadMaxMb.Hint"] = "Allowed range: 80 to 250 MB. Uploads larger than this are blocked before submit and rejected server-side.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingVideoBitsPerSecond"] = "Recording Video Bits Per Second",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingVideoBitsPerSecond.Hint"] = "Allowed range: 350000 to 1200000.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingAudioBitsPerSecond"] = "Recording Audio Bits Per Second",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingAudioBitsPerSecond.Hint"] = "Allowed range: 32000 to 128000.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingSourceMode"] = "Recording Source Mode",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingSourceMode.Hint"] = "ScreenPreferred records screen video when available and falls back to camera video.",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadTimeoutMs"] = "Recording Upload Timeout MS",
            ["Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadTimeoutMs.Hint"] = "Allowed range: 5000 to 60000 milliseconds.",
            ["Plugins.Misc.AIInterview.Admin.AiService.FinalizationWaitTimeoutMs"] = "Finalization Wait Timeout MS",
            ["Plugins.Misc.AIInterview.Admin.AiService.FinalizationWaitTimeoutMs.Hint"] = "Allowed range: 5000 to 45000 milliseconds.",
            ["Plugins.Misc.AIInterview.Admin.MockMode.Warning"] = "Development AI mode is enabled. Azure OpenAI is bypassed.",
            ["Plugins.Misc.AIInterview.Runtime.MockMode.Warning"] = "Development AI mode is enabled. Azure OpenAI is bypassed.",
            ["Plugins.Misc.AIInterview.Runtime.StopInterview"] = "Stop Interview",
            [FinalCompletionSpeechResourceKey] = DefaultFinalCompletionSpeech,
            ["Plugins.Misc.AIInterview.Runtime.Transcript"] = "Transcript",
            ["Plugins.Misc.AIInterview.Runtime.Log"] = "Runtime Log",
            ["Plugins.Misc.AIInterview.Runtime.CameraOff"] = "Camera Off",
            ["Plugins.Misc.AIInterview.Runtime.CameraOn"] = "Camera On",
            ["Plugins.Misc.AIInterview.Runtime.MicOn"] = "Mic On",
            ["Plugins.Misc.AIInterview.Runtime.RecordingReady"] = "Recording Ready",
            ["Plugins.Misc.AIInterview.Runtime.RecordingUnavailable"] = "Recording Unavailable",
            ["Plugins.Misc.AIInterview.Runtime.Guidelines.Title"] = "Interview Guidelines",
            ["Plugins.Misc.AIInterview.Runtime.StartInterview"] = "Start Interview",
            ["Plugins.Misc.AIInterview.Runtime.ScreenSharingOptional"] = "Screen sharing optional",
            ["Plugins.Misc.AIInterview.Runtime.ScreenSharingRequired"] = "Screen sharing required",
            ["Plugins.Misc.AIInterview.Runtime.ScreenSharingEnded"] = "Screen sharing ended",
            ["Plugins.Misc.AIInterview.Runtime.Conversations"] = "Conversations",
            ["Plugins.Misc.AIInterview.Runtime.WelcomePrompt"] = "Welcome! Click Start Interview to begin.",
            ["Plugins.Misc.AIInterview.Runtime.StartPrompt"] = "Click Start Interview to begin.",
            ["Plugins.Misc.AIInterview.Runtime.AnswerPlaceholder"] = "Enter your answer here...",
            ["Plugins.Misc.AIInterview.Runtime.ViewReport"] = "View Report",
            ["Plugins.Misc.AIInterview.Runtime.Close"] = "Close",
            ["Plugins.Misc.AIInterview.Runtime.Guidelines.Intro"] = "Please review these interview requirements before you begin.",
            ["Plugins.Misc.AIInterview.Runtime.Guidelines.Acknowledge"] = "I have read and agree to follow these interview guidelines.",
            ["Plugins.Misc.AIInterview.Runtime.IAgree"] = "I Agree",
            ["Plugins.Misc.AIInterview.Runtime.Cancel"] = "Cancel",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Trigger"] = "Solve & Report an Issue",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Title"] = "Solve & Report an Issue",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Intro"] = "Help us improve your experience by reporting any issues you encounter during the interview.",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.IssueLabel"] = "What issue are you experiencing? *",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Option.AiNotSpeaking"] = "AI is not speaking",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Option.TypingNotWorking"] = "Typing is not working",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Option.LoadingIssues"] = "Loading issues",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Option.ResultDelay"] = "Taking too much time for result generation",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Option.TalkToSupportTeam"] = "Talk to support team",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Option.OtherIssue"] = "Other issue",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.SolutionLabel"] = "Solution:",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Solution.AiNotSpeaking"] = "Ensure your device's sound is not muted and your volume is up. Try refreshing the page. If the issue continues, try a different browser.",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Solution.TypingNotWorking"] = "Make sure your keyboard is connected and working in other applications. Try refreshing the page.",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Solution.LoadingIssues"] = "Check your internet connection. Try refreshing the page or clearing your browser cache. If the issue persists, try a different browser or device.",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Solution.ResultDelay"] = "Result generation may take a few minutes. Please wait and avoid refreshing the page. If it takes too long, try again after some time.",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Support.CallSupport"] = "Call Support",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Support.AvailabilityNote"] = "Note: Support is available from 10:00 AM to 7:00 PM (Monday to Saturday).",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.WasHelpful"] = "Was this helpful?",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.CommentLabel"] = "Please describe your issue",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.CommentPlaceholder"] = "Please provide any additional details about the issue you're experiencing...",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.AttachmentLabel"] = "Attachments (Optional)",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.UploadText"] = "Click to upload images or documents",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.GotIt"] = "Got it",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.SubmitReport"] = "Submit Report",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.Success"] = "Thanks for the report. Your feedback has been submitted.",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.InvalidIssue"] = "Select a valid issue.",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.InvalidHelpfulness"] = "Select a valid helpfulness option.",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.CommentRequired"] = "Please describe your issue before submitting.",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.AttachmentOnlyOther"] = "Attachments are only available for Other issue reports.",
            ["Plugins.Misc.AIInterview.Runtime.Feedback.UploadUnavailable"] = "Attachment upload is unavailable."
        };
    }

    protected static Dictionary<string, string> GetEmployerApplicationsLocaleResources()
    {
        return new Dictionary<string, string>
        {
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Dashboard.Action.ReviewQueue"] = "Review queue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Dashboard.Action.ViewAnalysis"] = "View analysis",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Deactivate.Tooltip"] = "Deactivate invite",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Resume"] = "Resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.DownloadResume"] = "Download resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.NoResume"] = "No resume"
        };
    }

    protected static int NormalizeStrengthsSummaryMaxCompletionTokens(int maxCompletionTokens)
    {
        return Math.Clamp(
            maxCompletionTokens <= 0 ? AIInterviewDefaults.DefaultStrengthsSummaryMaxCompletionTokens : maxCompletionTokens,
            AIInterviewDefaults.MinStrengthsSummaryMaxCompletionTokens,
            AIInterviewDefaults.MaxStrengthsSummaryMaxCompletionTokens);
    }

    protected static int NormalizeQuestionPlanMaxCompletionTokens(int maxCompletionTokens) =>
        Math.Clamp(
            maxCompletionTokens <= 0 ? AIInterviewDefaults.DefaultQuestionPlanMaxCompletionTokens : maxCompletionTokens,
            AIInterviewDefaults.MinQuestionPlanMaxCompletionTokens,
            AIInterviewDefaults.MaxQuestionPlanMaxCompletionTokens);

    protected static int NormalizeQuestionPlanRetryMaxCompletionTokens(int maxCompletionTokens) =>
        Math.Clamp(
            maxCompletionTokens <= 0 ? AIInterviewDefaults.DefaultQuestionPlanRetryMaxCompletionTokens : maxCompletionTokens,
            AIInterviewDefaults.MinQuestionPlanRetryMaxCompletionTokens,
            AIInterviewDefaults.MaxQuestionPlanRetryMaxCompletionTokens);

    protected static int NormalizeRecordingUploadMaxMb(int maxMb)
    {
        return Math.Clamp(
            maxMb <= 0 ? AIInterviewDefaults.DefaultRecordingUploadMaxMb : maxMb,
            AIInterviewDefaults.MinRecordingUploadMaxMb,
            AIInterviewDefaults.MaxRecordingUploadMaxMb);
    }

    protected static int NormalizeRecordingVideoBitsPerSecond(int bitsPerSecond)
    {
        return Math.Clamp(
            bitsPerSecond <= 0 ? AIInterviewDefaults.DefaultRecordingVideoBitsPerSecond : bitsPerSecond,
            AIInterviewDefaults.MinRecordingVideoBitsPerSecond,
            AIInterviewDefaults.MaxRecordingVideoBitsPerSecond);
    }

    protected static int NormalizeRecordingAudioBitsPerSecond(int bitsPerSecond)
    {
        return Math.Clamp(
            bitsPerSecond <= 0 ? AIInterviewDefaults.DefaultRecordingAudioBitsPerSecond : bitsPerSecond,
            AIInterviewDefaults.MinRecordingAudioBitsPerSecond,
            AIInterviewDefaults.MaxRecordingAudioBitsPerSecond);
    }

    protected static string NormalizeRecordingSourceMode(string sourceMode)
    {
        var normalized = sourceMode?.Trim();
        var sourceModes = new[] { "ScreenPreferred", "CameraOnly", "ScreenOnly", "ScreenAndCamera" };
        return sourceModes.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? sourceModes.First(value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase))
            : AIInterviewDefaults.DefaultRecordingSourceMode;
    }

    protected static int NormalizeRecordingUploadTimeoutMs(int timeoutMs)
    {
        return Math.Clamp(
            timeoutMs <= 0 ? AIInterviewDefaults.DefaultRecordingUploadTimeoutMs : timeoutMs,
            AIInterviewDefaults.MinRecordingUploadTimeoutMs,
            AIInterviewDefaults.MaxRecordingUploadTimeoutMs);
    }

    protected static int NormalizeFinalizationWaitTimeoutMs(int timeoutMs, int recordingUploadTimeoutMs = 0)
    {
        var normalized = Math.Clamp(
            timeoutMs <= 0 ? AIInterviewDefaults.DefaultFinalizationWaitTimeoutMs : timeoutMs,
            AIInterviewDefaults.MinFinalizationWaitTimeoutMs,
            AIInterviewDefaults.MaxFinalizationWaitTimeoutMs);
        var normalizedUploadTimeoutMs = NormalizeRecordingUploadTimeoutMs(recordingUploadTimeoutMs);
        return Math.Max(normalized, normalizedUploadTimeoutMs + 5000);
    }

    public override async Task InstallAsync()
    {
        //settings
        var settings = new AIInterviewSettings
        {
            Enabled = true,
            ApiKey = string.Empty,
            MinimumScore = 0,
            TrackAzureOpenAiUsage = true,
            TrackAzureSpeechUsage = true,
            CalculateAzureCostPerInterview = true,
            EnableFinalScoringAtCompletion = true,
            MockInterviewQuestionCount = 5,
            AzureUsageCurrencyCode = "USD",
            SupportPhoneNumber = AIInterviewDefaults.DefaultSupportPhoneNumber,
            StrengthsSummaryMaxCompletionTokens = AIInterviewDefaults.DefaultStrengthsSummaryMaxCompletionTokens,
            QuestionPlanMaxCompletionTokens = AIInterviewDefaults.DefaultQuestionPlanMaxCompletionTokens,
            QuestionPlanRetryMaxCompletionTokens = AIInterviewDefaults.DefaultQuestionPlanRetryMaxCompletionTokens,
            RecordingUploadMaxMb = AIInterviewDefaults.DefaultRecordingUploadMaxMb,
            RecordingVideoBitsPerSecond = AIInterviewDefaults.DefaultRecordingVideoBitsPerSecond,
            RecordingAudioBitsPerSecond = AIInterviewDefaults.DefaultRecordingAudioBitsPerSecond,
            RecordingSourceMode = AIInterviewDefaults.DefaultRecordingSourceMode,
            RecordingUploadTimeoutMs = AIInterviewDefaults.DefaultRecordingUploadTimeoutMs,
            FinalizationWaitTimeoutMs = NormalizeFinalizationWaitTimeoutMs(AIInterviewDefaults.DefaultFinalizationWaitTimeoutMs, AIInterviewDefaults.DefaultRecordingUploadTimeoutMs),
            AzureDocumentIntelligenceModelId = AIInterviewDefaults.DefaultAzureDocumentIntelligenceModelId,
            AzureDocumentIntelligenceTimeoutSeconds = AIInterviewDefaults.DefaultAzureDocumentIntelligenceTimeoutSeconds,
            ResumeProfileExtractionSystemPrompt = AIInterviewDefaults.DefaultResumeProfileExtractionSystemPrompt,
            QuestionPlanSystemPrompt = AIInterviewDefaults.DefaultQuestionPlanSystemPrompt,
            QuestionPlanBuilderInstructionBlock = AIInterviewDefaults.DefaultQuestionPlanBuilderInstructionBlock,
            RuntimeQuestionGenerationSystemPrompt = AIInterviewDefaults.DefaultRuntimeQuestionGenerationSystemPrompt,
            RuntimeScoringSystemPrompt = AIInterviewDefaults.DefaultRuntimeScoringSystemPrompt,
            RuntimeScoringRetryAddendumPrompt = AIInterviewDefaults.DefaultRuntimeScoringRetryAddendumPrompt,
            FinalScoringSystemPrompt = AIInterviewDefaults.DefaultFinalScoringSystemPrompt,
            StrengthsSummarySystemPrompt = AIInterviewDefaults.DefaultStrengthsSummarySystemPrompt,
            StrengthsSummaryRetryStrictJsonSystemPrompt = AIInterviewDefaults.DefaultStrengthsSummaryRetryStrictJsonSystemPrompt,
            CreditProductSkuMappingsJson = AIInterviewDefaults.DefaultCreditProductSkuMappingsJson,
            CreditPurchasePageUrl = AIInterviewDefaults.DefaultCreditPurchasePageUrl
        };
        await _settingService.SaveSettingAsync(settings);

        var mockSettings = new MockAIInterviewSettings
        {
            UseMockResponses = false
        };
        await _settingService.SaveSettingAsync(mockSettings);

        await EnsureJobProductTemplateAsync();
        await EnsureFixedQuestionProductTemplateAsync();
        await EnsureMockPracticeProductTemplateAsync();
        await EnsurePricingCategoryTemplateAsync();
        await EnsureWidgetActiveAsync();
        await EnsureMessageTemplatesAsync();
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetEmployerApplicationsLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetAdminLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetMyActivityCreditLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetRuntimeTourLocaleResources());

        //locales
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            [$"{AIInterviewDefaults.LocalizationPrefix}.Enabled"] = "Enabled",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Enabled.Hint"] = "Enable this setting to use AI Interview features",
            [$"{AIInterviewDefaults.LocalizationPrefix}.ApiKey"] = "API Key",
            [$"{AIInterviewDefaults.LocalizationPrefix}.ApiKey.Hint"] = "Specify the API key for AI service.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.UseMockResponses"] = "Use AI Responses",
            [$"{AIInterviewDefaults.LocalizationPrefix}.UseMockResponses.Hint"] = "Development only. Enable to bypass Azure OpenAI and use AI responses.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MinimumScore"] = "Default minimum score fallback",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.JobTitle"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeFile"] = "Resume File",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.JobTitle.Required"] = "Job Title is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeFile.Required"] = "Resume required. Upload a resume or select a previous resume.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeFile.Invalid"] = "Allowed resume file types: PDF, DOCX. Maximum size: 5 MB.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeRequired"] = "Resume required",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.PreviousResume"] = "Use a previous resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.PreviousResume.Placeholder"] = "Select a previous resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.PreviousResume.Invalid"] = "Please select a valid previous resume.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.AlreadyApplied"] = "You have already applied for this job.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.InterviewRequired"] = "An interview is required before you can apply.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.MinimumScoreNotReached"] = "A minimum score of {0} is required to apply for this job. Please retake the AI interview to improve your chances.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.Success"] = "Your application has been submitted successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.Title"] = "Apply for a Position",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.Submit"] = "Submit Application",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.Title"] = "AI Interview Dashboard",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.ApplyNow"] = "Apply Now",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.ViewHistory"] = "View History",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.StartInterview.Title"] = "Start an AI Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.StartInterview.Description"] = "Practice your skills with our AI interviewer.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.StartInterview"] = "Start Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.Difficulty"] = "Difficulty",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Difficulty.Easy"] = "Easy",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Difficulty.Medium"] = "Medium",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Difficulty.Hard"] = "Hard",

            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Title"] = "Your Interview History",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.MockTitle"] = "AI Interview History",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Title"] = "My Activity",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.NavigationLabel"] = "My Activity",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Tab.AppliedJobs"] = "Applied Jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Tab.SavedJobs"] = "Saved Jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Tab.MockInterviews"] = "AI Interviews",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Loading"] = "Loading activity...",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.SavedJobs.Empty"] = "Saved jobs will appear here when you bookmark roles.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.MockPracticeLabel"] = "AI Practice",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.PracticeReportTitle"] = "Practice report - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.PracticeRecordingTitle"] = "Practice recording - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.OpenPracticeReport"] = "Open practice report - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.OpenPracticeRecording"] = "Open practice recording - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Date"] = "Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Status"] = "Status",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Score"] = "Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Actions"] = "Actions",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.ViewReport"] = "View Report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.NoSessions"] = "You have no interview sessions yet.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Sort.LatestApplied"] = "Latest Applied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Sort.OldestApplied"] = "Oldest Applied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Sort.HighestScore"] = "Highest Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Sort.LowestScore"] = "Lowest Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Sort.LatestInterviewDate"] = "Latest Interview Date",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Title"] = "Interview Report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Date"] = "Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Score"] = "Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Details"] = "Report Details",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.NotFound"] = "Report not found or access denied.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Questions"] = "Questions",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Recording"] = "Recording",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.OpenRecording"] = "Open recording",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AverageTechnicalScore"] = "Average Technical Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AverageCommunication"] = "Average Communication",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AverageProfessionalism"] = "Average Professionalism",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AveragePositiveAttitude"] = "Average Positive Attitude",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.QuestionScores"] = "Question Scores",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Question"] = "Question",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.InterviewTurns"] = "Interview Turns",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Answer"] = "Answer",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Feedback"] = "Feedback",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.TechnicalScore"] = "Technical Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Communication"] = "Communication",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Professionalism"] = "Professionalism",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.PositiveAttitude"] = "Positive Attitude",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Pending"] = "(pending)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Asked"] = "Asked",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Answered"] = "Answered",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.CopyShareLink"] = "Copy share link",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Share"] = "Share",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.OpenSharePage"] = "Open share page",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.ViewReport"] = "View Report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Back"] = "Back",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.DrawerTitle"] = "Interview report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.ClosePanel"] = "Close report panel",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Loading"] = "Loading report...",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.LoadFailed"] = "Failed to load report.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Copied"] = "Copied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.CopyFailed"] = "Copy failed",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.RecordingShareTitle"] = "Interview recording",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.JobReportTitle"] = "{0} report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.JobRecordingTitle"] = "{0} recording",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.LinkCopied"] = "Link copied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.ShareTitle"] = "Interview Report - Skillfinder",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.BrandTagline"] = "AI-Powered Interview Platform",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.BrandCta"] = "Explore Skillfinder hiring CTA",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.TypeMock"] = "Practice Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.TypeJob"] = "Job Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.ContextResumeBased"] = "Evaluated against uploaded resume",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.Title"] = "AI Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.Difficulty"] = "Difficulty",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.InitialQuestion"] = "Hello! Please introduce yourself and tell me about your background.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.AnswerPlaceholder"] = "Type your answer here...",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.SubmitAnswer"] = "Submit Answer",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.StopInterview"] = "Finish Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.CompletedScore"] = "Interview completed! Your score is",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.Unauthorized"] = "Unauthorized runtime request.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.InvalidToken"] = "Invalid or expired session token.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.InvalidAnswer"] = "Answer cannot be empty.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.TokenServiceFailure"] = "Token service failure.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.NextQuestionMock"] = "Next AI question?",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.ReportContentMock"] = "AI report content",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.StopInterview"] = "Stop Interview",
            [FinalCompletionSpeechResourceKey] = DefaultFinalCompletionSpeech,
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Transcript"] = "Transcript",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Log"] = "Runtime Log",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.CameraOff"] = "Camera Off",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.CameraOn"] = "Camera On",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.MicOn"] = "Mic On",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.RecordingReady"] = "Recording Ready",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.RecordingUnavailable"] = "Recording Unavailable",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Guidelines.Title"] = "Interview Guidelines",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.StartInterview"] = "Start Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.ScreenSharingOptional"] = "Screen sharing optional",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.ScreenSharingRequired"] = "Screen sharing required",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.ScreenSharingEnded"] = "Screen sharing ended",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Conversations"] = "Conversations",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.WelcomePrompt"] = "Welcome! Click Start Interview to begin.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.StartPrompt"] = "Click Start Interview to begin.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.AnswerPlaceholder"] = "Enter your answer here...",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.ViewReport"] = "View Report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Close"] = "Close",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Guidelines.Intro"] = "Please review these interview requirements before you begin.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Guidelines.Acknowledge"] = "I have read and agree to follow these interview guidelines.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.IAgree"] = "I Agree",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Cancel"] = "Cancel",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Trigger"] = "Solve & Report an Issue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Title"] = "Solve & Report an Issue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Intro"] = "Help us improve your experience by reporting any issues you encounter during the interview.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.IssueLabel"] = "What issue are you experiencing? *",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Option.AiNotSpeaking"] = "AI is not speaking",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Option.TypingNotWorking"] = "Typing is not working",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Option.LoadingIssues"] = "Loading issues",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Option.ResultDelay"] = "Taking too much time for result generation",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Option.TalkToSupportTeam"] = "Talk to support team",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Option.OtherIssue"] = "Other issue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.SolutionLabel"] = "Solution:",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Solution.AiNotSpeaking"] = "Ensure your device's sound is not muted and your volume is up. Try refreshing the page. If the issue continues, try a different browser.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Solution.TypingNotWorking"] = "Make sure your keyboard is connected and working in other applications. Try refreshing the page.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Solution.LoadingIssues"] = "Check your internet connection. Try refreshing the page or clearing your browser cache. If the issue persists, try a different browser or device.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Solution.ResultDelay"] = "Result generation may take a few minutes. Please wait and avoid refreshing the page. If it takes too long, try again after some time.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Support.CallSupport"] = "Call Support",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Support.AvailabilityNote"] = "Note: Support is available from 10:00 AM to 7:00 PM (Monday to Saturday).",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.WasHelpful"] = "Was this helpful?",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.CommentLabel"] = "Please describe your issue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.CommentPlaceholder"] = "Please provide any additional details about the issue you're experiencing...",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.AttachmentLabel"] = "Attachments (Optional)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.UploadText"] = "Click to upload images or documents",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.GotIt"] = "Got it",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.SubmitReport"] = "Submit Report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.Success"] = "Thanks for the report. Your feedback has been submitted.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.InvalidIssue"] = "Select a valid issue.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.InvalidHelpfulness"] = "Select a valid helpfulness option.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.CommentRequired"] = "Please describe your issue before submitting.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.AttachmentOnlyOther"] = "Attachments are only available for Other issue reports.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Feedback.UploadUnavailable"] = "Attachment upload is unavailable.",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Provider"] = "AI Provider",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Model"] = "AI Model",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Prompt"] = "System Prompt",
            [$"{AIInterviewDefaults.LocalizationPrefix}.ServiceSettings"] = "Service Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.CreditPackAmount"] = "Credit Pack Amount",
            [$"{AIInterviewDefaults.LocalizationPrefix}.CreditPackPrice"] = "Credit Pack Price",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.CreditPurchasePageUrl"] = "Credit Purchase Page URL",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.CreditPurchasePageUrl.Hint"] = "Relative or absolute URL used by the job page when the user has no credits. The default is /pricing.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.SupportPhoneNumber"] = "Support Phone Number",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.SupportPhoneNumber.Hint"] = "Phone number shown to candidates when they select Talk to support team during an interview.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.AzureDocumentIntelligenceEndpointUrl"] = "Azure Document Intelligence Endpoint URL",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.AzureDocumentIntelligenceApiKey"] = "Azure Document Intelligence API Key",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.AzureDocumentIntelligenceModelId"] = "Azure Document Intelligence Model ID",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.AzureDocumentIntelligenceTimeoutSeconds"] = "Azure Document Intelligence Timeout Seconds",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.AzureDocumentIntelligenceEndpointUrl.Hint"] = "Endpoint for the Azure AI Document Intelligence resource used to read candidate resumes.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.AzureDocumentIntelligenceApiKey.Hint"] = "Used server-side only for resume text extraction. Leave blank to keep the existing key.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.AzureDocumentIntelligenceModelId.Hint"] = "Use prebuilt-read unless Azure support instructs otherwise.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.AzureDocumentIntelligenceTimeoutSeconds.Hint"] = "Maximum time to wait for resume reading before returning an extraction failure.",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.TopUp.InvalidAmount"] = "Invalid top-up amount.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.TopUp.Success"] = "Credits topped up successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.TopUp.Remarks"] = "Admin top-up",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Configure.Title"] = "AI Interview Configuration",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Configure.General"] = "General Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Configure.Service"] = "AI Service Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Configure.CreditPack"] = "Credit Pack Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Configure.MockInterviewQuestionCount"] = "AI Interview Question count",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Configure.MockInterviewQuestionCount.Hint"] = "Applies to mock interviews only. Allowed range: 1 to 10. Default is 5.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.Success"] = "Sponsor invite created successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.EmailRequired"] = "Email is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.ProductNotFound"] = "Product not found.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.InvalidOwnership"] = "Product is not owned by the sponsor.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.InvalidAttempts"] = "Invalid max attempts.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.InvalidExpiry"] = "Invalid expiry date.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.UnexpectedError"] = "An unexpected error occurred.",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Title"] = "Employer Interview Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Credits.Balance"] = "Credit Balance",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.CreateNew"] = "Create New Invite",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.CreateTitle"] = "Create Employer Invite",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Email"] = "Email",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.ProductId"] = "Product ID",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.MaxAttempts"] = "Max Attempts",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.ExpiryDate"] = "Expiry Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Create"] = "Create Invite",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.ExistingInvites"] = "Existing Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.ActiveTitle"] = "Active Employer Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Code"] = "Code",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Link"] = "Link",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Status"] = "Status",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Actions"] = "Actions",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.CopyLink"] = "Copy Link",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Accepted"] = "Used/Accepted",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Expired"] = "Expired",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Active"] = "Active",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Inactive"] = "Inactive",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Deactivate"] = "Deactivate",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Deactivate.Tooltip"] = "Deactivate invite",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.NoInvites"] = "No invites found.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Success"] = "Invite created successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Error"] = "Error creating invite.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Deactivated"] = "Invite deactivated successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.CreditProductSkuMappingsJson.Invalid"] = "The credit product SKU mappings JSON is invalid. Use a JSON object such as {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10}.",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Title"] = "Applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Candidate"] = "Candidate",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Status"] = "Status",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.MinScore"] = "Minimum score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.MaxScore"] = "Maximum score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Filter"] = "Apply filters",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.FilterTitle"] = "Filter applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.FilterSubtitle"] = "Refine candidates by status, score, and application date.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.UpdateStatus.Success"] = "Application status updated successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ID"] = "ID",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Email"] = "Email",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.JobTitle"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ChargeMode"] = "Charge Mode",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Attempts"] = "Attempts",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.PromptSource"] = "Prompt Source",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.InterviewSort"] = "Sort by",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.TopScorersFirst"] = "Top scorers first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.LowestScorersFirst"] = "Lowest scorers first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.LatestApplied"] = "Latest applied first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedFromUtc"] = "Applied from",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedToUtc"] = "Applied to",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.OnlyWithInterviewScore"] = "Only candidates with interview score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ResultsSummary"] = "Showing {0} filtered result(s)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.PageSize"] = "Page size",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Reset"] = "Reset",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.All"] = "All",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Phone"] = "Phone",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.InterviewScore"] = "Interview score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedOn"] = "Applied on",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Resume"] = "Resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.DownloadResume"] = "Download resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.NoResume"] = "No resume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.CoverMessage"] = "Cover message",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Resume.NotFound"] = "Resume file was not found.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Title"] = "Employer Interview Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.CreateTitle"] = "Create Employer Invite",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.ActiveTitle"] = "Active Employer Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.BulkSuccess"] = "Successfully created {0} invites. {1} emails were invalid.",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.Completed"] = "Completed",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.InProgress"] = "In Progress",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.Started"] = "Started",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.Applied"] = "Applied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.Reviewed"] = "Reviewed",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.Shortlisted"] = "Shortlisted",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.Rejected"] = "Rejected",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.Withdrawn"] = "Withdrawn",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Common.Unknown"] = "Unknown",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Common.None"] = "N/A",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Common.Interview"] = "Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.Welcome"] = "Welcome to AI Interview.",
            [AIInterviewDefaults.HomepageTopPerformersTitleResourceKey] = "Top Performers",
            [AIInterviewDefaults.HomepageTopPerformersScoreResourceKey] = "Best score",
            [AIInterviewDefaults.HomepageTopPerformersFallbackSkillResourceKey] = "Not specified",
            [AIInterviewDefaults.HomepageTopPerformersAvatarAltResourceKey] = "Default candidate avatar",
            [AIInterviewDefaults.HomepageTopPerformersPreviousResourceKey] = "Previous performers",
            [AIInterviewDefaults.HomepageTopPerformersNextResourceKey] = "Next performers",
            [AIInterviewDefaults.HomepageTopPerformersEmptyResourceKey] = "Top performers will appear here after completed interviews are evaluated.",
            [AIInterviewDefaults.HomepageTopPerformersUnknownCandidateResourceKey] = "Unknown candidate",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.Title"] = "My Applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Title"] = "My Activity",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.NavigationLabel"] = "My Activity",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Tab.AppliedJobs"] = "Applied Jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Tab.SavedJobs"] = "Saved Jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Tab.MockInterviews"] = "AI Interviews",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.Loading"] = "Loading activity...",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyActivity.SavedJobs.Empty"] = "Saved jobs will appear here when you bookmark roles.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.MockPracticeLabel"] = "AI Practice",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.PracticeReportTitle"] = "Practice report - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.PracticeRecordingTitle"] = "Practice recording - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.OpenPracticeReport"] = "Open practice report - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.History.OpenPracticeRecording"] = "Open practice recording - {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.JobTitle"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.AppliedDate"] = "Applied Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.AttemptCount"] = "Attempt Count",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.LatestScoreDate"] = "Latest Score Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.Score"] = "Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.NoCredits"] = "Insufficient credits. Please purchase credits to start the interview.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.NoCredits.Link"] = "View Pricing",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.ExpiredLink"] = "Your previous interview link expired. Start the interview again from this page.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.Unavailable"] = "The interview service is temporarily unavailable. Please try again.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MockPractice.DifficultyRequired"] = "Please select a practice difficulty.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MockPractice.SkillOrResumeRequired"] = "Select a practice skill or provide a resume to start the practice interview.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MockPractice.SelectionRequired"] = "We couldn't start your AI interview. Please select a difficulty level, a skill, or upload your resume before continuing.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MockPractice.StartValidationFailed"] = "Please review the practice inputs and try again.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.NextQuestion"] = "Next question ready.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.SponsorMessage"] = "This interview is company-sponsored.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Title"] = "Employer Scoreboard",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Eyebrow"] = "Recruitment Analytics Desk",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Intro"] = "Review assessment outcomes, verify interview completion, and move hiring decisions forward from one candidate metrics workspace.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CreateJobAction"] = "Create Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ReviewApplicationsAction"] = "Review Applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ManageInvitesAction"] = "Manage Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.TotalCompletedAssessments"] = "Total Completed Assessments",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.TotalSubmissions"] = "{0} total candidate submissions tracked",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.AverageAnalyticalScore"] = "Average Analytical Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.HighestRecordedScore"] = "Highest recorded score {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ActiveFlaggedViolations"] = "Active Flagged Violations",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ShortlistedDetail"] = "{0} shortlisted for next-step review",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.OpenJobModules"] = "Open Job Modules",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CandidateVolume"] = "Candidate Volume",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ShortlistQueue"] = "Shortlist Queue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CandidateAssessmentMatrix"] = "Candidate Assessment Matrix",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CandidateName"] = "Candidate Name",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.AssessmentModule"] = "Assessment Module",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CompletionDate"] = "Completion Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.CoreAiRating"] = "Core AI Rating",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.VerificationStatus"] = "Verification Status",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Actions"] = "Actions",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.AssessmentWorkflow"] = "Assessment workflow",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.InterviewCompleted"] = "Interview completed",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ApplicationReceived"] = "Application received",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ViewAnalysis"] = "View Analysis",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.ReviewQueue"] = "Review Queue",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.NoAssessments"] = "No candidate assessments have been submitted yet.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Verification.Verified"] = "Verified",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Verification.Flagged"] = "Flagged",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Verification.Evaluating"] = "Evaluating",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.Kicker"] = "AI Interview Role",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.WorkArrangement"] = "Work arrangement",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.EmploymentType"] = "Employment type",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.JobLocation"] = "Job location",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.SalaryRange"] = "Salary range",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.ExperienceLevel"] = "Experience level",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.Posted"] = "Posted {0}",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.AppliedCount"] = "{0} applied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.ViewJob"] = "View job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.SaveJob"] = "Save job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.RemoveSavedJob"] = "Remove from saved jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.SavedToSavedJobs"] = "Saved to saved jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.RemovedFromSavedJobs"] = "Removed from saved jobs",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.JobPreview"] = "Job preview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.CloseJobPreview"] = "Close job preview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.LoadingJobDetails"] = "Loading job details...",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.UnableToLoadJobDetails"] = "Unable to load job details.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.SavedJobsUnavailable"] = "Saved jobs are temporarily unavailable.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.JobNotFound"] = "The selected job could not be found.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobCard.InvalidJob"] = "The selected product is not an AI interview job.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.Kicker"] = "AI Interview Role",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.HiringCompany"] = "Hiring Company",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.CandidatesApplied"] = "{0} applied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.ViewJob"] = "View job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.EmailAFriend"] = "Email a friend",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.SaveJob"] = "Save job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.SavedJob"] = "Saved job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.SaveToCustomWishlist"] = "Save to custom wishlist",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.SaveFirstForWishlist"] = "Save the job first, then choose a custom wishlist.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.JobDescription"] = "Job description",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.RoleHighlights"] = "Role highlights",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.RoleHighlightsFallback"] = "Key responsibilities and outcomes will be discussed during the interview process.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.Skills"] = "Skills",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.SkillsFallback"] = "Skills will be evaluated during the AI interview.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.JobDetails.JobDetails"] = "Job details",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Title"] = "Create a Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ShortDescription"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.FullDescription"] = "Job Description",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.JobLocation.Invalid"] = "Select a valid job location.",
        });
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetUpgradeLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetAdminLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetMyActivityCreditLocaleResources());
        await EnsureRuntimeActivityLogTypesAsync();

        await EnsureCompletionRecoveryTaskAsync();

        await EnsureEmployerRoleAsync();
        await EnsureInstituteRoleAsync();
        await base.InstallAsync();
    }

    private async Task EnsureCompletionRecoveryTaskAsync()
    {
        if (_scheduleTaskService == null)
            return;

        var completionTasks = (await _scheduleTaskService.GetAllTasksAsync(showHidden: true) ?? new List<ScheduleTask>())
            .Where(task =>
                string.Equals(task.Type, AIInterviewDefaults.CompletionRecoveryTaskType, StringComparison.Ordinal) ||
                string.Equals(task.Type, AIInterviewDefaults.LegacyCompletionRecoveryTaskType, StringComparison.Ordinal))
            .OrderBy(task => task.Id)
            .ToList();
        var retainedTask = completionTasks
            .FirstOrDefault(task => string.Equals(task.Type, AIInterviewDefaults.CompletionRecoveryTaskType, StringComparison.Ordinal)) ??
            completionTasks.FirstOrDefault();

        if (retainedTask != null)
        {
            if (string.Equals(retainedTask.Type, AIInterviewDefaults.LegacyCompletionRecoveryTaskType, StringComparison.Ordinal))
            {
                retainedTask.Type = AIInterviewDefaults.CompletionRecoveryTaskType;
                await _scheduleTaskService.UpdateTaskAsync(retainedTask);
            }

            foreach (var duplicateTask in completionTasks.Where(task => !ReferenceEquals(task, retainedTask)))
                await _scheduleTaskService.DeleteTaskAsync(duplicateTask);

            return;
        }

        await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
        {
            Enabled = true,
            StopOnError = false,
            LastEnabledUtc = DateTime.UtcNow,
            Seconds = AIInterviewDefaults.CompletionRecoveryTaskPeriodSeconds,
            Name = AIInterviewDefaults.CompletionRecoveryTaskName,
            Type = AIInterviewDefaults.CompletionRecoveryTaskType
        });
    }

    private async Task DeleteCompletionRecoveryTasksAsync()
    {
        if (_scheduleTaskService == null)
            return;

        var completionTasks = (await _scheduleTaskService.GetAllTasksAsync(showHidden: true) ?? new List<ScheduleTask>())
            .Where(task =>
                string.Equals(task.Type, AIInterviewDefaults.CompletionRecoveryTaskType, StringComparison.Ordinal) ||
                string.Equals(task.Type, AIInterviewDefaults.LegacyCompletionRecoveryTaskType, StringComparison.Ordinal))
            .ToList();

        foreach (var completionTask in completionTasks)
            await _scheduleTaskService.DeleteTaskAsync(completionTask);
    }

    /// <summary>
    /// Uninstall plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        //settings
        await _settingService.DeleteSettingAsync<AIInterviewSettings>();
        await _settingService.DeleteSettingAsync<MockAIInterviewSettings>();

        if (_widgetSettings != null && _widgetSettings.ActiveWidgetSystemNames.RemoveAll(systemName =>
            string.Equals(systemName, AIInterviewDefaults.SystemName, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        //locales
        await _localizationService.DeleteLocaleResourcesAsync(AIInterviewDefaults.LocalizationPrefix);
        await DeleteRuntimeActivityLogTypesAsync();

        await DeleteCompletionRecoveryTasksAsync();

        await base.UninstallAsync();
    }

    #endregion
}

internal static class AIInterviewRoleHelper
{
    public static async Task<bool> IsInRoleAsync(ICustomerService customerService, Customer customer, string roleSystemName)
    {
        if (customerService == null || customer == null || string.IsNullOrWhiteSpace(roleSystemName))
            return false;

        if (await customerService.IsInCustomerRoleAsync(customer, roleSystemName, true))
            return true;

        var activeRoles = await customerService.GetCustomerRolesAsync(customer);
        return activeRoles?.Any(role =>
            string.Equals(role.SystemName?.Trim(), roleSystemName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role.Name?.Trim(), roleSystemName, StringComparison.OrdinalIgnoreCase)) == true;
    }
}
