using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Admin;

public record AvailabilityRuleAdminModel : BaseNopEntityModel
{
    public int ServiceId { get; set; }

    public int VendorId { get; set; }

    public int DayOfWeek { get; set; }

    public string StartTime { get; set; }

    public string EndTime { get; set; }

    public string TimeZoneId { get; set; }

    public bool IsActive { get; set; }
}
