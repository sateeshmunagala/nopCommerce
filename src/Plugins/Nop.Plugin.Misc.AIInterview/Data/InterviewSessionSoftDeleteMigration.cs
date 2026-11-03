using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/08/19 10:00:00", "Misc.AIInterview applicant interview soft delete", MigrationProcessType.Update)]
public class InterviewSessionSoftDeleteMigration : Migration
{
    private const string TableName = nameof(InterviewSession);

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(nameof(InterviewSession.Deleted)).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(nameof(InterviewSession.Deleted))
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(false);
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(nameof(InterviewSession.Deleted)).Exists())
            Delete.Column(nameof(InterviewSession.Deleted)).FromTable(TableName);
    }
}
