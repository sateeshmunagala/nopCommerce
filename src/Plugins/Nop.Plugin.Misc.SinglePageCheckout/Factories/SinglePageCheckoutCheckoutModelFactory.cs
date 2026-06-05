using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Orders;
using Nop.Services.Payments;
using Nop.Web.Factories;
using Nop.Web.Models.Checkout;

namespace Nop.Plugin.Misc.SinglePageCheckout.Factories;

public class SinglePageCheckoutCheckoutModelFactory : ICheckoutModelFactory
{
    private readonly CheckoutModelFactory _coreCheckoutModelFactory;
    private readonly SinglePageCheckoutSettings _settings;
    private readonly SinglePageCheckoutModelTuner _tuner;
    private readonly IWorkContext _workContext;

    public SinglePageCheckoutCheckoutModelFactory(
        CheckoutModelFactory coreCheckoutModelFactory,
        SinglePageCheckoutSettings settings,
        SinglePageCheckoutModelTuner tuner,
        IWorkContext workContext)
    {
        _coreCheckoutModelFactory = coreCheckoutModelFactory;
        _settings = settings;
        _tuner = tuner;
        _workContext = workContext;
    }

    public async Task PrepareBillingAddressModelAsync(CheckoutBillingAddressModel model, IList<ShoppingCartItem> cart, int? selectedCountryId = null, bool prePopulateNewAddressWithCustomerFields = false, string overrideAttributesXml = "")
    {
        await _coreCheckoutModelFactory.PrepareBillingAddressModelAsync(model, cart, selectedCountryId, prePopulateNewAddressWithCustomerFields, overrideAttributesXml);

        if (_settings.Enabled)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            _tuner.TuneBillingAddressModel(model, customer.BillingAddressId ?? 0);
        }
    }

    public async Task PrepareShippingAddressModelAsync(CheckoutShippingAddressModel model, IList<ShoppingCartItem> cart, int? selectedCountryId = null, bool prePopulateNewAddressWithCustomerFields = false, string overrideAttributesXml = "")
    {
        await _coreCheckoutModelFactory.PrepareShippingAddressModelAsync(model, cart, selectedCountryId, prePopulateNewAddressWithCustomerFields, overrideAttributesXml);

        if (_settings.Enabled)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            _tuner.TuneShippingAddressModel(model, customer.ShippingAddressId ?? 0);
        }
    }

    public async Task<OnePageCheckoutModel> PrepareOnePageCheckoutModelAsync(IList<ShoppingCartItem> cart)
    {
        var model = await _coreCheckoutModelFactory.PrepareOnePageCheckoutModelAsync(cart);

        if (_settings.Enabled)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            _tuner.TuneBillingAddressModel(model.BillingAddress, customer.BillingAddressId ?? 0);
        }

        return model;
    }

    // Pass-through methods

    public Task<CheckoutShippingMethodModel> PrepareShippingMethodModelAsync(IList<ShoppingCartItem> cart, Address shippingAddress)
    {
        return _coreCheckoutModelFactory.PrepareShippingMethodModelAsync(cart, shippingAddress);
    }

    public Task<CheckoutPaymentMethodModel> PreparePaymentMethodModelAsync(IList<ShoppingCartItem> cart, int filterByCountryId)
    {
        return _coreCheckoutModelFactory.PreparePaymentMethodModelAsync(cart, filterByCountryId);
    }

    public Task<CheckoutPaymentInfoModel> PreparePaymentInfoModelAsync(IPaymentMethod paymentMethod)
    {
        return _coreCheckoutModelFactory.PreparePaymentInfoModelAsync(paymentMethod);
    }

    public Task<CheckoutConfirmModel> PrepareConfirmOrderModelAsync(IList<ShoppingCartItem> cart)
    {
        return _coreCheckoutModelFactory.PrepareConfirmOrderModelAsync(cart);
    }

    public Task<CheckoutCompletedModel> PrepareCheckoutCompletedModelAsync(Order order)
    {
        return _coreCheckoutModelFactory.PrepareCheckoutCompletedModelAsync(order);
    }

    public Task<CheckoutProgressModel> PrepareCheckoutProgressModelAsync(CheckoutProgressStep step)
    {
        return _coreCheckoutModelFactory.PrepareCheckoutProgressModelAsync(step);
    }
}
