using Nop.Plugin.Misc.AppointmentBooking.Models;
using Nop.Services.Catalog;

namespace Nop.Plugin.Misc.AppointmentBooking.Services;

/// <summary>
/// Represents appointment booking service
/// </summary>
public class AppointmentBookingService : IAppointmentBookingService
{
    #region Fields

    private readonly AppointmentBookingSettings _appointmentBookingSettings;
    private readonly IPriceFormatter _priceFormatter;
    private readonly IProductService _productService;

    #endregion

    #region Ctor

    public AppointmentBookingService(AppointmentBookingSettings appointmentBookingSettings,
        IPriceFormatter priceFormatter,
        IProductService productService)
    {
        _appointmentBookingSettings = appointmentBookingSettings;
        _priceFormatter = priceFormatter;
        _productService = productService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Determines whether a product should expose appointment booking UI
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task<bool> IsAppointmentProductAsync(int productId)
    {
        if (!_appointmentBookingSettings.Enabled || productId <= 0)
            return false;

        return await _productService.GetProductByIdAsync(productId) != null;
    }

    /// <summary>
    /// Prepares product booking model
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <param name="customerId">Customer identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task<ProductAppointmentBookingModel> PrepareProductBookingModelAsync(int productId, int customerId)
    {
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return null;

        return new ProductAppointmentBookingModel
        {
            Id = product.Id,
            ProductName = product.Name,
            ShortDescription = product.ShortDescription,
            Price = product.Price > decimal.Zero ? await _priceFormatter.FormatPriceAsync(product.Price) : string.Empty,
            BookingUrl = _appointmentBookingSettings.DefaultBookingUrl,
            DefaultDurationMinutes = _appointmentBookingSettings.DefaultDurationMinutes,
            AllowCalendarIframe = _appointmentBookingSettings.AllowCalendarIframe,
            CalendarProvider = _appointmentBookingSettings.CalendarProvider
        };
    }

    #endregion
}
