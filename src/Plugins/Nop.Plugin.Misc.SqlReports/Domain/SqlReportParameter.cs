using Nop.Core;

namespace Nop.Plugin.Misc.SqlReports.Domain;

public class SqlReportParameter : BaseEntity
{
    public string Name { get; set; }

    public string ParameterName { get; set; }

    public string DataType { get; set; }

    public string DefaultValue { get; set; }

    public string Prompt { get; set; }

    public bool IsRequired { get; set; }

    public int DisplayOrder { get; set; }
}
