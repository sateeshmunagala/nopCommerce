using Nop.Plugin.Misc.AppointmentBooking.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Services;

/// <summary>
/// Represents calendar integration service
/// </summary>
public interface ICalendarIntegrationService
{
    /// <summary>
    /// Prepares vendor calendar connection model
    /// </summary>
    /// <param name="vendorId">Vendor identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task<CalendarConnectionModel> PrepareVendorConnectionModelAsync(int vendorId);

    /// <summary>
    /// Gets calendar connection URL
    /// </summary>
    /// <param name="vendorId">Vendor identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task<string> GetCalendarConnectionUrlAsync(int vendorId);
}
