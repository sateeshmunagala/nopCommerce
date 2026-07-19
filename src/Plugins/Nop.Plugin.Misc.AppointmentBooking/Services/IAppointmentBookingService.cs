using Nop.Plugin.Misc.AppointmentBooking.Models;

namespace Nop.Plugin.Misc.AppointmentBooking.Services;

/// <summary>
/// Represents appointment booking service
/// </summary>
public interface IAppointmentBookingService
{
    /// <summary>
    /// Determines whether a product should expose appointment booking UI
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task<bool> IsAppointmentProductAsync(int productId);

    /// <summary>
    /// Prepares product booking model
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <param name="customerId">Customer identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task<ProductAppointmentBookingModel> PrepareProductBookingModelAsync(int productId, int customerId);
}
