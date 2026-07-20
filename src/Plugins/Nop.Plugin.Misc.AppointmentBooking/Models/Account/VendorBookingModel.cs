namespace Nop.Plugin.Misc.AppointmentBooking.Models.Account;

/// <summary>
/// Represents a vendor-visible booking
/// </summary>
public record VendorBookingModel
{
    public int Id { get; set; }

    public string ServiceName { get; set; }

    public string CustomerDisplayName { get; set; }

    public int? OrderId { get; set; }

    public string OrderDisplayText { get; set; }

    public DateTime StartUtc { get; set; }

    public string StartText { get; set; }

    public DateTime EndUtc { get; set; }

    public string EndText { get; set; }

    public string Status { get; set; }

    public string AttendeeName { get; set; }

    public string AttendeeEmail { get; set; }
}
