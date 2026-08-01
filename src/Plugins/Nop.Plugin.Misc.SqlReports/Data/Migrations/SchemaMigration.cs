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
    }

    public override void Down()
    {
        this.DeleteTableIfExists<SqlReportCustomerRoleMapping>();
        this.DeleteTableIfExists<SqlReportParameterMapping>();
        this.DeleteTableIfExists<SqlReportParameter>();
        this.DeleteTableIfExists<SqlReport>();
    }
}
