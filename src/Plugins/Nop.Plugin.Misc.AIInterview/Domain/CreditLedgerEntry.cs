using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class CreditLedgerEntry : BaseEntity
{
    public int CreditWalletId { get; set; }
    public decimal Amount { get; set; }
    public string TransactionType { get; set; }
    public string LedgerSource { get; set; }
    public int ProductId { get; set; }
    public int OrderId { get; set; }
    public int SponsorInviteId { get; set; }
    public int? InterviewSessionId { get; set; }
    public string Remarks { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}
