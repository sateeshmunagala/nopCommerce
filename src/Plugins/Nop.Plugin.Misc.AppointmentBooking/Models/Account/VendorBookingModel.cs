namespace Nop.Plugin.Misc.AppointmentBooking.Models.Account;

/// <summary>
/// Represents a vendor-visible booking
/// </summary>
public record VendorBookingModel
{
    public int Id { get; set; }

    public string ServiceName { get; set; }

    public string CustomerDisplayName { get; set; }

    public string DisplayCustomerName { get; set; }

    public int? OrderId { get; set; }

    public string OrderDisplayText { get; set; }

    public int ProductId { get; set; }

    public DateTime StartUtc { get; set; }

    public string StartText { get; set; }

    public DateTime EndUtc { get; set; }

    public string EndText { get; set; }

    public string DateHeaderText { get; set; }

    public string TimeRangeText { get; set; }

    public int DurationMinutes { get; set; }

    public string PriceText { get; set; }

    public string Status { get; set; }

    public string StatusText { get; set; }

    public string StatusCssClass { get; set; }

    public string AttendeeName { get; set; }

    public string AttendeeEmail { get; set; }
}

public record VendorBookingTabModel
{
    public string ActiveTab { get; set; }

    public IList<VendorBookingModel> Bookings { get; set; } = new List<VendorBookingModel>();
}
