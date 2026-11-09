using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.SqlReports.Admin.Models;

public record SqlReportParameterSearchModel : BaseSearchModel
{
    public string SearchName { get; set; }
}
