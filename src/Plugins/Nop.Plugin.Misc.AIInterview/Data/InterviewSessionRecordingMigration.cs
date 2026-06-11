using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/06/11 01:00:00", "Misc.AIInterview interview session recording url", MigrationProcessType.Update)]
public class InterviewSessionRecordingMigration : Migration
{
    private const string TableName = nameof(InterviewSession);
    private const string ColumnName = nameof(InterviewSession.RecordingUrl);

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(ColumnName).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(ColumnName)
                .AsString(int.MaxValue)
                .Nullable();
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(ColumnName).Exists())
            Delete.Column(ColumnName).FromTable(TableName);
    }
}
