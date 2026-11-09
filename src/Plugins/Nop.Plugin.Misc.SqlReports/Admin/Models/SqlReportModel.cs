using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.SqlReports.Admin.Models;

public record SqlReportModel : BaseNopEntityModel
{
    public SqlReportModel()
    {
        SelectedCustomerRoleIds = new List<int>();
        AvailableCustomerRoles = new List<SelectListItem>();
        SelectedParameterIds = new List<int>();
        AvailableParameters = new List<SelectListItem>();
    }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Report.Fields.Name")]
    public string Name { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Report.Fields.SystemName")]
    public string SystemName { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Report.Fields.Description")]
    public string Description { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Report.Fields.SqlQuery")]
    public string SqlQuery { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Report.Fields.IsActive")]
    public bool IsActive { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Report.Fields.DisplayOrder")]
    public int DisplayOrder { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Report.Fields.CustomerRoles")]
    public IList<int> SelectedCustomerRoleIds { get; set; }

    public IList<SelectListItem> AvailableCustomerRoles { get; set; }

    public string CustomerRoleNames { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Report.Fields.Parameters")]
    public IList<int> SelectedParameterIds { get; set; }

    public IList<SelectListItem> AvailableParameters { get; set; }

    public string ParameterNames { get; set; }
}
