using System.Collections.Generic;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Services.Localization;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Payments.Razorpay.Models;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.Razorpay.Components;

[ViewComponent(Name = "RazorpayPaymentInfo")]
public class RazorpayPaymentInfoViewComponent : NopViewComponent
{
    private static readonly Dictionary<string, string> PaymentInfoResources = new()
    {
        ["Plugins.Payments.Razorpay.PaymentInfo.Success"] = "Payment verified successfully.",
        ["Plugins.Payments.Razorpay.PaymentInfo.PlacingOrder"] = "Payment verified. Placing your order...",
        ["Plugins.Payments.Razorpay.PaymentInfo.SaveDeferred"] = "Payment verified. Checkout is busy; wait, then select Continue to proceed.",
        ["Plugins.Payments.Razorpay.PaymentInfo.ConfirmationUnavailable"] = "Payment verified, but order confirmation is not ready. Use Confirm order when the checkout controls become available.",
        ["Plugins.Payments.Razorpay.PaymentInfo.TermsRequired"] = "Payment verified. Accept the terms and select Confirm order to finish.",
        ["Plugins.Payments.Razorpay.PaymentInfo.CaptchaRequired"] = "Payment verified. Complete the CAPTCHA, then select Confirm order.",
        ["Plugins.Payments.Razorpay.PaymentInfo.SaveFailed"] = "Payment was verified, but checkout could not save the payment details. Select Continue to retry."
    };

    private readonly ILocalizationService _localizationService;
    private readonly IWorkContext _workContext;

    public RazorpayPaymentInfoViewComponent(ILocalizationService localizationService, IWorkContext workContext)
    {
        _localizationService = localizationService;
        _workContext = workContext;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var language = await _workContext.GetWorkingLanguageAsync();
        if (language != null)
        {
            var missingResources = new Dictionary<string, string>();
            foreach (var resource in PaymentInfoResources)
            {
                if (await _localizationService.GetLocaleStringResourceByNameAsync(resource.Key, language.Id, false) == null)
                    missingResources.Add(resource.Key, resource.Value);
            }

            if (missingResources.Count > 0)
                await _localizationService.AddOrUpdateLocaleResourceAsync(missingResources, language.Id);
        }

        var model = new PaymentInfoModel();
        return View("~/Plugins/Payments.Razorpay/Views/PaymentInfo.cshtml", model);
    }
}
