using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.SqlReports.Admin.Models;

public record SqlReportParameterModel : BaseNopEntityModel
{
    public SqlReportParameterModel()
    {
        AvailableDataTypes = new List<SelectListItem>();
    }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Parameter.Fields.Name")]
    public string Name { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Parameter.Fields.ParameterName")]
    public string ParameterName { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Parameter.Fields.DataType")]
    public string DataType { get; set; }

    public IList<SelectListItem> AvailableDataTypes { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Parameter.Fields.DefaultValue")]
    public string DefaultValue { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Parameter.Fields.Prompt")]
    public string Prompt { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Parameter.Fields.IsRequired")]
    public bool IsRequired { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Parameter.Fields.DisplayOrder")]
    public int DisplayOrder { get; set; }
}
