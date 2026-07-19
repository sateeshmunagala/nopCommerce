using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AppointmentBooking.Models;
using Nop.Plugin.Misc.AppointmentBooking.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Controllers;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.AppointmentBooking.Controllers;

/// <summary>
/// Represents appointment booking controller
/// </summary>
public class AppointmentBookingController : BasePublicController
{
    #region Fields

    private readonly AppointmentBookingSettings _appointmentBookingSettings;
    private readonly IAppointmentBookingService _appointmentBookingService;
    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly ISettingService _settingService;
    private readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public AppointmentBookingController(AppointmentBookingSettings appointmentBookingSettings,
        IAppointmentBookingService appointmentBookingService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        ISettingService settingService,
        IWorkContext workContext)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _appointmentBookingService = appointmentBookingService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _settingService = settingService;
        _workContext = workContext;
    }

    #endregion

    #region Methods

    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public IActionResult Configure()
    {
        var model = new ConfigurationModel
        {
            Enabled = _appointmentBookingSettings.Enabled,
            DefaultBookingUrl = _appointmentBookingSettings.DefaultBookingUrl,
            DefaultDurationMinutes = _appointmentBookingSettings.DefaultDurationMinutes,
            AllowCalendarIframe = _appointmentBookingSettings.AllowCalendarIframe,
            CalendarProvider = _appointmentBookingSettings.CalendarProvider
        };

        return View("~/Plugins/Misc.AppointmentBooking/Views/Configure.cshtml", model);
    }

    [HttpPost, ActionName("Configure")]
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [FormValueRequired("save")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return Configure();

        _appointmentBookingSettings.Enabled = model.Enabled;
        _appointmentBookingSettings.DefaultBookingUrl = model.DefaultBookingUrl?.Trim() ?? string.Empty;
        _appointmentBookingSettings.DefaultDurationMinutes = model.DefaultDurationMinutes;
        _appointmentBookingSettings.AllowCalendarIframe = model.AllowCalendarIframe;
        _appointmentBookingSettings.CalendarProvider = model.CalendarProvider?.Trim() ?? string.Empty;

        await _settingService.SaveSettingAsync(_appointmentBookingSettings);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AppointmentBooking.Configuration.Saved"));

        return Configure();
    }

    public async Task<IActionResult> ProductBooking(int productId)
    {
        if (!await _appointmentBookingService.IsAppointmentProductAsync(productId))
            return InvokeHttp404();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var model = await _appointmentBookingService.PrepareProductBookingModelAsync(productId, customer?.Id ?? 0);
        if (model == null)
            return InvokeHttp404();

        return View("~/Plugins/Misc.AppointmentBooking/Views/AppointmentBooking/ProductBooking.cshtml", model);
    }

    #endregion
}
