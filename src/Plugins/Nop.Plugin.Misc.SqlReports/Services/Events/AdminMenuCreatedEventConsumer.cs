using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.SqlReports.Services.Events;

public class AdminMenuCreatedEventConsumer : IConsumer<AdminMenuCreatedEvent>
{
    private readonly ILocalizationService _localizationService;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly SqlReportsSettings _settings;

    public AdminMenuCreatedEventConsumer(ILocalizationService localizationService,
        INopUrlHelper nopUrlHelper,
        SqlReportsSettings settings)
    {
        _localizationService = localizationService;
        _nopUrlHelper = nopUrlHelper;
        _settings = settings;
    }

    public async Task HandleEventAsync(AdminMenuCreatedEvent eventMessage)
    {
        var root = new AdminMenuItem
        {
            SystemName = SqlReportsDefaults.MenuSystemName,
            Title = await _localizationService.GetResourceAsync("Plugins.Misc.SqlReports.Menu"),
            IconClass = "fas fa-chart-line",
            Visible = true,
            PermissionNames = new List<string> { SqlReportsDefaults.Permissions.RunReports }
        };

        root.ChildNodes.Add(new AdminMenuItem
        {
            SystemName = SqlReportsDefaults.ReportsMenuSystemName,
            Title = await _localizationService.GetResourceAsync("Plugins.Misc.SqlReports.Reports"),
            IconClass = "far fa-dot-circle",
            Url = _nopUrlHelper.RouteUrl(SqlReportsDefaults.Routes.Reports),
            PermissionNames = new List<string> { SqlReportsDefaults.Permissions.RunReports }
        });

        root.ChildNodes.Add(new AdminMenuItem
        {
            SystemName = SqlReportsDefaults.ParametersMenuSystemName,
            Title = await _localizationService.GetResourceAsync("Plugins.Misc.SqlReports.Parameters"),
            IconClass = "far fa-dot-circle",
            Url = _nopUrlHelper.RouteUrl(SqlReportsDefaults.Routes.Parameters),
            PermissionNames = new List<string> { SqlReportsDefaults.Permissions.ManageReports }
        });

        if (_settings.EnableInstantQuery)
        {
            root.ChildNodes.Add(new AdminMenuItem
            {
                SystemName = SqlReportsDefaults.InstantQueryMenuSystemName,
                Title = await _localizationService.GetResourceAsync("Plugins.Misc.SqlReports.InstantQuery"),
                IconClass = "far fa-dot-circle",
                Url = _nopUrlHelper.RouteUrl(SqlReportsDefaults.Routes.InstantQuery),
                PermissionNames = new List<string> { SqlReportsDefaults.Permissions.RunReports }
            });
        }

        root.ChildNodes.Add(new AdminMenuItem
        {
            SystemName = SqlReportsDefaults.ConfigureMenuSystemName,
            Title = await _localizationService.GetResourceAsync("Plugins.Misc.SqlReports.Configure"),
            IconClass = "far fa-dot-circle",
            Url = _nopUrlHelper.RouteUrl(SqlReportsDefaults.Routes.Configure),
            PermissionNames = new List<string> { SqlReportsDefaults.Permissions.ManageReports }
        });

        eventMessage.RootMenuItem.InsertAfter("Reports", root);
    }
}
