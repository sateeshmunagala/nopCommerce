using System.Data;
using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.SqlReports.Domain;

namespace Nop.Plugin.Misc.SqlReports.Data.Migrations;

[NopMigration("2026/08/01 12:02:00", "Misc.SqlReports execution log report FK set null", MigrationProcessType.Update)]
public class ExecutionLogReportForeignKeyMigration : Migration
{
    private const string ForeignKeyName = "FK_SqlReportExecutionLog_SqlReport";

    public override void Up()
    {
        if (!Schema.Table(nameof(SqlReportExecutionLog)).Exists() ||
            !Schema.Table(nameof(SqlReport)).Exists() ||
            !Schema.Table(nameof(SqlReportExecutionLog)).Column(nameof(SqlReportExecutionLog.SqlReportId)).Exists())
            return;

        if (Schema.Table(nameof(SqlReportExecutionLog)).Constraint(ForeignKeyName).Exists())
            Delete.ForeignKey(ForeignKeyName).OnTable(nameof(SqlReportExecutionLog));

        Create.ForeignKey(ForeignKeyName)
            .FromTable(nameof(SqlReportExecutionLog)).ForeignColumn(nameof(SqlReportExecutionLog.SqlReportId))
            .ToTable(nameof(SqlReport)).PrimaryColumn(nameof(SqlReport.Id))
            .OnDelete(Rule.SetNull);
    }

    public override void Down()
    {
        if (!Schema.Table(nameof(SqlReportExecutionLog)).Exists() ||
            !Schema.Table(nameof(SqlReport)).Exists() ||
            !Schema.Table(nameof(SqlReportExecutionLog)).Column(nameof(SqlReportExecutionLog.SqlReportId)).Exists())
            return;

        if (Schema.Table(nameof(SqlReportExecutionLog)).Constraint(ForeignKeyName).Exists())
            Delete.ForeignKey(ForeignKeyName).OnTable(nameof(SqlReportExecutionLog));

        Create.ForeignKey(ForeignKeyName)
            .FromTable(nameof(SqlReportExecutionLog)).ForeignColumn(nameof(SqlReportExecutionLog.SqlReportId))
            .ToTable(nameof(SqlReport)).PrimaryColumn(nameof(SqlReport.Id));
    }
}
