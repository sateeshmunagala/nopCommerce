using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Models;

/// <summary>
/// Represents vendor calendar connection model
/// </summary>
public record CalendarConnectionModel : BaseNopModel
{
    /// <summary>
    /// Gets or sets the vendor identifier
    /// </summary>
    public int VendorId { get; set; }

    /// <summary>
    /// Gets or sets the calendar provider label
    /// </summary>
    public string CalendarProvider { get; set; }

    /// <summary>
    /// Gets or sets the calendar connection URL
    /// </summary>
    public string ConnectUrl { get; set; }

    /// <summary>
    /// Gets or sets the calendar disconnection URL
    /// </summary>
    public string DisconnectUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a calendar is connected
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// Gets or sets a status message
    /// </summary>
    public string StatusMessage { get; set; }
}
