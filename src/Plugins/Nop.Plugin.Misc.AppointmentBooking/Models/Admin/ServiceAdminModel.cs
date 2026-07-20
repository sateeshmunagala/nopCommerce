using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Admin;

/// <summary>
/// Represents a bookable service admin model
/// </summary>
public record ServiceAdminModel : BaseNopEntityModel
{
    public string Name { get; set; }

    public string Description { get; set; }

    public int VendorId { get; set; }

    public int DurationMinutes { get; set; }

    public int BufferBeforeMinutes { get; set; }

    public int BufferAfterMinutes { get; set; }

    public int MinAdvanceBookingHours { get; set; }

    public int MaxAdvanceBookingDays { get; set; }

    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }

    public int MappedProductId { get; set; }
}
