using Nop.Core.Domain.Cms;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Plugin.Widgets.AISearch.Components;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Web.Framework.Infrastructure;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Widgets.AISearch;

public class AISearchPlugin : BasePlugin, IMiscPlugin, IWidgetPlugin
{
    private readonly ILocalizationService _localizationService;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly ISettingService _settingService;
    private readonly WidgetSettings _widgetSettings;

    public AISearchPlugin(ILocalizationService localizationService,
        INopUrlHelper nopUrlHelper,
        IScheduleTaskService scheduleTaskService,
        ISettingService settingService,
        WidgetSettings widgetSettings)
    {
        _localizationService = localizationService;
        _nopUrlHelper = nopUrlHelper;
        _scheduleTaskService = scheduleTaskService;
        _settingService = settingService;
        _widgetSettings = widgetSettings;
    }

    public override string GetConfigurationPageUrl()
    {
        return _nopUrlHelper.RouteUrl(AISearchDefaults.ConfigurationRouteName);
    }

    public Task<IList<string>> GetWidgetZonesAsync()
    {
        return Task.FromResult<IList<string>>(new List<string> { PublicWidgetZones.Footer });
    }

    public Type GetWidgetViewComponent(string widgetZone)
    {
        if (widgetZone == PublicWidgetZones.Footer)
            return typeof(AISearchWidgetComponent);

        return null;
    }

    public bool HideInWidgetList => false;

    public override async Task InstallAsync()
    {
        var settings = new AISearchSettings
        {
            Enabled = false,
            AzureOpenAiApiVersion = "2024-10-21",
            TopK = 8,
            SimilarityThreshold = 0.75,
            ReindexIntervalMinutes = 30
        };
        await _settingService.SaveSettingAsync(settings);

        if (!_widgetSettings.ActiveWidgetSystemNames.Contains(AISearchDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Add(AISearchDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        if (await _scheduleTaskService.GetTaskByTypeAsync(AISearchDefaults.ScheduleTaskType) == null)
        {
            await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
            {
                Name = AISearchDefaults.ScheduleTaskName,
                Type = AISearchDefaults.ScheduleTaskType,
                Seconds = settings.ReindexIntervalMinutes * 60,
                Enabled = true,
                StopOnError = false,
                LastEnabledUtc = DateTime.UtcNow
            });
        }

        await _localizationService.AddOrUpdateLocaleResourceAsync(LocaleResources());
        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        var task = await _scheduleTaskService.GetTaskByTypeAsync(AISearchDefaults.ScheduleTaskType);
        if (task != null)
            await _scheduleTaskService.DeleteTaskAsync(task);

        await _settingService.DeleteSettingAsync<AISearchSettings>();

        if (_widgetSettings.ActiveWidgetSystemNames.RemoveAll(name =>
            string.Equals(name, AISearchDefaults.SystemName, StringComparison.OrdinalIgnoreCase)) > 0)
            await _settingService.SaveSettingAsync(_widgetSettings);

        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Widgets.AISearch");
        await base.UninstallAsync();
    }

    private static Dictionary<string, string> LocaleResources()
    {
        return new Dictionary<string, string>
        {
            ["Plugins.Widgets.AISearch.Admin.Enabled"] = "Enabled",
            ["Plugins.Widgets.AISearch.Admin.Enabled.Hint"] = "Enable semantic product search on the public store.",
            ["Plugins.Widgets.AISearch.Admin.AzureOpenAiEndpointUrl"] = "Azure OpenAI endpoint URL",
            ["Plugins.Widgets.AISearch.Admin.AzureOpenAiEndpointUrl.Hint"] = "The HTTPS endpoint of the Azure OpenAI resource.",
            ["Plugins.Widgets.AISearch.Admin.AzureOpenAiApiKey"] = "Azure OpenAI API key",
            ["Plugins.Widgets.AISearch.Admin.AzureOpenAiApiKey.Hint"] = "The API key used only for server-side embedding requests.",
            ["Plugins.Widgets.AISearch.Admin.AzureOpenAiEmbeddingDeploymentName"] = "Embedding deployment name",
            ["Plugins.Widgets.AISearch.Admin.AzureOpenAiEmbeddingDeploymentName.Hint"] = "The Azure OpenAI deployment that returns 1536-dimensional embeddings.",
            ["Plugins.Widgets.AISearch.Admin.AzureOpenAiApiVersion"] = "Azure OpenAI API version",
            ["Plugins.Widgets.AISearch.Admin.AzureOpenAiApiVersion.Hint"] = "The API version sent with embedding requests.",
            ["Plugins.Widgets.AISearch.Admin.TopK"] = "Maximum results",
            ["Plugins.Widgets.AISearch.Admin.TopK.Hint"] = "The maximum number of ranked products returned for one search.",
            ["Plugins.Widgets.AISearch.Admin.SimilarityThreshold"] = "Similarity threshold",
            ["Plugins.Widgets.AISearch.Admin.SimilarityThreshold.Hint"] = "Minimum cosine similarity from 0 to 1 required for a result.",
            ["Plugins.Widgets.AISearch.Admin.SyncFusionLicenseKey"] = "Syncfusion license key",
            ["Plugins.Widgets.AISearch.Admin.SyncFusionLicenseKey.Hint"] = "The browser license key used by the Syncfusion AI AssistView control.",
            ["Plugins.Widgets.AISearch.Admin.ReindexIntervalMinutes"] = "Reindex interval (minutes)",
            ["Plugins.Widgets.AISearch.Admin.ReindexIntervalMinutes.Hint"] = "The scheduled interval for refreshing changed product embeddings.",
            ["Plugins.Widgets.AISearch.Admin.ReindexNow"] = "Reindex Now",
            ["Plugins.Widgets.AISearch.Admin.ReindexStarted"] = "Product embeddings were reindexed.",
            ["Plugins.Widgets.AISearch.Permission.Admin"] = "Admin area. Manage AI product search",
            ["Plugins.Widgets.AISearch.NoResults"] = "No matching results found, try a different search"
        };
    }
}
