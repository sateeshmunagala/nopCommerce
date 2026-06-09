using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

public class AdminMenuCreatedEventConsumer : BaseAdminMenuCreatedEventConsumer
{
    private readonly ILocalizationService _localizationService;
    private readonly INopUrlHelper _nopUrlHelper;

    public AdminMenuCreatedEventConsumer(IPluginManager<IPlugin> pluginManager,
        ILocalizationService localizationService,
        INopUrlHelper nopUrlHelper)
        : base(pluginManager)
    {
        _localizationService = localizationService;
        _nopUrlHelper = nopUrlHelper;
    }

    protected override string PluginSystemName => AIInterviewDefaults.SystemName;

    protected override MenuItemInsertType InsertType => MenuItemInsertType.After;

    protected override string AfterMenuSystemName => "Configuration";

    protected override async Task<AdminMenuItem> GetAdminMenuItemAsync(IPlugin plugin)
    {
        var item = plugin.GetAdminMenuItem();
        item.SystemName = AIInterviewDefaults.AdminMenuSystemName;
        item.Title = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Menu.Root");
        item.Url = _nopUrlHelper.RouteUrl(AIInterviewDefaults.ConfigurationRouteName);
        item.PermissionNames = new List<string> { StandardPermission.Configuration.MANAGE_SETTINGS };
        item.ChildNodes = new List<AdminMenuItem>
        {
            await BuildChildAsync(AIInterviewDefaults.AdminConfigureMenuSystemName, "Plugins.Misc.AIInterview.Admin.Menu.Configure", AIInterviewDefaults.ConfigurationRouteName),
            await BuildChildAsync(AIInterviewDefaults.AdminGeneralMenuSystemName, "Plugins.Misc.AIInterview.Admin.Menu.General", AIInterviewDefaults.AdminGeneralRouteName),
            await BuildChildAsync(AIInterviewDefaults.AdminAiServiceMenuSystemName, "Plugins.Misc.AIInterview.Admin.Menu.AiService", AIInterviewDefaults.AdminAiServiceRouteName),
            await BuildChildAsync(AIInterviewDefaults.AdminSponsorInvitesMenuSystemName, "Plugins.Misc.AIInterview.Admin.Menu.SponsorInvites", AIInterviewDefaults.AdminSponsorInvitesRouteName),
            await BuildChildAsync(AIInterviewDefaults.AdminVendorCreditsMenuSystemName, "Plugins.Misc.AIInterview.Admin.Menu.VendorCredits", AIInterviewDefaults.AdminVendorCreditsRouteName),
            await BuildChildAsync(AIInterviewDefaults.AdminApplicantCreditsMenuSystemName, "Plugins.Misc.AIInterview.Admin.Menu.ApplicantCredits", AIInterviewDefaults.AdminApplicantCreditsRouteName),
            await BuildChildAsync(AIInterviewDefaults.AdminScoreboardMenuSystemName, "Plugins.Misc.AIInterview.Admin.Menu.Scoreboard", AIInterviewDefaults.AdminScoreboardRouteName),
        };

        return item;
    }

    private async Task<AdminMenuItem> BuildChildAsync(string systemName, string resourceKey, string routeName)
    {
        return new AdminMenuItem
        {
            SystemName = systemName,
            Title = await _localizationService.GetResourceAsync(resourceKey),
            Url = _nopUrlHelper.RouteUrl(routeName),
            IconClass = "far fa-circle",
            PermissionNames = new List<string> { StandardPermission.Configuration.MANAGE_SETTINGS }
        };
    }
}
