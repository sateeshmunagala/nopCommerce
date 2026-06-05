namespace Nop.Plugin.Misc.SinglePageCheckout.Models;

public record BuyNowButtonModel
{
    public int ProductId { get; set; }
    public string WidgetZone { get; set; }
}
