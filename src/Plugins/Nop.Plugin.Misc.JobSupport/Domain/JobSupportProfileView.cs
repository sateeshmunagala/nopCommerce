using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport.Domain;

public partial class JobSupportProfileView : BaseEntity
{
    public int ViewerCustomerId { get; set; }
    public int ViewedCustomerId { get; set; }
    public int ViewerProfileId { get; set; }
    public int ViewedProfileId { get; set; }
    public DateTime FirstViewedOnUtc { get; set; }
    public DateTime LastViewedOnUtc { get; set; }
    public int ViewCount { get; set; }
    public bool ContactRevealed { get; set; }
    public DateTime? ContactRevealedOnUtc { get; set; }
    public int? LegacyShoppingCartItemId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
