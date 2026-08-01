using Nop.Services.Common;
using Nop.Plugin.Misc.SqlReports.Services;
using Nop.Services.Plugins;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.SqlReports;

public class SqlReportsPlugin : BasePlugin, IMiscPlugin
{
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly SqlReportsInstallService _installService;

    public SqlReportsPlugin(INopUrlHelper nopUrlHelper,
        SqlReportsInstallService installService)
    {
        _nopUrlHelper = nopUrlHelper;
        _installService = installService;
    }

    public override string GetConfigurationPageUrl()
    {
        return _nopUrlHelper.RouteUrl(SqlReportsDefaults.Routes.Configure);
    }

    public override async Task InstallAsync()
    {
        await _installService.InstallAsync();
        await base.InstallAsync();
    }

    public override async Task UninstallAsync()
    {
        await _installService.UninstallAsync();
        await base.UninstallAsync();
    }
}
