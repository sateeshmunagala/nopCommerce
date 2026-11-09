using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Plugin.Misc.SqlReports.Services;

namespace Nop.Plugin.Misc.SqlReports.Admin.Models;

public record SqlReportRunModel : BaseNopEntityModel
{
    public SqlReportRunModel()
    {
        Parameters = new List<SqlReportRunParameterModel>();
        Result = new SqlReportResultModel();
    }

    public string Name { get; set; }

    public string Description { get; set; }

    public string SqlQuery { get; set; }

    public IList<SqlReportRunParameterModel> Parameters { get; set; }

    public SqlReportResultModel Result { get; set; }

    public bool AllowExport { get; set; }
}

public record SqlReportRunParameterModel : BaseNopModel
{
    public SqlReportRunParameterModel()
    {
        SelectedValues = new List<string>();
        AvailableOptions = new List<SelectListItem>();
    }

    public int ParameterId { get; set; }

    public string Name { get; set; }

    public string ParameterName { get; set; }

    public string DataType { get; set; }

    public string Prompt { get; set; }

    public bool IsRequired { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Run.ParameterValue")]
    public string Value { get; set; }

    public IList<string> SelectedValues { get; set; }

    public IList<SelectListItem> AvailableOptions { get; set; }

    public bool IsList => SqlReportDataType.IsList(DataType);

    public bool IsNumber => SqlReportDataType.IsNumber(DataType);
}
