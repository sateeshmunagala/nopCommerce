using Nop.Core;

namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Represents an appointment booking
/// </summary>
public class Booking : BaseEntity
{
    public int ServiceId { get; set; }

    public int ProductId { get; set; }

    public int VendorId { get; set; }

    public int CustomerId { get; set; }

    public int OrderId { get; set; }

    public int OrderItemId { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string CustomerTimeZoneId { get; set; }

    public string Status { get; set; }

    public string AttendeeName { get; set; }

    public string AttendeeEmail { get; set; }

    public string AttendeePhone { get; set; }

    public string AttendeeNotes { get; set; }

    public string CancellationReason { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}
