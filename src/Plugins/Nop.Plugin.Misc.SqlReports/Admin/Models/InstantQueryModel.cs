using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.SqlReports.Admin.Models;

public record InstantQueryModel : BaseNopModel
{
    public InstantQueryModel()
    {
        Result = new SqlReportResultModel();
    }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.InstantQuery.Fields.SqlQuery")]
    public string SqlQuery { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.InstantQuery.Fields.ParameterValues")]
    public string ParameterValues { get; set; }

    public SqlReportResultModel Result { get; set; }

    public bool AllowExport { get; set; }
}
