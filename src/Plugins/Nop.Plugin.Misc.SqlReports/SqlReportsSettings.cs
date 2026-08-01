using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.SqlReports;

public class SqlReportsSettings : ISettings
{
    public int MaxRowsPerQuery { get; set; }

    public int CommandTimeoutSeconds { get; set; }

    public int MaxCellLength { get; set; }

    public bool EnableInstantQuery { get; set; }

    public bool AllowExport { get; set; }
}
