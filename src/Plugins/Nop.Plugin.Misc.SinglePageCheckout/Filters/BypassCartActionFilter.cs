using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nop.Core.Domain.Orders;
using Nop.Services.Configuration;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.SinglePageCheckout.Filters;

public class BypassCartActionFilter : IAsyncActionFilter
{
    private readonly ISettingService _settingService;

    public BypassCartActionFilter(ISettingService settingService)
    {
        _settingService = settingService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.Controller.GetType().Name == "CheckoutController")
        {
            if (context.ActionDescriptor.RouteValues.TryGetValue("action", out var actionName) &&
                (actionName == "Index" || actionName == "OpcIndex"))
            {
                var settings = await _settingService.LoadSettingAsync<SinglePageCheckoutSettings>();
                if (settings.Enabled && settings.BypassCart)
                {
                    context.Result = new RedirectToRouteResult(SinglePageCheckoutDefaults.CheckoutRouteName, null);
                    return;
                }
            }
        }

        var executedContext = await next();

        // After the action executes, intercept if it's trying to redirect to standard Checkout
        if (executedContext.Result is RedirectToRouteResult redirectResult)
        {
            if (redirectResult.RouteName == "Checkout" || redirectResult.RouteName == "CheckoutOnePage")
            {
                var settings = await _settingService.LoadSettingAsync<SinglePageCheckoutSettings>();
                if (settings.Enabled && settings.BypassCart)
                {
                    executedContext.Result = new RedirectToRouteResult(SinglePageCheckoutDefaults.CheckoutRouteName, null);
                }
            }
        }
    }
}
