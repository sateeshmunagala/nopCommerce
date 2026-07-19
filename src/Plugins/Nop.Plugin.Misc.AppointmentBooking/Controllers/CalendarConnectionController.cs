using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Http;
using Nop.Plugin.Misc.AppointmentBooking.Services;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Web.Controllers;

namespace Nop.Plugin.Misc.AppointmentBooking.Controllers;

/// <summary>
/// Represents calendar connection controller
/// </summary>
public class CalendarConnectionController : BasePublicController
{
    #region Fields

    private readonly ICalendarIntegrationService _calendarIntegrationService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public CalendarConnectionController(ICalendarIntegrationService calendarIntegrationService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IWorkContext workContext)
    {
        _calendarIntegrationService = calendarIntegrationService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _workContext = workContext;
    }

    #endregion

    #region Utilities

    protected virtual IActionResult RedirectToVendorInfo()
    {
        return RedirectToRoute(NopRouteNames.Standard.CUSTOMER_VENDOR_INFO);
    }

    #endregion

    #region Methods

    public async Task<IActionResult> Connect()
    {
        var vendor = await _workContext.GetCurrentVendorAsync();
        if (vendor == null)
            return Challenge();

        _ = await _calendarIntegrationService.GetCalendarConnectionUrlAsync(vendor.Id);
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AppointmentBooking.Calendar.ConnectPlaceholder"));

        return RedirectToVendorInfo();
    }

    public IActionResult Callback()
    {
        return RedirectToVendorInfo();
    }

    public async Task<IActionResult> Disconnect()
    {
        var vendor = await _workContext.GetCurrentVendorAsync();
        if (vendor == null)
            return Challenge();

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AppointmentBooking.Calendar.DisconnectPlaceholder"));

        return RedirectToVendorInfo();
    }

    #endregion
}
