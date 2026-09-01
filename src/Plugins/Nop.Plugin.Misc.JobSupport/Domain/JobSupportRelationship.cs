using Nop.Core;

namespace Nop.Plugin.Misc.JobSupport.Domain;

public partial class JobSupportRelationship : BaseEntity
{
    public int SourceCustomerId { get; set; }
    public int TargetCustomerId { get; set; }
    public int SourceProfileId { get; set; }
    public int TargetProfileId { get; set; }
    public int RelationshipTypeId { get; set; }
    public int StatusId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
    public DateTime? RespondedOnUtc { get; set; }
    public int? LegacyShoppingCartItemId { get; set; }
    public string MetadataJson { get; set; }
}
