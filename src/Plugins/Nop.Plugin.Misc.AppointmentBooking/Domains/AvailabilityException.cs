using Nop.Core;

namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Represents date-specific availability block or override
/// </summary>
public class AvailabilityException : BaseEntity
{
    public int ServiceId { get; set; }

    public int VendorId { get; set; }

    public DateTime ExceptionDateUtc { get; set; }

    public DateTime StartTimeUtc { get; set; }

    public DateTime EndTimeUtc { get; set; }

    public bool IsAvailable { get; set; }

    public string Reason { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}
