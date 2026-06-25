using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Cms;
using Nop.Services.Catalog;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Plugins;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Misc.AIInterview;

/// <summary>
/// Represents AI Interview plugin
/// </summary>
public class AIInterviewPlugin : BasePlugin, IMiscPlugin, IWidgetPlugin
{
    #region Fields

    private readonly ILocalizationService _localizationService;
    private readonly ISettingService _settingService;
    private readonly IWebHelper _webHelper;
    private readonly IMessageTemplateService _messageTemplateService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly WidgetSettings _widgetSettings;

    #endregion

    #region Ctor

    public AIInterviewPlugin(ILocalizationService localizationService,
        ISettingService settingService,
        IWebHelper webHelper,
        IMessageTemplateService messageTemplateService,
        IProductTemplateService productTemplateService = null,
        WidgetSettings widgetSettings = null)
    {
        _localizationService = localizationService;
        _settingService = settingService;
        _webHelper = webHelper;
        _messageTemplateService = messageTemplateService;
        _productTemplateService = productTemplateService;
        _widgetSettings = widgetSettings;
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
            "productdetails_before_collateral",
            AdminWidgetZones.ProductDetailsBlock
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

        return typeof(Components.AIInterviewProductDetailsViewComponent);
    }

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}Admin/AIInterview/Configure";
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

        if (string.IsNullOrWhiteSpace(settings.CreditProductSkuMappingsJson))
            settings.CreditProductSkuMappingsJson = AIInterviewDefaults.DefaultCreditProductSkuMappingsJson;

        if (string.IsNullOrWhiteSpace(settings.CreditPurchasePageUrl))
            settings.CreditPurchasePageUrl = AIInterviewDefaults.DefaultCreditPurchasePageUrl;

        await _settingService.SaveSettingAsync(settings);
        await EnsureJobProductTemplateAsync();
        await EnsureWidgetActiveAsync();
        await EnsureMessageTemplatesAsync();
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetUpgradeLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetAdminLocaleResources());

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
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeFile.Help"] = "Upload a PDF or DOCX file up to 5 MB, or leave blank to reuse your most recent resume.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.SortBy"] = "Sort by",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.AccessDenied"] = "You do not have access to this interview report.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.Title"] = "Interview Session Error",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.StartAgain"] = "Start Again",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.ExpiredLink"] = "Your previous interview link expired. Start the interview again from this page.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.Unavailable"] = "The interview service is temporarily unavailable. Please try again.",
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
            [$"{AIInterviewDefaults.LocalizationPrefix}.Common.CompletedUtc"] = "Completed (UTC)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Common.Available"] = "Available",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Common.Interview"] = "Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Status.Active"] = "Active",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.Attempt"] = "Attempt",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.ApplicationsCountLabel"] = "application(s)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.Applied"] = "Applied",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.HistoryFootnote"] = "Customer-side history includes overall results and per-question AI evaluation details.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.ApplyPanel.Description"] = "Apply for this role and start the mock interview directly from this page. Interview difficulty is handled automatically.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.SignInPrompt"] = "Sign in to apply and start the interview for this role.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.NextQuestion"] = "Next question ready.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl"] = "Azure Blob Storage Container URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken"] = "Azure Blob Storage SAS Token",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl.Hint"] = "Used for server-side recording uploads and other media persistence.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken.Hint"] = "Paste the SAS token string exactly as issued. It is stored only in admin settings.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.EmailInvalid"] = "Enter a valid email address.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Status"] = "Status",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.StatusComment"] = "Status comment",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Update"] = "Update",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ExportCsv"] = "Export to CSV",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.UpdateStatus.Invalid"] = "Select a valid application status.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.JobTitle"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ChargeMode"] = "Charge Mode",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Attempts"] = "Attempts",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.PromptSource"] = "Prompt Source",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.InterviewSort"] = "Interview sorting",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.TopScorersFirst"] = "Top scorers first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.LowestScorersFirst"] = "Lowest scorers first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.LatestApplied"] = "Latest applied first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedFromUtc"] = "Applied from (UTC)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedToUtc"] = "Applied to (UTC)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.OnlyWithInterviewScore"] = "Only candidates with interview score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ResultsSummary"] = "Showing {0} filtered result(s)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.InterviewScore"] = "Interview score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedOn"] = "Applied on",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.CoverMessage"] = "Cover message",
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
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Title"] = "Vendor Scoreboard",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Name"] = "Job title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Name.Required"] = "Job title is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ShortDescription"] = "Summary",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.FullDescription"] = "Description",
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
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Settings"] = "Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Select"] = "Select",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ApplyUntilUtc.Past"] = "Apply until date cannot be in the past.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.MinimumScore.Range"] = "Minimum score must be between 0 and 100.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.QuestionCount.Range"] = "Question count must be between 1 and 10 when interview is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.ExperienceLevel.Invalid"] = "Select a valid experience level.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.WorkMode.Invalid"] = "Select a valid work mode.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.EmploymentType.Invalid"] = "Select a valid employment type.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.JobLocation.Unsupported"] = "Job location metadata is not configured. Configure the related specification attribute before creating the job.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.SalaryRange.Unsupported"] = "Salary range metadata is not configured. Configure the related specification attribute before creating the job.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Submit"] = "Create Job",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Success"] = "The job was created successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Unavailable"] = "Job creation is temporarily unavailable.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Title"] = "Create a Job"
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
            ["Plugins.Misc.AIInterview.Admin.Configure.Title"] = "AI Interview Configuration",
            ["Plugins.Misc.AIInterview.Admin.Configure.General"] = "General Settings",
            ["Plugins.Misc.AIInterview.Admin.Configure.Service"] = "AI Service Settings",
            ["Plugins.Misc.AIInterview.Admin.Configure.CreditPack"] = "Credit Pack Settings",
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
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiApiKey"] = "Azure OpenAI API Key",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiDeploymentOrModel"] = "Azure OpenAI Deployment / Model",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechKey"] = "Azure Speech Key",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechRegion"] = "Azure Speech Region",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl"] = "Azure Blob Storage Container URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken"] = "Azure Blob Storage SAS Token",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl.Hint"] = "Used for server-side recording uploads and other media persistence.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken.Hint"] = "Paste the SAS token string exactly as issued. It is stored only in admin settings.",
            ["Plugins.Misc.AIInterview.Admin.AiService.CreditProductSkuMappingsJson"] = "Credit Product SKU Mappings (JSON)",
            ["Plugins.Misc.AIInterview.Admin.AiService.CreditProductSkuMappingsJson.Hint"] = "Map product SKUs to credits granted per unit. Example: {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10,\"AI-CREDIT-20\":20}. Create normal Pricing-category products with those SKUs and prices. Credits are granted only after successful payment for registered customers.",
            ["Plugins.Misc.AIInterview.Admin.AiService.CreditProductSkuMappingsJson.Invalid"] = "The credit product SKU mappings JSON is invalid. Use a JSON object such as {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10}.",
            ["Plugins.Misc.AIInterview.Admin.AiService.CreditPurchasePageUrl"] = "Credit Purchase Page URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.CreditPurchasePageUrl.Hint"] = "Relative or absolute URL used by the job page when the user has no credits. The default is /pricing.",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.Title"] = "Sponsor Invite Management",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.Create"] = "Create Invites",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.List"] = "Existing Invites",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.Email"] = "Email",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.Status"] = "Status",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.BulkEmails"] = "Bulk Emails",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.ProductId"] = "Product ID",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.MaxAttempts"] = "Max Attempts",
            ["Plugins.Misc.AIInterview.Admin.SponsorInvites.ExpiryDateUtc"] = "Expiry Date (UTC)",
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
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.LastCreditActivityUtc"] = "Last Credit Activity UTC",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.ViewLedger"] = "View Ledger",
            ["Plugins.Misc.AIInterview.Admin.Credits.Activity.Empty"] = "No applicant credit activity found.",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Empty"] = "No ledger entries found for the selected applicant.",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Title"] = "Ledger",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Customer"] = "Customer",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Amount"] = "Amount",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Type"] = "Type",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Remarks"] = "Remarks",
            ["Plugins.Misc.AIInterview.Admin.Credits.Ledger.Utc"] = "UTC",
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
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl"] = "Azure Blob Storage Container URL",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken"] = "Azure Blob Storage SAS Token",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl.Hint"] = "Used for server-side recording uploads and other media persistence.",
            ["Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken.Hint"] = "Paste the SAS token string exactly as issued. It is stored only in admin settings.",
            ["Plugins.Misc.AIInterview.Admin.MockMode.Warning"] = "Development mock mode is enabled. Azure OpenAI is bypassed.",
            ["Plugins.Misc.AIInterview.Runtime.MockMode.Warning"] = "Development mock mode is enabled. Azure OpenAI is bypassed.",
            ["Plugins.Misc.AIInterview.Runtime.StopInterview"] = "Stop Interview",
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
            ["Plugins.Misc.AIInterview.Runtime.Cancel"] = "Cancel"
        };
    }

    public override async Task InstallAsync()
    {
        //settings
        var settings = new AIInterviewSettings
        {
            Enabled = true,
            ApiKey = string.Empty,
            MinimumScore = 0,
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
        await EnsureWidgetActiveAsync();
        await EnsureMessageTemplatesAsync();
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetAdminLocaleResources());

        //locales
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            [$"{AIInterviewDefaults.LocalizationPrefix}.Enabled"] = "Enabled",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Enabled.Hint"] = "Enable this setting to use AI Interview features",
            [$"{AIInterviewDefaults.LocalizationPrefix}.ApiKey"] = "API Key",
            [$"{AIInterviewDefaults.LocalizationPrefix}.ApiKey.Hint"] = "Specify the API key for AI service.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.UseMockResponses"] = "Use Mock Responses",
            [$"{AIInterviewDefaults.LocalizationPrefix}.UseMockResponses.Hint"] = "Development only. Enable to bypass Azure OpenAI and use mock responses.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MinimumScore"] = "Default minimum score fallback",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.JobTitle"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeFile"] = "Resume File",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.JobTitle.Required"] = "Job Title is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeFile.Required"] = "Resume file is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeFile.Invalid"] = "Allowed resume file types: PDF, DOCX. Maximum size: 5 MB.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.AlreadyApplied"] = "You have already applied for this job.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.InterviewRequired"] = "An interview is required before you can apply.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.MinimumScoreNotReached"] = "A minimum score of {0} is required to apply for this job. Please retake the AI interview to improve your chances.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.Success"] = "Your application has been submitted successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.Title"] = "Apply for a Position",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.Submit"] = "Submit Application",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.Title"] = "AI Interview Dashboard",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.ApplyNow"] = "Apply Now",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.ViewHistory"] = "View History",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.StartInterview.Title"] = "Start a Mock Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.StartInterview.Description"] = "Practice your skills with our AI interviewer.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.StartInterview"] = "Start Interview",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Index.Difficulty"] = "Difficulty",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Difficulty.Easy"] = "Easy",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Difficulty.Medium"] = "Medium",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Difficulty.Hard"] = "Hard",

            [$"{AIInterviewDefaults.LocalizationPrefix}.History.Title"] = "Your Interview History",
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

            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.Title"] = "Mock AI Interview",
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
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.NextQuestionMock"] = "Next mock question?",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.ReportContentMock"] = "Mock report content",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.StopInterview"] = "Stop Interview",
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

            [$"{AIInterviewDefaults.LocalizationPrefix}.Provider"] = "AI Provider",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Model"] = "AI Model",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Prompt"] = "System Prompt",
            [$"{AIInterviewDefaults.LocalizationPrefix}.ServiceSettings"] = "Service Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.CreditPackAmount"] = "Credit Pack Amount",
            [$"{AIInterviewDefaults.LocalizationPrefix}.CreditPackPrice"] = "Credit Pack Price",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.CreditPurchasePageUrl"] = "Credit Purchase Page URL",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.CreditPurchasePageUrl.Hint"] = "Relative or absolute URL used by the job page when the user has no credits. The default is /pricing.",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.TopUp.InvalidAmount"] = "Invalid top-up amount.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.TopUp.Success"] = "Credits topped up successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.TopUp.Remarks"] = "Admin top-up",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Configure.Title"] = "AI Interview Configuration",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Configure.General"] = "General Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Configure.Service"] = "AI Service Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Configure.CreditPack"] = "Credit Pack Settings",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.Success"] = "Sponsor invite created successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.EmailRequired"] = "Email is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.ProductNotFound"] = "Product not found.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.InvalidOwnership"] = "Product is not owned by the sponsor.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.InvalidAttempts"] = "Invalid max attempts.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.InvalidExpiry"] = "Invalid expiry date.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.Invite.UnexpectedError"] = "An unexpected error occurred.",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Title"] = "Sponsor Invites",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Credits.Balance"] = "Credit Balance",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.CreateNew"] = "Create New Invite",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Email"] = "Email",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.ProductId"] = "Product ID",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.MaxAttempts"] = "Max Attempts",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.ExpiryDate"] = "Expiry Date (UTC)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Create"] = "Create Invite",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.ExistingInvites"] = "Existing Invites",
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
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.NoInvites"] = "No invites found.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Success"] = "Invite created successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Error"] = "Error creating invite.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Invite.Deactivated"] = "Invite deactivated successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Admin.AiService.CreditProductSkuMappingsJson.Invalid"] = "The credit product SKU mappings JSON is invalid. Use a JSON object such as {\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10}.",

            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Title"] = "Applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Candidate"] = "Candidate",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.UpdateStatus.Success"] = "Application status updated successfully.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ID"] = "ID",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Email"] = "Email",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.JobTitle"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ChargeMode"] = "Charge Mode",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Attempts"] = "Attempts",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.PromptSource"] = "Prompt Source",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.InterviewSort"] = "Interview sorting",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.TopScorersFirst"] = "Top scorers first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.LowestScorersFirst"] = "Lowest scorers first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.Sort.LatestApplied"] = "Latest applied first",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedFromUtc"] = "Applied from (UTC)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedToUtc"] = "Applied to (UTC)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.OnlyWithInterviewScore"] = "Only candidates with interview score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.ResultsSummary"] = "Showing {0} filtered result(s)",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.InterviewScore"] = "Interview score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.AppliedOn"] = "Applied on",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Employer.Applications.CoverMessage"] = "Cover message",
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
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.Title"] = "My Applications",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.JobTitle"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.AppliedDate"] = "Applied Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.AttemptCount"] = "Attempt Count",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.LatestScoreDate"] = "Latest Score Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MyApplications.Score"] = "Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.NoCredits"] = "Insufficient credits. Please purchase credits to start the interview.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.NoCredits.Link"] = "View Pricing",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.ExpiredLink"] = "Your previous interview link expired. Start the interview again from this page.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.Error.Unavailable"] = "The interview service is temporarily unavailable. Please try again.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Interview.NextQuestion"] = "Next question ready.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Runtime.SponsorMessage"] = "This interview is company-sponsored.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorScoreboard.Title"] = "Vendor Scoreboard",
            [$"{AIInterviewDefaults.LocalizationPrefix}.VendorJobCreation.Title"] = "Create a Job",
        });
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetUpgradeLocaleResources());
        await _localizationService.AddOrUpdateLocaleResourceAsync(GetAdminLocaleResources());

        await base.InstallAsync();
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

        await base.UninstallAsync();
    }

    #endregion
}
