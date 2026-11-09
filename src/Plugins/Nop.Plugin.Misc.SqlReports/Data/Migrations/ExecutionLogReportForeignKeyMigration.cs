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

        DropExecutionLogReportForeignKeys();

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

        DropExecutionLogReportForeignKeys();

        Create.ForeignKey(ForeignKeyName)
            .FromTable(nameof(SqlReportExecutionLog)).ForeignColumn(nameof(SqlReportExecutionLog.SqlReportId))
            .ToTable(nameof(SqlReport)).PrimaryColumn(nameof(SqlReport.Id));
    }

    protected virtual void DropExecutionLogReportForeignKeys()
    {
        Execute.Sql($@"
DECLARE @sql nvarchar(max) = N'';

SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(parent.schema_id)) + N'.' + QUOTENAME(parent.name) +
    N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
INNER JOIN sys.tables parent ON fk.parent_object_id = parent.object_id
INNER JOIN sys.columns parent_column ON fkc.parent_object_id = parent_column.object_id AND fkc.parent_column_id = parent_column.column_id
INNER JOIN sys.tables referenced ON fk.referenced_object_id = referenced.object_id
INNER JOIN sys.columns referenced_column ON fkc.referenced_object_id = referenced_column.object_id AND fkc.referenced_column_id = referenced_column.column_id
WHERE parent.name = N'{nameof(SqlReportExecutionLog)}'
    AND parent_column.name = N'{nameof(SqlReportExecutionLog.SqlReportId)}'
    AND referenced.name = N'{nameof(SqlReport)}'
    AND referenced_column.name = N'{nameof(SqlReport.Id)}';

IF LEN(@sql) > 0
    EXEC sp_executesql @sql;");
    }
}
