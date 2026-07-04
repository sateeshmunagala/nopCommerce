using Nop.Web.Models.Checkout;
using Nop.Web.Models.ShoppingCart;
using System.Linq;

namespace Nop.Plugin.Misc.SinglePageCheckout.Factories;

public class SinglePageCheckoutModelTuner
{
    private readonly SinglePageCheckoutSettings _settings;

    public SinglePageCheckoutModelTuner(SinglePageCheckoutSettings settings)
    {
        _settings = settings;
    }

    public void TuneShoppingCartModel(ShoppingCartModel model)
    {
        if (model == null) return;

        model.HideCheckoutButton = true; // Always true for the sidebar, relies on main flow

        model.DiscountBox.Display = model.DiscountBox.Display && _settings.ShowDiscountBox;
        model.GiftCardBox.Display = model.GiftCardBox.Display && _settings.ShowGiftCardBox;

        if (!_settings.ShowCheckoutAttributes)
        {
            model.CheckoutAttributes.Clear();
        }

        if (!_settings.ShowOrderReviewData)
        {
            model.OrderReviewData.Display = false;
        }
    }

    public void TuneBillingAddressModel(CheckoutBillingAddressModel model, int customerBillingAddressId)
    {
        int? preferredAddressId = null;

        if (_settings.PreselectLastCustomerBillingAddress && customerBillingAddressId > 0)
        {
            preferredAddressId = customerBillingAddressId;
        }
        else if (_settings.PreselectDefaultCustomerAddress)
        {
            preferredAddressId = customerBillingAddressId > 0 ? customerBillingAddressId : model.ExistingAddresses.FirstOrDefault()?.Id;
        }

        if (preferredAddressId.HasValue)
        {
            var address = model.ExistingAddresses.FirstOrDefault(a => a.Id == preferredAddressId.Value);
            if (address != null)
            {
                model.ExistingAddresses.Remove(address);
                model.ExistingAddresses.Insert(0, address);
                model.NewAddressPreselected = false;
            }
        }

        if (_settings.EnableShipToSameAddressByDefault && model.ShipToSameAddressAllowed)
        {
            model.ShipToSameAddress = true;
        }

        if (_settings.DefaultBillingCountryId > 0)
        {
            if (!model.BillingNewAddress.CountryId.HasValue)
            {
                model.BillingNewAddress.CountryId = _settings.DefaultBillingCountryId;
            }
        }
    }

    public void TuneShippingAddressModel(CheckoutShippingAddressModel model, int customerShippingAddressId)
    {
        int? preferredAddressId = null;

        if (_settings.PreselectLastCustomerShippingAddress && customerShippingAddressId > 0)
        {
            preferredAddressId = customerShippingAddressId;
        }
        else if (_settings.PreselectDefaultCustomerAddress)
        {
            preferredAddressId = customerShippingAddressId > 0 ? customerShippingAddressId : model.ExistingAddresses.FirstOrDefault()?.Id;
        }

        if (preferredAddressId.HasValue)
        {
            var address = model.ExistingAddresses.FirstOrDefault(a => a.Id == preferredAddressId.Value);
            if (address != null)
            {
                model.ExistingAddresses.Remove(address);
                model.ExistingAddresses.Insert(0, address);
                model.NewAddressPreselected = false;
            }
        }
    }
}
