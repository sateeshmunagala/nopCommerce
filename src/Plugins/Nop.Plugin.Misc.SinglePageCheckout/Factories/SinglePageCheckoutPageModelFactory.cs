using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.SinglePageCheckout.Models;
using Nop.Web.Factories;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.SinglePageCheckout.Factories;

public class SinglePageCheckoutPageModelFactory : ISinglePageCheckoutPageModelFactory
{
    private readonly ICheckoutModelFactory _checkoutModelFactory;
    private readonly IShoppingCartModelFactory _shoppingCartModelFactory;
    private readonly SinglePageCheckoutModelTuner _tuner;
    private readonly SinglePageCheckoutSettings _settings;

    public SinglePageCheckoutPageModelFactory(
        ICheckoutModelFactory checkoutModelFactory,
        IShoppingCartModelFactory shoppingCartModelFactory,
        SinglePageCheckoutModelTuner tuner,
        SinglePageCheckoutSettings settings)
    {
        _checkoutModelFactory = checkoutModelFactory;
        _shoppingCartModelFactory = shoppingCartModelFactory;
        _tuner = tuner;
        _settings = settings;
    }

    public async Task<SinglePageCheckoutPageModel> PrepareAsync(IList<ShoppingCartItem> cart)
    {
        var model = new SinglePageCheckoutPageModel
        {
            Settings = _settings
        };

        model.CheckoutModel = await _checkoutModelFactory.PrepareOnePageCheckoutModelAsync(cart);

        if (_settings.ShowCartOnCheckout)
        {
            model.ShoppingCartModel = await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(
                new ShoppingCartModel(),
                cart,
                isEditable: _settings.AllowCartItemEditing,
                validateCheckoutAttributes: false,
                prepareAndDisplayOrderReviewData: _settings.ShowOrderReviewData);

            _tuner.TuneShoppingCartModel(model.ShoppingCartModel);
        }

        return model;
    }
}
