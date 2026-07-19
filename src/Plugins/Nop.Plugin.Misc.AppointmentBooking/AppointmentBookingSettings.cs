using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.AppointmentBooking;

/// <summary>
/// Represents appointment booking plugin settings
/// </summary>
public class AppointmentBookingSettings : ISettings
{
    /// <summary>
    /// Gets or sets a value indicating whether appointment booking surfaces are enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the default booking URL
    /// </summary>
    public string DefaultBookingUrl { get; set; }

    /// <summary>
    /// Gets or sets the default booking duration in minutes
    /// </summary>
    public int DefaultDurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a calendar URL can be embedded in an iframe
    /// </summary>
    public bool AllowCalendarIframe { get; set; }

    /// <summary>
    /// Gets or sets the calendar provider label
    /// </summary>
    public string CalendarProvider { get; set; }
}
