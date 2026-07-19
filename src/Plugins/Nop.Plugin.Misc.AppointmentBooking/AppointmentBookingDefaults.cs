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
    /// Gets the calendar connect route name
    /// </summary>
    public static string CalendarConnectRouteName => "Nop.Plugin.Misc.AppointmentBooking.CalendarConnect";

    /// <summary>
    /// Gets the calendar callback route name
    /// </summary>
    public static string CalendarCallbackRouteName => "Nop.Plugin.Misc.AppointmentBooking.CalendarCallback";

    /// <summary>
    /// Gets the calendar disconnect route name
    /// </summary>
    public static string CalendarDisconnectRouteName => "Nop.Plugin.Misc.AppointmentBooking.CalendarDisconnect";
}
