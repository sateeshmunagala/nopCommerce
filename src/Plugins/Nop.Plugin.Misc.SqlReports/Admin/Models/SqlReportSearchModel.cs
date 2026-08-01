using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.SqlReports.Admin.Models;

public record SqlReportSearchModel : BaseSearchModel
{
    public string SearchName { get; set; }

    public bool CanManageReports { get; set; }
}
