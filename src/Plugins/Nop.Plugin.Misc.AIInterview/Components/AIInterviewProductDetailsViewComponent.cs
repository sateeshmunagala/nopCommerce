using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Web.Framework.Components;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.AIInterview.Components;

public class AIInterviewProductDetailsViewComponent : NopViewComponent
{
    private readonly ICreditService _creditService;
    private readonly IProductService _productService;
    private readonly IProductTemplateService _productTemplateService;
    private readonly IWorkContext _workContext;

    public AIInterviewProductDetailsViewComponent(ICreditService creditService,
        IWorkContext workContext,
        IProductService productService,
        IProductTemplateService productTemplateService)
    {
        _creditService = creditService;
        _workContext = workContext;
        _productService = productService;
        _productTemplateService = productTemplateService;
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
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return Content("");

        var productTemplate = await _productTemplateService.GetProductTemplateByIdAsync(product.ProductTemplateId);
        if (productTemplate == null ||
            !string.Equals(productTemplate.ViewPath, AIInterviewDefaults.JobProductTemplateViewPath, StringComparison.OrdinalIgnoreCase))
            return Content("");

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null)
            return Content("");

        var wallet = await _creditService.GetOrCreateWalletAsync(customer.Id);
        var hasCredits = wallet.Balance >= 1;

        ViewBag.HasCredits = hasCredits;
        ViewBag.ProductId = productId;

        var sponsorToken = HttpContext?.Request?.Query?["sponsorToken"].ToString() ?? "";
        ViewBag.SponsorToken = sponsorToken;

        // Verify sponsor token validity
        bool hasSponsorCredits = false;
        if (!string.IsNullOrEmpty(sponsorToken))
        {
            var inviteService = HttpContext.RequestServices.GetService(typeof(ISponsorInviteService)) as ISponsorInviteService;
            if (inviteService != null)
            {
                var invite = await inviteService.GetSponsorInviteByCodeAsync(sponsorToken);
                if (invite != null && !invite.IsAccepted && (!invite.ExpiryDateUtc.HasValue || invite.ExpiryDateUtc > DateTime.UtcNow) && invite.Email.Equals(customer.Email, StringComparison.OrdinalIgnoreCase))
                {
                    var sponsorWallet = await _creditService.GetOrCreateWalletAsync(invite.SponsorId);
                    if (sponsorWallet.Balance >= 1)
                    {
                        hasSponsorCredits = true;
                    }
                }
            }
        }

        ViewBag.HasSponsorCredits = hasSponsorCredits;

        return View("~/Plugins/Misc.AIInterview/Views/Shared/Components/AIInterviewProductDetails/Default.cshtml", model);
    }
}
