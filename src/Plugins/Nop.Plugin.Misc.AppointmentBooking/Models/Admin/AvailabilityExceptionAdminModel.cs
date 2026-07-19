using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Admin;

public record AvailabilityExceptionAdminModel : BaseNopEntityModel
{
    public int ServiceId { get; set; }

    public int VendorId { get; set; }

    public DateTime ExceptionDateUtc { get; set; }

    public string StartTime { get; set; }

    public string EndTime { get; set; }

    public bool IsAvailable { get; set; }

    public string Reason { get; set; }
}
