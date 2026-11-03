using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class CreditWallet : BaseEntity
{
    public int CustomerId { get; set; }
    public decimal Balance { get; set; }
}
