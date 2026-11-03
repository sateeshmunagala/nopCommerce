using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/06/14 12:00:00", "Misc.AIInterview interview session question count", MigrationProcessType.Update)]
public class InterviewSessionQuestionCountMigration : Migration
{
    private const string TableName = nameof(InterviewSession);
    private const string ColumnName = nameof(InterviewSession.QuestionCount);

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(ColumnName).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(ColumnName)
                .AsInt32()
                .WithDefaultValue(0);
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(ColumnName).Exists())
            Delete.Column(ColumnName).FromTable(TableName);
    }
}
