namespace Nop.Plugin.Misc.JobSupport.Contracts;

public partial class PagedProfileSearchResult
{
    public IList<ProfileSearchResult> Items { get; set; } = new List<ProfileSearchResult>();
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public int TotalRecords { get; set; }
    public int? OutputTotalRecords { get; set; }
    public int ReturnedRowCount { get; set; }
    public bool Succeeded { get; set; }
    public ProfileQueryErrorCode ErrorCode { get; set; }
    public ProfileQuerySource Source { get; set; }
    public IList<string> MappingWarnings { get; set; } = new List<string>();
}
