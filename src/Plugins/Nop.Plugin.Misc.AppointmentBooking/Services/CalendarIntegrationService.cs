using Nop.Plugin.Misc.AppointmentBooking.Models;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.AppointmentBooking.Services;

/// <summary>
/// Represents calendar integration service
/// </summary>
public class CalendarIntegrationService : ICalendarIntegrationService
{
    #region Fields

    private readonly AppointmentBookingSettings _appointmentBookingSettings;
    private readonly INopUrlHelper _nopUrlHelper;

    #endregion

    #region Ctor

    public CalendarIntegrationService(AppointmentBookingSettings appointmentBookingSettings,
        INopUrlHelper nopUrlHelper)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _nopUrlHelper = nopUrlHelper;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Prepares vendor calendar connection model
    /// </summary>
    /// <param name="vendorId">Vendor identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task<CalendarConnectionModel> PrepareVendorConnectionModelAsync(int vendorId)
    {
        var model = new CalendarConnectionModel
        {
            VendorId = vendorId,
            CalendarProvider = _appointmentBookingSettings.CalendarProvider,
            ConnectUrl = _nopUrlHelper.RouteUrl(AppointmentBookingDefaults.CalendarConnectRouteName),
            DisconnectUrl = _nopUrlHelper.RouteUrl(AppointmentBookingDefaults.CalendarDisconnectRouteName),
            IsConnected = false,
            StatusMessage = "No calendar is connected yet."
        };

        return Task.FromResult(model);
    }

    /// <summary>
    /// Gets calendar connection URL
    /// </summary>
    /// <param name="vendorId">Vendor identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task<string> GetCalendarConnectionUrlAsync(int vendorId)
    {
        return Task.FromResult(_nopUrlHelper.RouteUrl(AppointmentBookingDefaults.CalendarConnectRouteName));
    }

    #endregion
}
