using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/07/29 11:00:00", "Misc.AIInterview previous resume source lookup index", MigrationProcessType.Update)]
public class InterviewSessionResumeSourceIndexMigration : Migration
{
    private const string TableName = nameof(InterviewSession);
    private const string IndexName = "IX_AIInterview_InterviewSession_Customer_Resume_Created_Id";

    public override void Up()
    {
        if (Schema.Table(TableName).Index(IndexName).Exists())
            return;

        Create.Index(IndexName)
            .OnTable(TableName)
            .OnColumn(nameof(InterviewSession.CustomerId)).Ascending()
            .OnColumn(nameof(InterviewSession.ResumeDownloadId)).Ascending()
            .OnColumn(nameof(InterviewSession.CreatedOnUtc)).Descending()
            .OnColumn(nameof(InterviewSession.Id)).Descending();
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Index(IndexName).Exists())
            Delete.Index(IndexName).OnTable(TableName);
    }
}
