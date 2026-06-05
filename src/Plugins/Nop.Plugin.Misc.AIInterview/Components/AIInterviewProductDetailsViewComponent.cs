using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Web.Framework.Components;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class AIInterviewProductDetailsViewComponent : NopViewComponent
{
    private readonly ICreditService _creditService;
    private readonly IWorkContext _workContext;

    public AIInterviewProductDetailsViewComponent(ICreditService creditService,
        IWorkContext workContext)
    {
        _creditService = creditService;
        _workContext = workContext;
    }

    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        var model = additionalData as BaseNopModel;
        if (model == null || model.GetType().Name != "ProductDetailsModel")
            return Content("");

        var productIdProperty = model.GetType().GetProperty("Id");
        if (productIdProperty == null)
            return Content("");

        var productId = (int)productIdProperty.GetValue(model);

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Content("");

        var wallet = await _creditService.GetOrCreateWalletAsync(customer.Id);
        var hasCredits = wallet.Balance >= 1;

        ViewBag.HasCredits = hasCredits;
        ViewBag.ProductId = productId;

        var sponsorToken = HttpContext.Request.Query["sponsorToken"].ToString();
        ViewBag.SponsorToken = sponsorToken;

        return View("~/Plugins/Misc.AIInterview/Views/Shared/Components/AIInterviewProductDetails/Default.cshtml");
    }
}
