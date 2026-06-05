using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.SinglePageCheckout.Models;

public record ConfigurationModel : BaseNopModel
{
    public int ActiveStoreScopeConfiguration { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.Enabled")]
    public bool Enabled { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.BypassCart")]
    public bool BypassCart { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.ShowCartOnCheckout")]
    public bool ShowCartOnCheckout { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.AllowCartItemEditing")]
    public bool AllowCartItemEditing { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.ShowDiscountBox")]
    public bool ShowDiscountBox { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.ShowGiftCardBox")]
    public bool ShowGiftCardBox { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.ShowCheckoutAttributes")]
    public bool ShowCheckoutAttributes { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.ShowOrderReviewData")]
    public bool ShowOrderReviewData { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.ShowEstimateShipping")]
    public bool ShowEstimateShipping { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.EnableBuyNow")]
    public bool EnableBuyNow { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.ShowBuyNowOnProductDetails")]
    public bool ShowBuyNowOnProductDetails { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.ShowBuyNowOnProductBoxes")]
    public bool ShowBuyNowOnProductBoxes { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.PreselectDefaultCustomerAddress")]
    public bool PreselectDefaultCustomerAddress { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.PreselectLastCustomerBillingAddress")]
    public bool PreselectLastCustomerBillingAddress { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.PreselectLastCustomerShippingAddress")]
    public bool PreselectLastCustomerShippingAddress { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.EnableShipToSameAddressByDefault")]
    public bool EnableShipToSameAddressByDefault { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SinglePageCheckout.Fields.DefaultBillingCountryId")]
    public int DefaultBillingCountryId { get; set; }
}
