using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/06/25 12:00:00", "Misc.AIInterview interview session recording share fields", MigrationProcessType.Update)]
public class InterviewSessionRecordingShareMigration : Migration
{
    private const string TableName = nameof(InterviewSession);

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(nameof(InterviewSession.RecordingShareToken)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(InterviewSession.RecordingShareToken))
                .AsString(256)
                .Nullable();
        }

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.RecordingShareEnabled)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(InterviewSession.RecordingShareEnabled))
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.RecordingShareCreatedOnUtc)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(InterviewSession.RecordingShareCreatedOnUtc))
                .AsDateTime2()
                .Nullable();
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(nameof(InterviewSession.RecordingShareCreatedOnUtc)).Exists())
            Delete.Column(nameof(InterviewSession.RecordingShareCreatedOnUtc)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.RecordingShareEnabled)).Exists())
            Delete.Column(nameof(InterviewSession.RecordingShareEnabled)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.RecordingShareToken)).Exists())
            Delete.Column(nameof(InterviewSession.RecordingShareToken)).FromTable(TableName);
    }
}
