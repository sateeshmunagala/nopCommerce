using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Customers;
using Nop.Services.Media;
using Nop.Services.Vendors;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class VendorPortalLogoViewComponent : NopViewComponent
{
    private readonly IPictureService _pictureService;
    private readonly IVendorService _vendorService;
    private readonly IWorkContext _workContext;
    private readonly ICustomerService _customerService;

    public VendorPortalLogoViewComponent(
        IPictureService pictureService,
        IVendorService vendorService,
        IWorkContext workContext,
        ICustomerService customerService)
    {
        _pictureService = pictureService;
        _vendorService = vendorService;
        _workContext = workContext;
        _customerService = customerService;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone = null, object additionalData = null)
    {
        if (!HttpContext.Items.ContainsKey(AIInterviewDefaults.IsVendorPortalPageKey))
            return Content(string.Empty);

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null || await _customerService.IsGuestAsync(customer))
            return Content(string.Empty);

        var logoUrl = HttpContext.Items.TryGetValue(
            AIInterviewDefaults.VendorPortalLogoUrlKey, out var url)
            ? url as string
            : null;

        if (customer.VendorId > 0)
        {
            var vendor = await _vendorService.GetVendorByIdAsync(customer.VendorId);
            if (vendor?.PictureId > 0)
            {
                var vendorLogoUrl = await _pictureService.GetPictureUrlAsync(vendor.PictureId);
                if (!string.IsNullOrWhiteSpace(vendorLogoUrl))
                    logoUrl = vendorLogoUrl;
            }
        }

        return View(
            "~/Plugins/Misc.AIInterview/Views/Shared/Components/VendorPortalLogo/Default.cshtml",
            (object)logoUrl);
    }
}
