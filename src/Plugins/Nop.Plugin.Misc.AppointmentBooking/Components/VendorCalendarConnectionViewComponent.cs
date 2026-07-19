using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AppointmentBooking.Services;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Misc.AppointmentBooking.Components;

/// <summary>
/// Represents the vendor calendar connection view component
/// </summary>
public class VendorCalendarConnectionViewComponent : NopViewComponent
{
    #region Fields

    private readonly AppointmentBookingSettings _appointmentBookingSettings;
    private readonly ICalendarIntegrationService _calendarIntegrationService;
    private readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public VendorCalendarConnectionViewComponent(AppointmentBookingSettings appointmentBookingSettings,
        ICalendarIntegrationService calendarIntegrationService,
        IWorkContext workContext)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _calendarIntegrationService = calendarIntegrationService;
        _workContext = workContext;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Invoke view component
    /// </summary>
    /// <param name="widgetZone">Widget zone name</param>
    /// <param name="additionalData">Additional data</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the view component result
    /// </returns>
    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        if (!_appointmentBookingSettings.Enabled)
            return Content(string.Empty);

        var vendor = await _workContext.GetCurrentVendorAsync();
        if (vendor == null)
            return Content(string.Empty);

        var model = await _calendarIntegrationService.PrepareVendorConnectionModelAsync(vendor.Id);

        return await ViewAsync("~/Plugins/Misc.AppointmentBooking/Views/CalendarConnection/VendorConnection.cshtml", model);
    }

    #endregion
}
