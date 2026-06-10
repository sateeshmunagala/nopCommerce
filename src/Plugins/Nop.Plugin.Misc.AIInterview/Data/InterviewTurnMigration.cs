using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/06/10 09:00:00", "Misc.AIInterview interview turn table", MigrationProcessType.Update)]
public class InterviewTurnMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<InterviewTurn>();

        Create.Index("IX_AIInterview_InterviewTurn_SessionId_SequenceNumber")
            .OnTable(nameof(InterviewTurn))
            .OnColumn(nameof(InterviewTurn.InterviewSessionId)).Ascending()
            .OnColumn(nameof(InterviewTurn.SequenceNumber)).Ascending();
    }

    public override void Down()
    {
        Delete.Index("IX_AIInterview_InterviewTurn_SessionId_SequenceNumber")
            .OnTable(nameof(InterviewTurn));
        this.DeleteTableIfExists<InterviewTurn>();
    }
}
