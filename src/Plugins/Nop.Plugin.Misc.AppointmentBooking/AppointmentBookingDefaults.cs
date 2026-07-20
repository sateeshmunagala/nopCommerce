namespace Nop.Plugin.Misc.AppointmentBooking;

/// <summary>
/// Represents appointment booking plugin constants
/// </summary>
public static class AppointmentBookingDefaults
{
    /// <summary>
    /// Gets the plugin system name
    /// </summary>
    public static string SystemName => "Misc.AppointmentBooking";

    /// <summary>
    /// Gets the configuration route name
    /// </summary>
    public static string ConfigurationRouteName => "Nop.Plugin.Misc.AppointmentBooking.Configure";

    /// <summary>
    /// Gets the product booking route name
    /// </summary>
    public static string ProductBookingRouteName => "Nop.Plugin.Misc.AppointmentBooking.ProductBooking";

    /// <summary>
    /// Gets the vendor account services route name
    /// </summary>
    public static string AccountServicesRouteName => "Nop.Plugin.Misc.AppointmentBooking.Account.Services";

    /// <summary>
    /// Gets the vendor account service create route name
    /// </summary>
    public static string AccountCreateServiceRouteName => "Nop.Plugin.Misc.AppointmentBooking.Account.Service.Create";

    /// <summary>
    /// Gets the vendor account service edit route name
    /// </summary>
    public static string AccountEditServiceRouteName => "Nop.Plugin.Misc.AppointmentBooking.Account.Service.Edit";

    /// <summary>
    /// Gets the vendor account service availability route name
    /// </summary>
    public static string AccountServiceAvailabilityRouteName => "Nop.Plugin.Misc.AppointmentBooking.Account.Service.Availability";

    /// <summary>
    /// Gets the vendor account service questions route name
    /// </summary>
    public static string AccountServiceQuestionsRouteName => "Nop.Plugin.Misc.AppointmentBooking.Account.Service.Questions";

    /// <summary>
    /// Gets the vendor account bookings route name
    /// </summary>
    public static string AccountBookingsRouteName => "Nop.Plugin.Misc.AppointmentBooking.Account.Bookings";

    /// <summary>
    /// Gets the vendor account services tab id
    /// </summary>
    public static int AccountServicesTabId => 170;

    /// <summary>
    /// Gets the vendor account bookings tab id
    /// </summary>
    public static int AccountBookingsTabId => 171;

    /// <summary>
    /// Gets the appointments administration menu system name
    /// </summary>
    public static string AppointmentsAdminMenuSystemName => "Nop.Plugin.Misc.AppointmentBooking.AppointmentsAdminMenu";

    /// <summary>
    /// Gets the services administration menu system name
    /// </summary>
    public static string ServicesAdminMenuSystemName => "Nop.Plugin.Misc.AppointmentBooking.ServicesAdminMenu";

    /// <summary>
    /// Gets the bookings administration menu system name
    /// </summary>
    public static string BookingsAdminMenuSystemName => "Nop.Plugin.Misc.AppointmentBooking.BookingsAdminMenu";
}
