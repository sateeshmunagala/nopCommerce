using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport.Domain;

public partial class JobSupportContactReveal : BaseEntity
{
    public int SubscriptionId { get; set; }
    public int ViewerCustomerId { get; set; }
    public int TargetCustomerId { get; set; }
    public int TargetProfileId { get; set; }
    public int CreditCost { get; set; }
    public DateTime RevealedOnUtc { get; set; }
    public int? LegacyGenericAttributeId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}
