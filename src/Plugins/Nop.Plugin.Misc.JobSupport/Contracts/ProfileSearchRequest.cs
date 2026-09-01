namespace Nop.Plugin.Misc.JobSupport.Contracts;

public partial class ProfileSearchRequest
{
    public IList<int> ProfileIds { get; set; } = new List<int>();
    public int CustomerId { get; set; }
    public int? ProfileTypeId { get; set; }
    public IList<int> PrimarySkillIds { get; set; } = new List<int>();
    public IList<int> SecondarySkillIds { get; set; } = new List<int>();
    public string Availability { get; set; }
    public string Keywords { get; set; }
    public bool ExcludeOwnProfile { get; set; } = true;
    public int StoreId { get; set; }
    public RelationshipType? RelationshipType { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; } = 12;
    public int SortOrder { get; set; }
}
