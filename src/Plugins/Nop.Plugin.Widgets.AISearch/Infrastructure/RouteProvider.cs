using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Infrastructure;

namespace Nop.Plugin.Widgets.AISearch.Infrastructure;

public class RouteProvider : BaseRouteProvider, IRouteProvider
{
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(name: AISearchDefaults.ConfigurationRouteName,
            pattern: "Admin/AISearch/Configure",
            defaults: new { controller = "AISearchAdmin", action = "Configure", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: AISearchDefaults.ReindexRouteName,
            pattern: "Admin/AISearch/ReindexNow",
            defaults: new { controller = "AISearchAdmin", action = "ReindexNow", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: AISearchDefaults.PublicSearchRouteName,
            pattern: "aisearch/query",
            defaults: new { controller = "AISearch", action = "Search" });
    }

    public int Priority => 0;
}
