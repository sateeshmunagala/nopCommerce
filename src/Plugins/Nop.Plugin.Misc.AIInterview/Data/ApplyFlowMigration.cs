using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2024/05/21 12:00:00", "Misc.AIInterview apply flow updates", MigrationProcessType.Installation)]
public class ApplyFlowMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(JobApplication)).Column(nameof(JobApplication.ResumeDownloadId)).Exists())
            Alter.Table(nameof(JobApplication)).AddColumn(nameof(JobApplication.ResumeDownloadId)).AsInt32().NotNullable().WithDefaultValue(0);

        if (!Schema.Table(nameof(InterviewSession)).Column(nameof(InterviewSession.CustomerId)).Exists())
            Alter.Table(nameof(InterviewSession)).AddColumn(nameof(InterviewSession.CustomerId)).AsInt32().NotNullable().WithDefaultValue(0);

        if (!Schema.Table(nameof(InterviewSession)).Column(nameof(InterviewSession.Score)).Exists())
            Alter.Table(nameof(InterviewSession)).AddColumn(nameof(InterviewSession.Score)).AsDecimal(18, 4).NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        // No need to implement Down for simple column additions if we don't want to risk data loss on rollback
    }
}
