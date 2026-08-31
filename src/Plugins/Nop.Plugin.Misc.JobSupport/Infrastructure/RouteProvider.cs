using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.JobSupport.Infrastructure;

public class RouteProvider : IRouteProvider
{
    public int Priority => 0;

    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        // Configuration route is registered in phase 2 together with JobSupportAdminController.
    }
}
