using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.SinglePageCheckout.Models;
using Nop.Services.Configuration;
using Nop.Web.Framework.Components;
using Nop.Web.Framework.Infrastructure;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.SinglePageCheckout.Components;

public class SinglePageCheckoutBuyNowViewComponent : NopViewComponent
{
    private readonly ISettingService _settingService;

    public SinglePageCheckoutBuyNowViewComponent(ISettingService settingService)
    {
        _settingService = settingService;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        var settings = await _settingService.LoadSettingAsync<SinglePageCheckoutSettings>();

        if (!settings.Enabled || !settings.EnableBuyNow)
            return Content("");

        if (widgetZone == PublicWidgetZones.ProductDetailsAddInfo && !settings.ShowBuyNowOnProductDetails)
            return Content("");

        if (widgetZone == PublicWidgetZones.ProductBoxAddinfoAfter && !settings.ShowBuyNowOnProductBoxes)
            return Content("");

        int productId = 0;

        if (additionalData is ProductDetailsModel productDetails)
        {
            productId = productDetails.Id;
        }
        else if (additionalData is ProductOverviewModel productOverview)
        {
            productId = productOverview.Id;
        }
        else if (additionalData is int id)
        {
            productId = id;
        }

        if (productId <= 0)
            return Content("");

        var model = new BuyNowButtonModel
        {
            ProductId = productId,
            WidgetZone = widgetZone
        };

        return await ViewAsync("~/Plugins/Misc.SinglePageCheckout/Views/Components/SinglePageCheckoutBuyNow/Default.cshtml", model);
    }
}
