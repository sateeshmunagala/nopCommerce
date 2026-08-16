using Nop.Core.Domain.Cms;
using Nop.Plugin.Misc.Skillfinder.InlineFilter.Components;
using Nop.Services.Cms;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Misc.Skillfinder.InlineFilter;

public class SkillfinderInlineFilterPlugin : BasePlugin, IWidgetPlugin
{
    private readonly ILocalizationService _localizationService;
    private readonly ISettingService _settingService;
    private readonly WidgetSettings _widgetSettings;

    public SkillfinderInlineFilterPlugin(
        ILocalizationService localizationService,
        ISettingService settingService,
        WidgetSettings widgetSettings)
    {
        _localizationService = localizationService;
        _settingService = settingService;
        _widgetSettings = widgetSettings;
    }

    public bool HideInWidgetList => false;

    public Task<IList<string>> GetWidgetZonesAsync()
    {
        return Task.FromResult<IList<string>>([PublicWidgetZones.HomepageBeforeProducts]);
    }

    public Type GetWidgetViewComponent(string widgetZone)
    {
        return typeof(InlineFilterWidgetViewComponent);
    }

    public override async Task InstallAsync()
    {
        if (!_widgetSettings.ActiveWidgetSystemNames.Contains(SkillfinderInlineFilterDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Add(SkillfinderInlineFilterDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            [$"{SkillfinderInlineFilterDefaults.LocalizationPrefix}.Eyebrow"] = "Explore roles",
            [$"{SkillfinderInlineFilterDefaults.LocalizationPrefix}.Title"] = "Find jobs by category",
            [$"{SkillfinderInlineFilterDefaults.LocalizationPrefix}.Description"] = "Choose a skill area to see current opportunities without leaving the page.",
            [$"{SkillfinderInlineFilterDefaults.LocalizationPrefix}.Loading"] = "Looking for matching paths...",
            [$"{SkillfinderInlineFilterDefaults.LocalizationPrefix}.ViewMore"] = "View More",
            [$"{SkillfinderInlineFilterDefaults.LocalizationPrefix}.NoResults"] = "No exact matches found. Try broadening your filter options.",
            [$"{SkillfinderInlineFilterDefaults.LocalizationPrefix}.ResultsLabel"] = "Matching jobs",
            [$"{SkillfinderInlineFilterDefaults.LocalizationPrefix}.CategoriesLabel"] = "Job categories",
            [$"{SkillfinderInlineFilterDefaults.LocalizationPrefix}.ViewJob"] = "View job"
        });

        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        if (_widgetSettings.ActiveWidgetSystemNames.Contains(SkillfinderInlineFilterDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Remove(SkillfinderInlineFilterDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _localizationService.DeleteLocaleResourcesAsync(SkillfinderInlineFilterDefaults.LocalizationPrefix);
        await base.UninstallAsync();
    }
}
