using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

/// <summary>
/// Represents plugin route provider
/// </summary>
public class RouteProvider : IRouteProvider
{
    /// <summary>
    /// Register routes
    /// </summary>
    /// <param name="endpointRouteBuilder">Route builder</param>
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        //Admin
        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.ConfigurationRouteName,
            pattern: "Admin/AIInterview/Configure",
            defaults: new { controller = "AIInterviewAdmin", action = "Configure", area = AreaNames.ADMIN });

        //Public
        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.IndexRouteName,
            pattern: "aiinterview",
            defaults: new { controller = "AIInterview", action = "Index" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.ApplyRouteName,
            pattern: "aiinterview/apply",
            defaults: new { controller = "AIInterview", action = "Apply" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.HistoryRouteName,
            pattern: "aiinterview/history",
            defaults: new { controller = "AIInterview", action = "History" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.ReportRouteName,
            pattern: "aiinterview/report/{sessionId}",
            defaults: new { controller = "AIInterview", action = "Report" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.InterviewRouteName,
            pattern: "aiinterview/interview/{sessionKey}",
            defaults: new { controller = "AIInterview", action = "Interview" });
    }

    /// <summary>
    /// Gets a priority of route provider
    /// </summary>
    public int Priority => 0;
}
