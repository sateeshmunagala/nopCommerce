using Nop.Core;

namespace Nop.Plugin.Misc.AIInterview.Domain;

public class SponsorInvite : BaseEntity
{
    public int SponsorId { get; set; }
    public int ProductId { get; set; }
    public string Email { get; set; }
    public string InviteCode { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime? ExpiryDateUtc { get; set; }
    public bool IsActive { get; set; }
    public bool IsAccepted { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}
