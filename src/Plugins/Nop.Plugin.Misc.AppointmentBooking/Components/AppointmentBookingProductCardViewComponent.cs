using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.AppointmentBooking.Models;
using Nop.Plugin.Misc.AppointmentBooking.Services;
using Nop.Web.Framework.Components;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.AppointmentBooking.Components;

[ViewComponent(Name = "AppointmentBookingProductCard")]
public class AppointmentBookingProductCardViewComponent : NopViewComponent
{
    private readonly IAppointmentBookingService _appointmentBookingService;

    public AppointmentBookingProductCardViewComponent(IAppointmentBookingService appointmentBookingService)
    {
        _appointmentBookingService = appointmentBookingService;
    }

    public async Task<IViewComponentResult> InvokeAsync(ProductOverviewModel productOverview)
    {
        if (productOverview == null || !await _appointmentBookingService.IsAppointmentProductAsync(productOverview.Id))
            return Content(string.Empty);

        var model = new AppointmentBookingProductCardModel
        {
            Id = productOverview.Id,
            ProductName = productOverview.Name,
            ShortDescription = productOverview.ShortDescription,
            SeName = productOverview.SeName,
            Price = productOverview.ProductPrice?.Price,
            Picture = productOverview.PictureModels.FirstOrDefault()
        };

        return View("~/Plugins/Misc.AppointmentBooking/Views/Shared/Components/AppointmentBookingProductCard/Default.cshtml", model);
    }
}
