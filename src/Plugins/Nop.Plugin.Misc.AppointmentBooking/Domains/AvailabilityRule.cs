using Nop.Core;

namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Represents weekly recurring availability
/// </summary>
public class AvailabilityRule : BaseEntity
{
    public int ServiceId { get; set; }

    public int VendorId { get; set; }

    public int DayOfWeek { get; set; }

    public DateTime StartTimeUtc { get; set; }

    public DateTime EndTimeUtc { get; set; }

    public string TimeZoneId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}
