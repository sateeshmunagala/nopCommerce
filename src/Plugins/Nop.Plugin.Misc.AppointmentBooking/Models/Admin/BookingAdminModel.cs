using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Admin;

public record BookingAdminModel : BaseNopEntityModel
{
    public int ServiceId { get; set; }

    public string ServiceName { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; }

    public int VendorId { get; set; }

    public string VendorName { get; set; }

    public int CustomerId { get; set; }

    public string CustomerDisplayName { get; set; }

    public int? OrderId { get; set; }

    public string OrderDisplayText { get; set; }

    public int? OrderItemId { get; set; }

    public string OrderItemDisplayText { get; set; }

    public DateTime StartUtc { get; set; }

    public string StartText { get; set; }

    public DateTime EndUtc { get; set; }

    public string EndText { get; set; }

    public string Status { get; set; }

    public string AttendeeName { get; set; }

    public string AttendeeEmail { get; set; }

    public string AttendeePhone { get; set; }

    public string AttendeeNotes { get; set; }

    public string CancellationReason { get; set; }
}
