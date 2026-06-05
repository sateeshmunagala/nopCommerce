using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2024/05/23 10:00:00", "Misc.AIInterview session product link updates", MigrationProcessType.Installation)]
public class SessionProductLinkMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(InterviewSession)).Column(nameof(InterviewSession.ProductId)).Exists())
            Alter.Table(nameof(InterviewSession)).AddColumn(nameof(InterviewSession.ProductId)).AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
