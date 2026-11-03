using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/08/02 10:00:00", "Interview session report share fields", MigrationProcessType.Update)]
public class InterviewSessionReportShareMigration : Migration
{
    private const string TableName = nameof(InterviewSession);

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(nameof(InterviewSession.ReportShareToken)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(InterviewSession.ReportShareToken))
                .AsString(256)
                .Nullable();
        }

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.ReportShareEnabled)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(InterviewSession.ReportShareEnabled))
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.ReportShareCreatedOnUtc)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(InterviewSession.ReportShareCreatedOnUtc))
                .AsDateTime2()
                .Nullable();
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(nameof(InterviewSession.ReportShareCreatedOnUtc)).Exists())
            Delete.Column(nameof(InterviewSession.ReportShareCreatedOnUtc)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.ReportShareEnabled)).Exists())
            Delete.Column(nameof(InterviewSession.ReportShareEnabled)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.ReportShareToken)).Exists())
            Delete.Column(nameof(InterviewSession.ReportShareToken)).FromTable(TableName);
    }
}
