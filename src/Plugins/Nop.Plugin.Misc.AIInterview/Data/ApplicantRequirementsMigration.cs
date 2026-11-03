using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2024/05/22 10:00:00", "Misc.AIInterview applicant requirements updates", MigrationProcessType.Installation)]
public class ApplicantRequirementsMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(InterviewSession)).Column(nameof(InterviewSession.QuestionScores)).Exists())
            Alter.Table(nameof(InterviewSession)).AddColumn(nameof(InterviewSession.QuestionScores)).AsString(int.MaxValue).Nullable();

        if (!Schema.Table(nameof(InterviewSession)).Column(nameof(InterviewSession.SponsorInviteId)).Exists())
            Alter.Table(nameof(InterviewSession)).AddColumn(nameof(InterviewSession.SponsorInviteId)).AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
