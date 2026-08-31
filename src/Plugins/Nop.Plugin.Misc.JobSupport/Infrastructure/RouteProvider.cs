using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.JobSupport.Infrastructure;

public class RouteProvider : IRouteProvider
{
    public int Priority => 0;

    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(
            name: "Plugin.Misc.JobSupport.LegacyParity",
            pattern: $"{JobSupportDefaults.AdminRoutePrefix}/LegacyParity",
            defaults: new { controller = "JobSupportAdmin", action = "LegacyParity", area = AreaNames.ADMIN });
    }
}
