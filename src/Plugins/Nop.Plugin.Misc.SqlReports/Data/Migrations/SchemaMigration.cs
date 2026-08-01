using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.SqlReports.Domain;

namespace Nop.Plugin.Misc.SqlReports.Data.Migrations;

[NopMigration("2026/08/01 12:00:00", "Misc.SqlReports schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<SqlReport>();
        this.CreateTableIfNotExists<SqlReportParameter>();
        this.CreateTableIfNotExists<SqlReportParameterMapping>();
        this.CreateTableIfNotExists<SqlReportCustomerRoleMapping>();
        this.CreateTableIfNotExists<SqlReportParameterOption>();
        this.CreateTableIfNotExists<SqlReportExecutionLog>();

        Create.Index("IX_SqlReport_SystemName").OnTable(nameof(SqlReport)).OnColumn(nameof(SqlReport.SystemName));
        Create.Index("IX_SqlReportParameter_ParameterName").OnTable(nameof(SqlReportParameter)).OnColumn(nameof(SqlReportParameter.ParameterName));
        Create.Index("IX_SqlReportParameterMapping_Report").OnTable(nameof(SqlReportParameterMapping)).OnColumn(nameof(SqlReportParameterMapping.SqlReportId));
        Create.Index("IX_SqlReportRoleMapping_Report").OnTable(nameof(SqlReportCustomerRoleMapping)).OnColumn(nameof(SqlReportCustomerRoleMapping.SqlReportId));
        Create.Index("IX_SqlReportRoleMapping_Role").OnTable(nameof(SqlReportCustomerRoleMapping)).OnColumn(nameof(SqlReportCustomerRoleMapping.CustomerRoleId));
        Create.Index("IX_SqlReportParameterOption_Parameter").OnTable(nameof(SqlReportParameterOption)).OnColumn(nameof(SqlReportParameterOption.SqlReportParameterId));
        Create.Index("IX_SqlReportExecutionLog_Report").OnTable(nameof(SqlReportExecutionLog)).OnColumn(nameof(SqlReportExecutionLog.SqlReportId));
        Create.Index("IX_SqlReportExecutionLog_Customer").OnTable(nameof(SqlReportExecutionLog)).OnColumn(nameof(SqlReportExecutionLog.CustomerId));
        Create.Index("IX_SqlReportExecutionLog_CreatedOnUtc").OnTable(nameof(SqlReportExecutionLog)).OnColumn(nameof(SqlReportExecutionLog.CreatedOnUtc));
    }

    public override void Down()
    {
        this.DeleteTableIfExists<SqlReportExecutionLog>();
        this.DeleteTableIfExists<SqlReportParameterOption>();
        this.DeleteTableIfExists<SqlReportCustomerRoleMapping>();
        this.DeleteTableIfExists<SqlReportParameterMapping>();
        this.DeleteTableIfExists<SqlReportParameter>();
        this.DeleteTableIfExists<SqlReport>();
    }
}
