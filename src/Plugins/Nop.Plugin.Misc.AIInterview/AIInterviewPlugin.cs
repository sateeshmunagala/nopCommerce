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
