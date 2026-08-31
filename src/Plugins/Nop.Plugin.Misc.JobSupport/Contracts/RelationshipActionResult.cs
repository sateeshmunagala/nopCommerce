namespace Nop.Plugin.Misc.JobSupport.Contracts;

public partial class RelationshipActionResult
{
    public bool Succeeded { get; set; }
    public bool AlreadyApplied { get; set; }
    public RelationshipType RelationshipType { get; set; }
    public int SourceCustomerId { get; set; }
    public int TargetCustomerId { get; set; }
    public int ProfileProductId { get; set; }
    public string UserMessageKey { get; set; }
    public string ErrorCode { get; set; }
}
