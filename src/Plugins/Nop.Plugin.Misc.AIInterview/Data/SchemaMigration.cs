using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2024/05/20 12:00:00", "Misc.AIInterview base schema", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<JobApplication>();
        this.CreateTableIfNotExists<InterviewSession>();
        this.CreateTableIfNotExists<CreditWallet>();
        this.CreateTableIfNotExists<CreditLedgerEntry>();
        this.CreateTableIfNotExists<SponsorInvite>();
        this.CreateTableIfNotExists<CreditPurchaseGrant>();

        Create.Index("IX_AIInterview_CreditPurchaseGrant_OrderItemId")
            .OnTable(nameof(CreditPurchaseGrant))
            .OnColumn(nameof(CreditPurchaseGrant.OrderItemId)).Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        this.DeleteTableIfExists<JobApplication>();
        this.DeleteTableIfExists<InterviewSession>();
        this.DeleteTableIfExists<CreditWallet>();
        this.DeleteTableIfExists<CreditLedgerEntry>();
        this.DeleteTableIfExists<SponsorInvite>();
        this.DeleteTableIfExists<CreditPurchaseGrant>();
    }
}
