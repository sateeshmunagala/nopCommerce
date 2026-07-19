using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Infrastructure;

namespace Nop.Plugin.Misc.AppointmentBooking.Infrastructure;

/// <summary>
/// Represents appointment booking route provider
/// </summary>
public class RouteProvider : BaseRouteProvider, IRouteProvider
{
    /// <summary>
    /// Register routes
    /// </summary>
    /// <param name="endpointRouteBuilder">Route builder</param>
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.ConfigurationRouteName,
            pattern: "Admin/AppointmentBooking/Configure",
            defaults: new { controller = "AppointmentBooking", action = "Configure", area = AreaNames.ADMIN });

        var lang = GetLanguageRoutePattern();

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.ProductBookingRouteName,
            pattern: $"{lang}/appointment-booking/product/{{productId}}",
            defaults: new { controller = "AppointmentBooking", action = "ProductBooking" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.CalendarConnectRouteName,
            pattern: $"{lang}/appointment-booking/calendar/connect",
            defaults: new { controller = "CalendarConnection", action = "Connect" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.CalendarCallbackRouteName,
            pattern: $"{lang}/appointment-booking/calendar/callback",
            defaults: new { controller = "CalendarConnection", action = "Callback" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.CalendarDisconnectRouteName,
            pattern: $"{lang}/appointment-booking/calendar/disconnect",
            defaults: new { controller = "CalendarConnection", action = "Disconnect" });
    }

    /// <summary>
    /// Gets a priority of route provider
    /// </summary>
    public int Priority => 0;
}
