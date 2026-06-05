using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.SinglePageCheckout;

public class SinglePageCheckoutSettings : ISettings
{
    public bool Enabled { get; set; }
    public bool BypassCart { get; set; }
    public bool ShowCartOnCheckout { get; set; }
    public bool AllowCartItemEditing { get; set; }
    public bool ShowDiscountBox { get; set; }
    public bool ShowGiftCardBox { get; set; }
    public bool ShowCheckoutAttributes { get; set; }
    public bool ShowOrderReviewData { get; set; }
    public bool ShowEstimateShipping { get; set; }
    public bool EnableBuyNow { get; set; }
    public bool ShowBuyNowOnProductDetails { get; set; }
    public bool ShowBuyNowOnProductBoxes { get; set; }
    public bool PreselectDefaultCustomerAddress { get; set; }
    public bool PreselectLastCustomerBillingAddress { get; set; }
    public bool PreselectLastCustomerShippingAddress { get; set; }
    public bool EnableShipToSameAddressByDefault { get; set; }
    public int DefaultBillingCountryId { get; set; }
}