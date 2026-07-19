using Nop.Core;

namespace Nop.Plugin.Misc.AppointmentBooking.Domains;

/// <summary>
/// Represents a mapping between a nopCommerce product and a bookable service
/// </summary>
public class ServiceProductMapping : BaseEntity
{
    public int ServiceId { get; set; }

    public int ProductId { get; set; }

    public int VendorId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedOnUtc { get; set; }

    public DateTime UpdatedOnUtc { get; set; }
}
