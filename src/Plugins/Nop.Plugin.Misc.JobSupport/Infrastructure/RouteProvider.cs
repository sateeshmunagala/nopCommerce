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
        endpointRouteBuilder.MapControllerRoute("Plugin.Misc.JobSupport.ProfileList", "job-support/profiles",
            new { controller = "JobSupportProfile", action = "List" });
        endpointRouteBuilder.MapControllerRoute("Plugin.Misc.JobSupport.ProfileDetail", "job-support/profile/{slug}",
            new { controller = "JobSupportProfile", action = "Detail" });
        endpointRouteBuilder.MapControllerRoute("Plugin.Misc.JobSupport.Relationship", "job-support/profile/{slug}/{action}",
            new { controller = "JobSupportRelationship" });
        endpointRouteBuilder.MapControllerRoute("Plugin.Misc.JobSupport.AccountProfile", "customer/job-support/profile",
            new { controller = "JobSupportAccount", action = "Profile" });
        endpointRouteBuilder.MapControllerRoute("Plugin.Misc.JobSupport.AccountShortlisted", "customer/job-support/shortlisted",
            new { controller = "JobSupportAccount", action = "Shortlisted" });
        endpointRouteBuilder.MapControllerRoute("Plugin.Misc.JobSupport.AccountRelationships", "customer/job-support/relationships",
            new { controller = "JobSupportAccount", action = "Relationships" });
        endpointRouteBuilder.MapControllerRoute("Plugin.Misc.JobSupport.AccountSubscription", "customer/job-support/subscription",
            new { controller = "JobSupportSubscription", action = "Index" });
        endpointRouteBuilder.MapControllerRoute("Plugin.Misc.JobSupport.AccountAffiliations", "customer/job-support/affiliations",
            new { controller = "JobSupportAccount", action = "Affiliations" });

        endpointRouteBuilder.MapControllerRoute(
            name: JobSupportDefaults.ConfigurationRouteName,
            pattern: $"{JobSupportDefaults.AdminRoutePrefix}/Configure",
            defaults: new { controller = "JobSupportAdmin", action = "Configure", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(
            name: "Plugin.Misc.JobSupport.LegacyParity",
            pattern: $"{JobSupportDefaults.AdminRoutePrefix}/LegacyParity",
            defaults: new { controller = "JobSupportAdmin", action = "LegacyParity", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(
            name: "Plugin.Misc.JobSupport.WorkflowDiagnostics",
            pattern: $"{JobSupportDefaults.AdminRoutePrefix}/WorkflowDiagnostics",
            defaults: new { controller = "JobSupportAdmin", action = "WorkflowDiagnostics", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(
            name: "Plugin.Misc.JobSupport.Migration",
            pattern: $"{JobSupportDefaults.AdminRoutePrefix}/Migration",
            defaults: new { controller = "JobSupportAdmin", action = "Migration", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(
            name: "Plugin.Misc.JobSupport.Cutover",
            pattern: $"{JobSupportDefaults.AdminRoutePrefix}/Cutover",
            defaults: new { controller = "JobSupportAdmin", action = "Cutover", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(
            name: "Plugin.Misc.JobSupport.Admin",
            pattern: $"{JobSupportDefaults.AdminRoutePrefix}/{{action}}",
            defaults: new { controller = "JobSupportAdmin", action = "Configure", area = AreaNames.ADMIN });
    }
}
