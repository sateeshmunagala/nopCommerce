using Nop.Core;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;

namespace Nop.Plugin.Misc.JobSupport;

public class JobSupportPlugin : BasePlugin, IMiscPlugin
{
    private readonly ILocalizationService _localizationService;
    private readonly ISettingService _settingService;
    private readonly IWebHelper _webHelper;

    public JobSupportPlugin(ILocalizationService localizationService,
        ISettingService settingService,
        IWebHelper webHelper)
    {
        _localizationService = localizationService;
        _settingService = settingService;
        _webHelper = webHelper;
    }

    public override string GetConfigurationPageUrl() => $"{_webHelper.GetStoreLocation()}Admin/JobSupport/Configure";

    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new JobSupportSettings { Enabled = false, DefaultPageSize = 12 });
        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Misc.JobSupport.FriendlyName"] = "Job Support",
            ["Plugins.Misc.JobSupport.Configuration"] = "Job Support configuration",
            ["Plugins.Misc.JobSupport.Fields.Enabled"] = "Enabled",
            ["Plugins.Misc.JobSupport.Disabled"] = "Job Support is currently disabled."
        });
        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<JobSupportSettings>();
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.JobSupport");
        await base.UninstallAsync();
    }
}
