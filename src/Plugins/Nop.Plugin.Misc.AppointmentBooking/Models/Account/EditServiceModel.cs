using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.AppointmentBooking.Models.Account;

/// <summary>
/// Represents a vendor editable bookable service
/// </summary>
public record EditServiceModel : BaseNopEntityModel
{
    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.Account.Service.Title")]
    public string Title { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.Account.Service.ShortDescription")]
    public string ShortDescription { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.Account.Service.Price")]
    public decimal Price { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.Account.Service.DurationMinutes")]
    public int DurationMinutes { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.Account.Service.Description")]
    public string ServiceDescription { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.Account.Service.MappedProduct")]
    public int MappedProductId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AppointmentBooking.Account.Service.IsPublic")]
    public bool IsPublic { get; set; }

    public IList<SelectListItem> AvailableProducts { get; set; } = new List<SelectListItem>();
}
