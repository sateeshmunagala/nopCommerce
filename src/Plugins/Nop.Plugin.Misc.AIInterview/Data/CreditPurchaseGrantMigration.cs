using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/06/09 01:00:00", "Misc.AIInterview credit purchase grant table", MigrationProcessType.Update)]
public class CreditPurchaseGrantMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<CreditPurchaseGrant>();
    }

    public override void Down()
    {
        this.DeleteTableIfExists<CreditPurchaseGrant>();
    }
}
