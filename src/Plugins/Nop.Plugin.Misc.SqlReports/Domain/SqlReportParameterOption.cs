using Nop.Core;

namespace Nop.Plugin.Misc.SqlReports.Domain;

public class SqlReportParameterOption : BaseEntity
{
    public int SqlReportParameterId { get; set; }

    public string Value { get; set; }

    public string Text { get; set; }

    public int DisplayOrder { get; set; }
}
