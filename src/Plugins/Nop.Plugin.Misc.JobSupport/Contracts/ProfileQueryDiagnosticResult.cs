namespace Nop.Plugin.Misc.JobSupport.Contracts;

public partial class ProfileQueryDiagnosticResult
{
    public string ProcedureName { get; set; }
    public bool Succeeded { get; set; }
    public int ReturnedRowCount { get; set; }
    public int? OutputTotalRecords { get; set; }
    public long DurationMilliseconds { get; set; }
    public IList<int> ProfileIds { get; set; } = new List<int>();
    public IList<string> MappingWarnings { get; set; } = new List<string>();
    public ProfileQueryErrorCode ErrorCode { get; set; }
}
