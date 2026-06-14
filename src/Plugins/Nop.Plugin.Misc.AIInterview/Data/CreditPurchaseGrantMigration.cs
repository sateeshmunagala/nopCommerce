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

        if (!Schema.Table(nameof(CreditPurchaseGrant)).Index("IX_AIInterview_CreditPurchaseGrant_OrderItemId").Exists())
        {
            Create.Index("IX_AIInterview_CreditPurchaseGrant_OrderItemId")
                .OnTable(nameof(CreditPurchaseGrant))
                .OnColumn(nameof(CreditPurchaseGrant.OrderItemId)).Ascending()
                .WithOptions().Unique();
        }
    }

    public override void Down()
    {
        if (Schema.Table(nameof(CreditPurchaseGrant)).Index("IX_AIInterview_CreditPurchaseGrant_OrderItemId").Exists())
        {
            Delete.Index("IX_AIInterview_CreditPurchaseGrant_OrderItemId")
                .OnTable(nameof(CreditPurchaseGrant))
                .OnColumn(nameof(CreditPurchaseGrant.OrderItemId));
        }
    }
}
