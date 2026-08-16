using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.Skillfinder.InlineFilter.Infrastructure;

public class RouteProvider : IRouteProvider
{
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(
            name: SkillfinderInlineFilterDefaults.RouteName,
            pattern: SkillfinderInlineFilterDefaults.ResultsRoutePattern,
            defaults: new { controller = "SkillfinderInlineFilter", action = "GetFilteredResults" });
    }

    public int Priority => 0;
}
