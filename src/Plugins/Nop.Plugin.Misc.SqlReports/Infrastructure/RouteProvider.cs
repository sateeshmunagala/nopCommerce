using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.SqlReports.Infrastructure;

public class RouteProvider : IRouteProvider
{
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(SqlReportsDefaults.Routes.Configure,
            "Admin/SqlReports/Configure",
            new { controller = "SqlReportsAdmin", action = "Configure", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(SqlReportsDefaults.Routes.Reports,
            "Admin/SqlReports",
            new { controller = "SqlReportsAdmin", action = "Reports", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(SqlReportsDefaults.Routes.ReportCreate,
            "Admin/SqlReports/Create",
            new { controller = "SqlReportsAdmin", action = "ReportCreate", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(SqlReportsDefaults.Routes.ReportEdit,
            "Admin/SqlReports/Edit/{id:min(0)?}",
            new { controller = "SqlReportsAdmin", action = "ReportEdit", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(SqlReportsDefaults.Routes.ReportRun,
            "Admin/SqlReports/Run/{id:min(0)?}",
            new { controller = "SqlReportsAdmin", action = "Run", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute("Plugin.Misc.SqlReports.Export",
            "Admin/SqlReports/Export/{id:min(0)?}",
            new { controller = "SqlReportsAdmin", action = "Export", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(SqlReportsDefaults.Routes.Parameters,
            "Admin/SqlReports/Parameters",
            new { controller = "SqlReportsAdmin", action = "Parameters", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(SqlReportsDefaults.Routes.ParameterCreate,
            "Admin/SqlReports/Parameters/Create",
            new { controller = "SqlReportsAdmin", action = "ParameterCreate", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(SqlReportsDefaults.Routes.ParameterEdit,
            "Admin/SqlReports/Parameters/Edit/{id:min(0)?}",
            new { controller = "SqlReportsAdmin", action = "ParameterEdit", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(SqlReportsDefaults.Routes.InstantQuery,
            "Admin/SqlReports/Instant",
            new { controller = "SqlReportsAdmin", action = "InstantQuery", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute("Plugin.Misc.SqlReports.InstantExport",
            "Admin/SqlReports/Instant/Export",
            new { controller = "SqlReportsAdmin", action = "InstantExport", area = AreaNames.ADMIN });
    }

    public int Priority => 0;
}
