using Nop.Web.Framework.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Admin;

/// <summary>
/// Represents a bookable service admin model
/// </summary>
public record ServiceAdminModel : BaseNopEntityModel
{
    public string Name { get; set; }

    public string Description { get; set; }

    public int VendorId { get; set; }

    public string VendorName { get; set; }

    public IList<SelectListItem> AvailableVendors { get; set; } = new List<SelectListItem>();

    public int DurationMinutes { get; set; }

    public IList<SelectListItem> AvailableDurations { get; set; } = new List<SelectListItem>();

    public int BufferBeforeMinutes { get; set; }

    public int BufferAfterMinutes { get; set; }

    public int MinAdvanceBookingHours { get; set; }

    public int MaxAdvanceBookingDays { get; set; }

    public bool IsActive { get; set; }

    public int DisplayOrder { get; set; }

}
