using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AppointmentBooking.Services;
using Nop.Web.Framework.Components;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.AppointmentBooking.Components;

/// <summary>
/// Represents the product appointment booking view component
/// </summary>
public class ProductAppointmentBookingViewComponent : NopViewComponent
{
    #region Fields

    private readonly IAppointmentBookingService _appointmentBookingService;
    private readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public ProductAppointmentBookingViewComponent(IAppointmentBookingService appointmentBookingService,
        IWorkContext workContext)
    {
        _appointmentBookingService = appointmentBookingService;
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
        if (additionalData is not ProductDetailsModel productDetailsModel)
            return Content(string.Empty);

        if (!await _appointmentBookingService.IsAppointmentProductAsync(productDetailsModel.Id))
            return Content(string.Empty);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var model = await _appointmentBookingService.PrepareProductBookingModelAsync(productDetailsModel.Id, customer?.Id ?? 0);
        if (model == null)
            return Content(string.Empty);

        model.VendorName = productDetailsModel.VendorModel?.Name;
        model.VendorImageUrl = productDetailsModel.DefaultPictureModel?.ImageUrl;
        model.VendorImageAlt = !string.IsNullOrWhiteSpace(productDetailsModel.VendorModel?.Name)
            ? productDetailsModel.VendorModel.Name
            : productDetailsModel.DefaultPictureModel?.AlternateText;

        return await ViewAsync("~/Plugins/Misc.AppointmentBooking/Views/AppointmentBooking/ProductBooking.cshtml", model);
    }

    #endregion
}
