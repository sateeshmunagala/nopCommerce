namespace Nop.Plugin.Misc.SqlReports.Services;

public class SqlReportExecutionResult
{
    public IList<string> Columns { get; set; } = new List<string>();

    public IList<IDictionary<string, object>> Rows { get; set; } = new List<IDictionary<string, object>>();

    public int RowsReturned { get; set; }

    public long ElapsedMilliseconds { get; set; }

    public bool Truncated { get; set; }
}
