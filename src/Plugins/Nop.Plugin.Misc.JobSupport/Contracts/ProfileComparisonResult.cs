namespace Nop.Plugin.Misc.JobSupport.Contracts;

public partial class ProfileComparisonResult
{
    public IList<int> MissingFromPlugin { get; set; } = new List<int>();
    public IList<int> UnexpectedInPlugin { get; set; } = new List<int>();
    public IList<string> OrderDifferences { get; set; } = new List<string>();
    public IList<string> FieldDifferences { get; set; } = new List<string>();
    public IList<string> PagingDifferences { get; set; } = new List<string>();

    public bool Matches => !MissingFromPlugin.Any() &&
        !UnexpectedInPlugin.Any() &&
        !OrderDifferences.Any() &&
        !FieldDifferences.Any() &&
        !PagingDifferences.Any();
}
