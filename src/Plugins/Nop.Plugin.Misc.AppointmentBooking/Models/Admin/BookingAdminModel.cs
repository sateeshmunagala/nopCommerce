using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Admin;

public record BookingAdminModel : BaseNopEntityModel
{
    public int ServiceId { get; set; }

    public int ProductId { get; set; }

    public int VendorId { get; set; }

    public int CustomerId { get; set; }

    public int OrderId { get; set; }

    public int OrderItemId { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    public string Status { get; set; }

    public string AttendeeName { get; set; }

    public string AttendeeEmail { get; set; }

    public string AttendeePhone { get; set; }

    public string AttendeeNotes { get; set; }

    public string CancellationReason { get; set; }
}
