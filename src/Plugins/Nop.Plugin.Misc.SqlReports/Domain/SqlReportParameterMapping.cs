using Nop.Core;

namespace Nop.Plugin.Misc.SqlReports.Domain;

public class SqlReportParameterMapping : BaseEntity
{
    public int SqlReportId { get; set; }

    public int SqlReportParameterId { get; set; }
}
