namespace Nop.Plugin.Misc.AppointmentBooking.Models.Account;

/// <summary>
/// Represents a vendor-visible booking
/// </summary>
public record VendorBookingModel
{
    public int Id { get; set; }

    public string ServiceName { get; set; }

    public int ProductId { get; set; }

    public int CustomerId { get; set; }

    public int OrderId { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string Status { get; set; }

    public string AttendeeName { get; set; }

    public string AttendeeEmail { get; set; }
}
