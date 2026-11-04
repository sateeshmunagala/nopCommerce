using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Payments.Razorpay.Models;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.Razorpay.Components;

[ViewComponent(Name = "RazorpayPaymentInfo")]
public class RazorpayPaymentInfoViewComponent : NopViewComponent
{
    public IViewComponentResult Invoke()
    {
        var model = new PaymentInfoModel();
        return View("~/Plugins/Payments.Razorpay/Views/PaymentInfo.cshtml", model);
    }
}
