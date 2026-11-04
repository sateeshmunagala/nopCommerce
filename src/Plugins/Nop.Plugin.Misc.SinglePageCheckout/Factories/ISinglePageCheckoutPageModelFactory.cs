using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.SinglePageCheckout.Models;
using Nop.Web.Factories;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.SinglePageCheckout.Factories;

public interface ISinglePageCheckoutPageModelFactory
{
    Task<SinglePageCheckoutPageModel> PrepareAsync(IList<ShoppingCartItem> cart);
}
