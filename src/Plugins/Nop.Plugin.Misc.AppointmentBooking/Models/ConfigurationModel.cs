using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.AppointmentBooking.Models;

/// <summary>
/// Represents appointment booking configuration model
/// </summary>
public record ConfigurationModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.Enabled")]
    public bool Enabled { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.DefaultBookingUrl")]
    public string DefaultBookingUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.DefaultDurationMinutes")]
    public int DefaultDurationMinutes { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.AllowCalendarIframe")]
    public bool AllowCalendarIframe { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.CalendarProvider")]
    public string CalendarProvider { get; set; }
}
