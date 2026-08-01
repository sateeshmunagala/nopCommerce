using FluentMigrator;
using Nop.Data;
using Nop.Data.Migrations;
using Nop.Web.Framework.Extensions;

namespace Nop.Plugin.Misc.SqlReports.Data.Migrations;

[NopMigration("2026/08/01 12:01:00", "Misc.SqlReports locale resources", MigrationProcessType.Installation)]
public class AddLocales : Migration
{
    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        this.AddOrUpdateLocaleResource(new Dictionary<string, string>
        {
            ["Plugins.Misc.SqlReports.Menu"] = "SQL reports",
            ["Plugins.Misc.SqlReports.Reports"] = "SQL reports",
            ["Plugins.Misc.SqlReports.Parameters"] = "Parameters",
            ["Plugins.Misc.SqlReports.InstantQuery"] = "Instant query",
            ["Plugins.Misc.SqlReports.Run"] = "Run",
            ["Plugins.Misc.SqlReports.BackToReports"] = "back to reports",
            ["Plugins.Misc.SqlReports.BackToParameters"] = "back to parameters",
            ["Plugins.Misc.SqlReports.Report.AddNew"] = "Add a new SQL report",
            ["Plugins.Misc.SqlReports.Report.Edit"] = "Edit SQL report",
            ["Plugins.Misc.SqlReports.Report.Fields.Name"] = "Name",
            ["Plugins.Misc.SqlReports.Report.Fields.Name.Required"] = "Name is required.",
            ["Plugins.Misc.SqlReports.Report.Fields.SystemName"] = "System name",
            ["Plugins.Misc.SqlReports.Report.Fields.Description"] = "Description",
            ["Plugins.Misc.SqlReports.Report.Fields.SqlQuery"] = "SQL query",
            ["Plugins.Misc.SqlReports.Report.Fields.SqlQuery.Required"] = "SQL query is required.",
            ["Plugins.Misc.SqlReports.Report.Fields.IsActive"] = "Active",
            ["Plugins.Misc.SqlReports.Report.Fields.DisplayOrder"] = "Display order",
            ["Plugins.Misc.SqlReports.Report.Fields.CustomerRoles"] = "Allowed customer roles",
            ["Plugins.Misc.SqlReports.Report.Fields.Parameters"] = "Parameters",
            ["Plugins.Misc.SqlReports.Parameter.AddNew"] = "Add a new parameter",
            ["Plugins.Misc.SqlReports.Parameter.Edit"] = "Edit parameter",
            ["Plugins.Misc.SqlReports.Parameter.Fields.Name"] = "Name",
            ["Plugins.Misc.SqlReports.Parameter.Fields.Name.Required"] = "Name is required.",
            ["Plugins.Misc.SqlReports.Parameter.Fields.ParameterName"] = "Parameter name",
            ["Plugins.Misc.SqlReports.Parameter.Fields.ParameterName.Invalid"] = "Enter a valid SQL parameter name, for example CustomerId or @CustomerId.",
            ["Plugins.Misc.SqlReports.Parameter.Fields.DataType"] = "Data type",
            ["Plugins.Misc.SqlReports.Parameter.Fields.DefaultValue"] = "Default value",
            ["Plugins.Misc.SqlReports.Parameter.Fields.Prompt"] = "Prompt",
            ["Plugins.Misc.SqlReports.Parameter.Fields.IsRequired"] = "Required",
            ["Plugins.Misc.SqlReports.Parameter.Fields.DisplayOrder"] = "Display order",
            ["Plugins.Misc.SqlReports.Run.ParameterValue"] = "Value",
            ["Plugins.Misc.SqlReports.InstantQuery.Fields.SqlQuery"] = "SQL query",
            ["Plugins.Misc.SqlReports.InstantQuery.Fields.ParameterValues"] = "Parameter values",
            ["Plugins.Misc.SqlReports.Result.RowsReturned"] = "Rows returned",
            ["Plugins.Misc.SqlReports.Result.Elapsed"] = "Elapsed",
            ["Plugins.Misc.SqlReports.Result.Truncated"] = "Preview row limit reached"
        });
    }

    public override void Down()
    {
    }
}
