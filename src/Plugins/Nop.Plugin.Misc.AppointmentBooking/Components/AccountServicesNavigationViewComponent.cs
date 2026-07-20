using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Web.Framework.Components;
using Nop.Web.Models.Customer;

namespace Nop.Plugin.Misc.AppointmentBooking.Components;

/// <summary>
/// Represents the vendor services account navigation item
/// </summary>
public class AccountServicesNavigationViewComponent : NopViewComponent
{
    private readonly AppointmentBookingSettings _appointmentBookingSettings;
    private readonly IWorkContext _workContext;

    public AccountServicesNavigationViewComponent(AppointmentBookingSettings appointmentBookingSettings,
        IWorkContext workContext)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _workContext = workContext;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        if (!_appointmentBookingSettings.Enabled || additionalData is not CustomerNavigationModel model)
            return Content(string.Empty);

        if (await _workContext.GetCurrentVendorAsync() == null)
            return Content(string.Empty);

        return await ViewAsync("~/Plugins/Misc.AppointmentBooking/Views/Components/AccountServicesNavigation/Default.cshtml", model);
    }
}
