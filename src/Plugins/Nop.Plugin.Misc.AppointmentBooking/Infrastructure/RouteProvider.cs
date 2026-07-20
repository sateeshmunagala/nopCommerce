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
            pattern: $"{lang}/appointment-booking/product/{{productId:int}}/hold",
            defaults: new { controller = "AppointmentBooking", action = "HoldSlot" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.AccountAvailabilityRouteName,
            pattern: $"{lang}/appointment-booking/account/availability",
            defaults: new { controller = "VendorAppointmentBooking", action = "AccountAvailability" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.AccountAvailabilitySaveScheduleRouteName,
            pattern: $"{lang}/appointment-booking/account/availability/save-schedule",
            defaults: new { controller = "VendorAppointmentBooking", action = "SaveAvailabilitySchedule" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.AccountAvailabilityBlockDatesRouteName,
            pattern: $"{lang}/appointment-booking/account/availability/block-dates",
            defaults: new { controller = "VendorAppointmentBooking", action = "BlockUnavailableDates" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.AccountAvailabilityDeleteBlockedDateRouteName,
            pattern: $"{lang}/appointment-booking/account/availability/delete-blocked-date",
            defaults: new { controller = "VendorAppointmentBooking", action = "DeleteBlockedDate" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.AccountServicesRouteName,
            pattern: $"{lang}/appointment-booking/account/services",
            defaults: new { controller = "VendorAppointmentBooking", action = "Services" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.AccountCreateServiceRouteName,
            pattern: $"{lang}/appointment-booking/account/services/create",
            defaults: new { controller = "VendorAppointmentBooking", action = "CreateService" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.AccountEditServiceRouteName,
            pattern: $"{lang}/appointment-booking/account/services/edit/{{id}}",
            defaults: new { controller = "VendorAppointmentBooking", action = "EditService" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.AccountServiceAvailabilityRouteName,
            pattern: $"{lang}/appointment-booking/account/services/{{id}}/availability",
            defaults: new { controller = "VendorAppointmentBooking", action = "Availability" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.AccountServiceQuestionsRouteName,
            pattern: $"{lang}/appointment-booking/account/services/{{id}}/questions",
            defaults: new { controller = "VendorAppointmentBooking", action = "Questions" });

        endpointRouteBuilder.MapControllerRoute(name: AppointmentBookingDefaults.AccountBookingsRouteName,
            pattern: $"{lang}/appointment-booking/account/bookings",
            defaults: new { controller = "VendorAppointmentBooking", action = "Bookings" });

    }

    /// <summary>
    /// Gets a priority of route provider
    /// </summary>
    public int Priority => 0;
}
