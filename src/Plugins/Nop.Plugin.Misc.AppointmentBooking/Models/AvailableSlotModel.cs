using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Models;

/// <summary>
/// Represents an available appointment slot
/// </summary>
public record AvailableSlotModel : BaseNopModel
{
    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string DisplayText { get; set; }

    public string DateKey { get; set; }

    public string DateText { get; set; }

    public string DayText { get; set; }

    public string TimeText { get; set; }
}
