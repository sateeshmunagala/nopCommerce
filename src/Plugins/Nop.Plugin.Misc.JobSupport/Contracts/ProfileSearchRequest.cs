namespace Nop.Plugin.Misc.JobSupport.Contracts;

public partial class ProfileSearchRequest
{
    public IList<int> ProductIds { get; set; } = new List<int>();
    public int CustomerId { get; set; }
    public int? ProfileTypeId { get; set; }
    public int StoreId { get; set; }
    public RelationshipType? RelationshipType { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; } = 12;
    public int SortOrder { get; set; }
}
