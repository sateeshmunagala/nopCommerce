using Nop.Core;

namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Represents a temporary checkout slot hold
/// </summary>
public class TimeSlotHold : BaseEntity
{
    public int ServiceId { get; set; }

    public int ProductId { get; set; }

    public int VendorId { get; set; }

    public int CustomerId { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public DateTime ExpiresOnUtc { get; set; }

    public string HoldToken { get; set; }

    public DateTime CreatedOnUtc { get; set; }
}
