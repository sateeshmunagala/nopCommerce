using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport.Domain;

public partial class JobSupportSubscription : BaseEntity
{
    public int CustomerId { get; set; }
    public int OrderId { get; set; }
    public int OrderItemId { get; set; }
    public int ProductId { get; set; }
    public int Status { get; set; }
    public DateTime StartOnUtc { get; set; }
    public DateTime EndOnUtc { get; set; }
    public int AllottedCredits { get; set; }
    public int CarriedCredits { get; set; }
    public int UsedCredits { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
    public int? LegacyRewardPointsHistoryId { get; set; }
    public string MigrationSource { get; set; }
}
