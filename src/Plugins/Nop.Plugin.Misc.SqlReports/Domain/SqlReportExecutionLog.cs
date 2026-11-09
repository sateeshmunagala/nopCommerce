using Nop.Core;

namespace Nop.Plugin.Misc.SqlReports.Domain;

public class SqlReportExecutionLog : BaseEntity
{
    public int? SqlReportId { get; set; }

    public int CustomerId { get; set; }

    public long DurationMs { get; set; }

    public int RowsReturned { get; set; }

    public bool Success { get; set; }

    public string Error { get; set; }

    public DateTime CreatedOnUtc { get; set; }
}
