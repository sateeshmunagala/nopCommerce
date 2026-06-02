using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.Helpers;

namespace Nop.Plugin.Misc.AIInterview;

/// <summary>
/// Represents AI Interview plugin
/// </summary>
public class AIInterviewPlugin : BasePlugin, IMiscPlugin
{
    #region Fields

    private readonly ILocalizationService _localizationService;
    private readonly ISettingService _settingService;
    private readonly IWebHelper _webHelper;

    #endregion

    #region Ctor

    public AIInterviewPlugin(ILocalizationService localizationService,
        ISettingService settingService,
        IWebHelper webHelper)
    {
        _localizationService = localizationService;
        _settingService = settingService;
        _webHelper = webHelper;
    }

    #endregion

    #region Methods

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
    public override async Task InstallAsync()
    {
        //settings
        var settings = new AIInterviewSettings
        {
            Enabled = false,
            ApiKey = string.Empty,
            ResumeRequired = false,
            InterviewRequired = false,
            MinimumScore = 0
        };
        await _settingService.SaveSettingAsync(settings);

        var mockSettings = new MockAIInterviewSettings
        {
            UseMockResponses = true
        };
        await _settingService.SaveSettingAsync(mockSettings);

        //locales
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            [$"{AIInterviewDefaults.LocalizationPrefix}.Enabled"] = "Enabled",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Enabled.Hint"] = "Enable this setting to use AI Interview features",
            [$"{AIInterviewDefaults.LocalizationPrefix}.ApiKey"] = "API Key",
            [$"{AIInterviewDefaults.LocalizationPrefix}.ApiKey.Hint"] = "Specify the API key for AI service.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.UseMockResponses"] = "Use Mock Responses",
            [$"{AIInterviewDefaults.LocalizationPrefix}.UseMockResponses.Hint"] = "Enable to use mock responses instead of calling actual AI service.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.ResumeRequired"] = "Resume Required",
            [$"{AIInterviewDefaults.LocalizationPrefix}.InterviewRequired"] = "Interview Required",
            [$"{AIInterviewDefaults.LocalizationPrefix}.MinimumScore"] = "Minimum Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.JobTitle"] = "Job Title",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeFile"] = "Resume File",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.JobTitle.Required"] = "Job Title is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.ResumeFile.Required"] = "Resume file is required.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.AlreadyApplied"] = "You have already applied for a position.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.InterviewRequired"] = "An interview is required before you can apply.",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Apply.MinimumScoreNotReached"] = "You must achieve a minimum score of {0} in your interview to apply. Your latest score was {1}.",
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

            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Title"] = "Interview Report",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Date"] = "Date",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Score"] = "Score",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.Details"] = "Report Details",
            [$"{AIInterviewDefaults.LocalizationPrefix}.Report.NotFound"] = "Report not found or access denied.",

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
        });

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

        //locales
        await _localizationService.DeleteLocaleResourcesAsync(AIInterviewDefaults.LocalizationPrefix);

        await base.UninstallAsync();
    }

    #endregion
}
