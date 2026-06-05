using Nop.Web.Framework.Models;
using Nop.Web.Models.Checkout;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.SinglePageCheckout.Models;

public record SinglePageCheckoutPageModel : BaseNopModel
{
    public OnePageCheckoutModel CheckoutModel { get; set; }
    public ShoppingCartModel ShoppingCartModel { get; set; }
    public SinglePageCheckoutSettings Settings { get; set; }
}
