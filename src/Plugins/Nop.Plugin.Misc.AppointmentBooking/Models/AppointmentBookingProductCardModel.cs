using Nop.Web.Framework.Models;
using Nop.Web.Models.Media;

namespace Nop.Plugin.Misc.AppointmentBooking.Models;

/// <summary>
/// Represents an appointment booking product card
/// </summary>
public record AppointmentBookingProductCardModel : BaseNopEntityModel
{
    public string ProductName { get; set; }

    public string ShortDescription { get; set; }

    public string SeName { get; set; }

    public string Price { get; set; }

    public PictureModel Picture { get; set; }
}
