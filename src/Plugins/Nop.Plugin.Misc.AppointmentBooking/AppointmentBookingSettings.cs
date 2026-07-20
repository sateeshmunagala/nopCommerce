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
    /// Gets or sets the default booking duration in minutes
    /// </summary>
    public int DefaultDurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets the default minimum advance booking window in hours
    /// </summary>
    public int DefaultMinAdvanceBookingHours { get; set; }

    /// <summary>
    /// Gets or sets the default maximum advance booking window in days
    /// </summary>
    public int DefaultMaxAdvanceBookingDays { get; set; }
}
