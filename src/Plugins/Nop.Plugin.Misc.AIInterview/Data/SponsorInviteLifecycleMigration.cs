using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/06/09 00:00:00", "Misc.AIInterview sponsor invite lifecycle updates", MigrationProcessType.Update)]
public class SponsorInviteLifecycleMigration : Migration
{
    private const string TableName = nameof(SponsorInvite);
    private const string ColumnName = nameof(SponsorInvite.IsActive);

    public override void Up()
    {
        if (!Schema.Table(TableName).Column(ColumnName).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(ColumnName)
                .AsBoolean()
                .NotNullable()
                .WithDefaultValue(true);
        }
    }

    public override void Down()
    {
        if (Schema.Table(TableName).Column(ColumnName).Exists())
            Delete.Column(ColumnName).FromTable(TableName);
    }
}
