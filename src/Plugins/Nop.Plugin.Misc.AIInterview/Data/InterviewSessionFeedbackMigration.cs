using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/07/23 09:00:00", "Misc.AIInterview interview session runtime feedback", MigrationProcessType.Update)]
public class InterviewSessionFeedbackMigration : Migration
{
    private const string TableName = nameof(InterviewSession);

    public override void Up()
    {
        AddStringColumn(nameof(InterviewSession.CandidateFeedbackIssue), 200);
        AddStringColumn(nameof(InterviewSession.CandidateFeedbackHelpfulness), 50);
        AddTextColumn(nameof(InterviewSession.CandidateFeedbackComment));
        AddIntColumn(nameof(InterviewSession.CandidateFeedbackAttachmentDownloadId));
        AddDateTimeColumn(nameof(InterviewSession.CandidateFeedbackSubmittedOnUtc));
    }

    public override void Down()
    {
        DeleteColumnIfExists(nameof(InterviewSession.CandidateFeedbackSubmittedOnUtc));
        DeleteColumnIfExists(nameof(InterviewSession.CandidateFeedbackAttachmentDownloadId));
        DeleteColumnIfExists(nameof(InterviewSession.CandidateFeedbackComment));
        DeleteColumnIfExists(nameof(InterviewSession.CandidateFeedbackHelpfulness));
        DeleteColumnIfExists(nameof(InterviewSession.CandidateFeedbackIssue));
    }

    protected virtual void AddStringColumn(string columnName, int length)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
            Alter.Table(TableName).AddColumn(columnName).AsString(length).Nullable();
    }

    protected virtual void AddTextColumn(string columnName)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
            Alter.Table(TableName).AddColumn(columnName).AsString(int.MaxValue).Nullable();
    }

    protected virtual void AddIntColumn(string columnName)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
            Alter.Table(TableName).AddColumn(columnName).AsInt32().NotNullable().WithDefaultValue(0);
    }

    protected virtual void AddDateTimeColumn(string columnName)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
            Alter.Table(TableName).AddColumn(columnName).AsDateTime2().Nullable();
    }

    protected virtual void DeleteColumnIfExists(string columnName)
    {
        if (Schema.Table(TableName).Column(columnName).Exists())
            Delete.Column(columnName).FromTable(TableName);
    }
}
