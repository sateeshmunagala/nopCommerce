using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class CreditPurchaseGrant : BaseEntity
{
    public int OrderId { get; set; }
    public int OrderItemId { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string Sku { get; set; }
    public int Quantity { get; set; }
    public int CreditsPerUnit { get; set; }
    public int CreditsGranted { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}
