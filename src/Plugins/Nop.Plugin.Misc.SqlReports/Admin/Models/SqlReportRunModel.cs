using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

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
}

public record SqlReportRunParameterModel : BaseNopModel
{
    public int ParameterId { get; set; }

    public string Name { get; set; }

    public string ParameterName { get; set; }

    public string DataType { get; set; }

    public string Prompt { get; set; }

    public bool IsRequired { get; set; }

    [NopResourceDisplayName("Plugins.Misc.SqlReports.Run.ParameterValue")]
    public string Value { get; set; }
}
