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

        CreateIndexIfNotExists("IX_SqlReport_SystemName", nameof(SqlReport), nameof(SqlReport.SystemName));
        CreateIndexIfNotExists("IX_SqlReportParameter_ParameterName", nameof(SqlReportParameter), nameof(SqlReportParameter.ParameterName));
        CreateIndexIfNotExists("IX_SqlReportParameterMapping_Report", nameof(SqlReportParameterMapping), nameof(SqlReportParameterMapping.SqlReportId));
        CreateIndexIfNotExists("IX_SqlReportRoleMapping_Report", nameof(SqlReportCustomerRoleMapping), nameof(SqlReportCustomerRoleMapping.SqlReportId));
        CreateIndexIfNotExists("IX_SqlReportRoleMapping_Role", nameof(SqlReportCustomerRoleMapping), nameof(SqlReportCustomerRoleMapping.CustomerRoleId));
        CreateIndexIfNotExists("IX_SqlReportParameterOption_Parameter", nameof(SqlReportParameterOption), nameof(SqlReportParameterOption.SqlReportParameterId));
        CreateIndexIfNotExists("IX_SqlReportExecutionLog_Report", nameof(SqlReportExecutionLog), nameof(SqlReportExecutionLog.SqlReportId));
        CreateIndexIfNotExists("IX_SqlReportExecutionLog_Customer", nameof(SqlReportExecutionLog), nameof(SqlReportExecutionLog.CustomerId));
        CreateIndexIfNotExists("IX_SqlReportExecutionLog_CreatedOnUtc", nameof(SqlReportExecutionLog), nameof(SqlReportExecutionLog.CreatedOnUtc));
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

    protected virtual void CreateIndexIfNotExists(string indexName, string tableName, string columnName)
    {
        if (Schema.Table(tableName).Index(indexName).Exists())
            return;

        Create.Index(indexName).OnTable(tableName).OnColumn(columnName);
    }
}
