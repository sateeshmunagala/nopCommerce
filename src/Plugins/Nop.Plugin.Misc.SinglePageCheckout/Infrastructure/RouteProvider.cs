using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.SinglePageCheckout.Infrastructure;

public class RouteProvider : IRouteProvider
{
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(
            name: SinglePageCheckoutDefaults.CheckoutRouteName,
            pattern: "singlepagecheckout",
            defaults: new { controller = "SinglePageCheckout", action = "Index" });

        endpointRouteBuilder.MapControllerRoute(
            name: SinglePageCheckoutDefaults.SummaryRouteName,
            pattern: "singlepagecheckout/sidebar",
            defaults: new { controller = "SinglePageCheckout", action = "Summary" });

        endpointRouteBuilder.MapControllerRoute(
            name: SinglePageCheckoutDefaults.BuyNowRouteName,
            pattern: "singlepagecheckout/buy-now/{productId:min(1)}",
            defaults: new { controller = "SinglePageCheckout", action = "BuyNow" });

        endpointRouteBuilder.MapControllerRoute(
            name: "Plugin.Misc.SinglePageCheckout.Configure",
            pattern: "Admin/SinglePageCheckoutAdmin/Configure",
            defaults: new { controller = "SinglePageCheckoutAdmin", action = "Configure", area = AreaNames.ADMIN });
    }

    public int Priority => 0;
}
