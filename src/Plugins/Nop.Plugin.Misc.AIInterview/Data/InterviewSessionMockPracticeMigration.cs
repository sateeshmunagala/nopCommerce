using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/07/08 10:00:00", "Misc.AIInterview mock practice session fields", MigrationProcessType.Update)]
public class InterviewSessionMockPracticeMigration : Migration
{
    private const string TableName = nameof(InterviewSession);

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(nameof(InterviewSession.InterviewType)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(InterviewSession.InterviewType))
                .AsString(100)
                .Nullable()
                .WithDefaultValue(AIInterviewDefaults.InterviewTypeJob);
        }

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.SourceProductId)).Exists())
            Alter.Table(TableName).AddColumn(nameof(InterviewSession.SourceProductId)).AsInt32().NotNullable().WithDefaultValue(0);

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.ResumeDownloadId)).Exists())
            Alter.Table(TableName).AddColumn(nameof(InterviewSession.ResumeDownloadId)).AsInt32().NotNullable().WithDefaultValue(0);

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.ResumeProfileJson)).Exists())
            Alter.Table(TableName).AddColumn(nameof(InterviewSession.ResumeProfileJson)).AsString(int.MaxValue).Nullable();

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.ResumeProfileGeneratedOnUtc)).Exists())
            Alter.Table(TableName).AddColumn(nameof(InterviewSession.ResumeProfileGeneratedOnUtc)).AsDateTime2().Nullable();

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.ResumeProfileError)).Exists())
            Alter.Table(TableName).AddColumn(nameof(InterviewSession.ResumeProfileError)).AsString(int.MaxValue).Nullable();

        if (!Schema.Table(TableName).Column(nameof(InterviewSession.SelectedProductAttributesJson)).Exists())
            Alter.Table(TableName).AddColumn(nameof(InterviewSession.SelectedProductAttributesJson)).AsString(int.MaxValue).Nullable();
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(nameof(InterviewSession.SelectedProductAttributesJson)).Exists())
            Delete.Column(nameof(InterviewSession.SelectedProductAttributesJson)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.ResumeProfileError)).Exists())
            Delete.Column(nameof(InterviewSession.ResumeProfileError)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.ResumeProfileGeneratedOnUtc)).Exists())
            Delete.Column(nameof(InterviewSession.ResumeProfileGeneratedOnUtc)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.ResumeProfileJson)).Exists())
            Delete.Column(nameof(InterviewSession.ResumeProfileJson)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.ResumeDownloadId)).Exists())
            Delete.Column(nameof(InterviewSession.ResumeDownloadId)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.SourceProductId)).Exists())
            Delete.Column(nameof(InterviewSession.SourceProductId)).FromTable(TableName);

        if (Schema.Table(TableName).Column(nameof(InterviewSession.InterviewType)).Exists())
            Delete.Column(nameof(InterviewSession.InterviewType)).FromTable(TableName);
    }
}
