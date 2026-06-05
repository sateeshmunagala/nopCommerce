using FluentMigrator;
using Nop.Data;
using Nop.Data.Migrations;
using Nop.Web.Framework.Extensions;

namespace Nop.Plugin.Misc.SinglePageCheckout.Data.Migrations;

[NopMigration("2025/11/02 11:00:00:0000000", "Misc.SinglePageCheckout add locales", MigrationProcessType.Installation)]
public class AddLocales : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        this.AddOrUpdateLocaleResource(new Dictionary<string, string>
        {
            ["Plugins.Misc.SinglePageCheckout.BuyNow.Button"] = "Buy Now",
            ["Plugins.Misc.SinglePageCheckout.Configuration.Title"] = "Single Page Checkout Configuration",
            ["Plugins.Misc.SinglePageCheckout.Fields.Enabled"] = "Enabled",
            ["Plugins.Misc.SinglePageCheckout.Fields.BypassCart"] = "Bypass Cart",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowCartOnCheckout"] = "Show Cart On Checkout",
            ["Plugins.Misc.SinglePageCheckout.Fields.AllowCartItemEditing"] = "Allow Cart Item Editing",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowDiscountBox"] = "Show Discount Box",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowGiftCardBox"] = "Show Gift Card Box",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowCheckoutAttributes"] = "Show Checkout Attributes",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowOrderReviewData"] = "Show Order Review Data",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowEstimateShipping"] = "Show Estimate Shipping",
            ["Plugins.Misc.SinglePageCheckout.Fields.EnableBuyNow"] = "Enable Buy Now",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowBuyNowOnProductDetails"] = "Show Buy Now On Product Details",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowBuyNowOnProductBoxes"] = "Show Buy Now On Product Boxes",
            ["Plugins.Misc.SinglePageCheckout.Fields.PreselectDefaultCustomerAddress"] = "Preselect Default Customer Address",
            ["Plugins.Misc.SinglePageCheckout.Fields.PreselectLastCustomerBillingAddress"] = "Preselect Last Customer Billing Address",
            ["Plugins.Misc.SinglePageCheckout.Fields.PreselectLastCustomerShippingAddress"] = "Preselect Last Customer Shipping Address",
            ["Plugins.Misc.SinglePageCheckout.Fields.EnableShipToSameAddressByDefault"] = "Enable Ship To Same Address By Default",
            ["Plugins.Misc.SinglePageCheckout.Fields.DefaultBillingCountryId"] = "Default Billing Country Id",

            // Hints
            ["Plugins.Misc.SinglePageCheckout.Fields.Enabled.Hint"] = "Enable or disable the single page checkout functionality.",
            ["Plugins.Misc.SinglePageCheckout.Fields.BypassCart.Hint"] = "When enabled, navigating to checkout from the cart will go directly to single page checkout instead of the standard checkout route.",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowCartOnCheckout.Hint"] = "Show the shopping cart sidebar summary on the checkout page.",
            ["Plugins.Misc.SinglePageCheckout.Fields.AllowCartItemEditing.Hint"] = "Allow users to edit cart item quantities or remove them from the sidebar summary.",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowDiscountBox.Hint"] = "Show the discount code entry box in the sidebar summary.",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowGiftCardBox.Hint"] = "Show the gift card entry box in the sidebar summary.",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowCheckoutAttributes.Hint"] = "Show checkout attributes in the sidebar summary.",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowOrderReviewData.Hint"] = "Show order review data in the sidebar summary.",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowEstimateShipping.Hint"] = "Show the estimate shipping component in the sidebar summary.",
            ["Plugins.Misc.SinglePageCheckout.Fields.EnableBuyNow.Hint"] = "Enable the Buy Now feature across configured widget zones.",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowBuyNowOnProductDetails.Hint"] = "Show the Buy Now button on the product details page.",
            ["Plugins.Misc.SinglePageCheckout.Fields.ShowBuyNowOnProductBoxes.Hint"] = "Show the Buy Now button on product box listings.",
            ["Plugins.Misc.SinglePageCheckout.Fields.PreselectDefaultCustomerAddress.Hint"] = "Automatically select the customer's default address (or first existing) for billing/shipping if last-address flags are disabled.",
            ["Plugins.Misc.SinglePageCheckout.Fields.PreselectLastCustomerBillingAddress.Hint"] = "Automatically select the customer's last used billing address.",
            ["Plugins.Misc.SinglePageCheckout.Fields.PreselectLastCustomerShippingAddress.Hint"] = "Automatically select the customer's last used shipping address.",
            ["Plugins.Misc.SinglePageCheckout.Fields.EnableShipToSameAddressByDefault.Hint"] = "Automatically check 'Ship to the same address' if applicable.",
            ["Plugins.Misc.SinglePageCheckout.Fields.DefaultBillingCountryId.Hint"] = "Set a default country for billing addresses (if no country is currently set).",
        });
    }

    public override void Down()
    {
        //nothing
    }
}
