using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/06/13 12:00:00", "Misc.AIInterview interview session token expiry", MigrationProcessType.Update)]
public class InterviewSessionTokenExpiryMigration : Migration
{
    private const string TableName = nameof(InterviewSession);
    private const string ColumnName = nameof(InterviewSession.TokenExpiryUtc);

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(ColumnName).Exists())
            Alter.Table(TableName).AddColumn(ColumnName).AsDateTime2().Nullable();
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(ColumnName).Exists())
            Delete.Column(ColumnName).FromTable(TableName);
    }
}
