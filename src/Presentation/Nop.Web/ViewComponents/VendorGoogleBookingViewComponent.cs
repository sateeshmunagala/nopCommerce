using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Booking;
using Nop.Core;

namespace Nop.Web.ViewComponents;

public class VendorGoogleBookingViewComponent : ViewComponent
{
    private readonly IBookingService _bookingService;
    private readonly IWorkContext _workContext;

    public VendorGoogleBookingViewComponent(IBookingService bookingService, IWorkContext workContext)
    {
        _bookingService = bookingService;
        _workContext = workContext;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var vendor = await _workContext.GetCurrentVendorAsync();
        if (vendor == null)
            return Content(string.Empty);

        var token = await _bookingService.GetTokenByVendorIdAsync(vendor.Id);
        //return View("~/Presentation/Nop.Web/Views/Customer/Components/VendorGoogleBooking.cshtml", token);
        // D:\nopcommerce\sateeshmunagala\nopCommerce\src\Presentation\Nop.Web\Views\Customer\Components
        return View("Default", token);
    }
}
