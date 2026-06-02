using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class CreditLedgerEntry : BaseEntity
{
    public int CreditWalletId { get; set; }
    public decimal Amount { get; set; }
    public string TransactionType { get; set; }
    public string Remarks { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}
