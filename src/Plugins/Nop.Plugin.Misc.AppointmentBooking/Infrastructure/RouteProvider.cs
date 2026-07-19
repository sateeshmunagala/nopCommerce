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

        endpointRouteBuilder.MapControllerRoute(name: "Nop.Plugin.Misc.AppointmentBooking.Services",
            pattern: "Admin/AppointmentBooking/Services",
            defaults: new { controller = "AppointmentBooking", action = "Services", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: "Nop.Plugin.Misc.AppointmentBooking.EditService",
            pattern: "Admin/AppointmentBooking/Service/Edit/{id?}",
            defaults: new { controller = "AppointmentBooking", action = "EditService", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: "Nop.Plugin.Misc.AppointmentBooking.Availability",
            pattern: "Admin/AppointmentBooking/Service/{serviceId}/Availability",
            defaults: new { controller = "AppointmentBooking", action = "Availability", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: "Nop.Plugin.Misc.AppointmentBooking.Bookings",
            pattern: "Admin/AppointmentBooking/Bookings",
            defaults: new { controller = "AppointmentBooking", action = "Bookings", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: "Nop.Plugin.Misc.AppointmentBooking.BookingDetails",
            pattern: "Admin/AppointmentBooking/Booking/{id}",
            defaults: new { controller = "AppointmentBooking", action = "BookingDetails", area = AreaNames.ADMIN });

        var lang = GetLanguageRoutePattern();

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.ProductBookingRouteName,
            pattern: $"{lang}/appointment-booking/product/{{productId}}",
            defaults: new { controller = "AppointmentBooking", action = "ProductBooking" });

        endpointRouteBuilder.MapControllerRoute(name: "Nop.Plugin.Misc.AppointmentBooking.HoldSlot",
            pattern: $"{lang}/appointment-booking/product/{{productId}}/hold",
            defaults: new { controller = "AppointmentBooking", action = "HoldSlot" });

    }

    /// <summary>
    /// Gets a priority of route provider
    /// </summary>
    public int Priority => 0;
}
