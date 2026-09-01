namespace Nop.Plugin.Misc.JobSupport.Contracts;

public partial class RelationshipQueryResult : ProfileCardResult
{
    public int RelationshipId { get; set; }
    public int RelationshipTypeId { get; set; }
    public int StatusId { get; set; }
    public DateTime RelationshipCreatedOnUtc { get; set; }
    public DateTime UpdatedOnUtc { get; set; }
}
