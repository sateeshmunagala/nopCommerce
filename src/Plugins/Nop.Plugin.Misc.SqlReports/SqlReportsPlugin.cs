using Nop.Services.Common;
using Nop.Services.Plugins;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.SqlReports;

public class SqlReportsPlugin : BasePlugin, IMiscPlugin
{
    private readonly INopUrlHelper _nopUrlHelper;

    public SqlReportsPlugin(INopUrlHelper nopUrlHelper)
    {
        _nopUrlHelper = nopUrlHelper;
    }

    public override string GetConfigurationPageUrl()
    {
        return _nopUrlHelper.RouteUrl(SqlReportsDefaults.Routes.Reports);
    }

    public override Task InstallAsync()
    {
        return base.InstallAsync();
    }

    public override Task UninstallAsync()
    {
        return base.UninstallAsync();
    }
}
