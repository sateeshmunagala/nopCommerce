using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.JobSupport.Infrastructure;

public class RouteProvider : IRouteProvider
{
    public int Priority => 0;

    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(name: JobSupportDefaults.ConfigurationRouteName,
            pattern: "Admin/JobSupport/Configure",
            defaults: new { controller = "JobSupportAdmin", action = "Configure", area = "Admin" });
    }
}
