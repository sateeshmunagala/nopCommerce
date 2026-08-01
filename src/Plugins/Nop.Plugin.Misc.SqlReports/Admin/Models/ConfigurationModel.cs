using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.SqlReports.Admin.Models;

public record ConfigurationModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.SqlReports.Configuration.MaxRowsPerQuery")]
    public int MaxRowsPerQuery { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Configuration.CommandTimeoutSeconds")]
    public int CommandTimeoutSeconds { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Configuration.MaxCellLength")]
    public int MaxCellLength { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Configuration.EnableInstantQuery")]
    public bool EnableInstantQuery { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Configuration.AllowExport")]
    public bool AllowExport { get; set; }
}
