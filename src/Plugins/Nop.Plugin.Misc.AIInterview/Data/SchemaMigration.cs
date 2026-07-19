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
        this.CreateTableIfNotExists<InterviewTurn>();
        this.CreateTableIfNotExists<CreditWallet>();
        this.CreateTableIfNotExists<CreditLedgerEntry>();
        this.CreateTableIfNotExists<SponsorInvite>();
        this.CreateTableIfNotExists<CreditPurchaseGrant>();
        this.CreateTableIfNotExists<AzureUsageMetric>();

        if (!Schema.Table(nameof(CreditPurchaseGrant)).Index("IX_AIInterview_CreditPurchaseGrant_OrderItemId").Exists())
        {
            Create.Index("IX_AIInterview_CreditPurchaseGrant_OrderItemId")
                .OnTable(nameof(CreditPurchaseGrant))
                .OnColumn(nameof(CreditPurchaseGrant.OrderItemId)).Ascending()
                .WithOptions().Unique();
        }

        if (!Schema.Table(nameof(InterviewTurn)).Index("IX_AIInterview_InterviewTurn_SessionId_SequenceNumber").Exists())
        {
            Create.Index("IX_AIInterview_InterviewTurn_SessionId_SequenceNumber")
                .OnTable(nameof(InterviewTurn))
                .OnColumn(nameof(InterviewTurn.InterviewSessionId)).Ascending()
                .OnColumn(nameof(InterviewTurn.SequenceNumber)).Ascending();
        }

        if (!Schema.Table(nameof(AzureUsageMetric)).Index("IX_AIInterview_AzureUsageMetric_InterviewSessionId").Exists())
        {
            Create.Index("IX_AIInterview_AzureUsageMetric_InterviewSessionId")
                .OnTable(nameof(AzureUsageMetric))
                .OnColumn(nameof(AzureUsageMetric.InterviewSessionId)).Ascending();
        }

        if (!Schema.Table(nameof(AzureUsageMetric)).Index("IX_AIInterview_AzureUsageMetric_InterviewTurnId").Exists())
        {
            Create.Index("IX_AIInterview_AzureUsageMetric_InterviewTurnId")
                .OnTable(nameof(AzureUsageMetric))
                .OnColumn(nameof(AzureUsageMetric.InterviewTurnId)).Ascending();
        }

        if (!Schema.Table(nameof(AzureUsageMetric)).Index("IX_AIInterview_AzureUsageMetric_UsageKind").Exists())
        {
            Create.Index("IX_AIInterview_AzureUsageMetric_UsageKind")
                .OnTable(nameof(AzureUsageMetric))
                .OnColumn(nameof(AzureUsageMetric.UsageKind)).Ascending();
        }

        if (!Schema.Table(nameof(AzureUsageMetric)).Index("IX_AIInterview_AzureUsageMetric_CreatedOnUtc").Exists())
        {
            Create.Index("IX_AIInterview_AzureUsageMetric_CreatedOnUtc")
                .OnTable(nameof(AzureUsageMetric))
                .OnColumn(nameof(AzureUsageMetric.CreatedOnUtc)).Ascending();
        }

        if (!Schema.Table(nameof(AzureUsageMetric)).Index("IX_AIInterview_AzureUsageMetric_ClientEventId").Exists())
        {
            Create.Index("IX_AIInterview_AzureUsageMetric_ClientEventId")
                .OnTable(nameof(AzureUsageMetric))
                .OnColumn(nameof(AzureUsageMetric.ClientEventId)).Ascending();
        }
    }

    public override void Down()
    {
        this.DeleteTableIfExists<JobApplication>();
        this.DeleteTableIfExists<InterviewSession>();
        this.DeleteTableIfExists<InterviewTurn>();
        this.DeleteTableIfExists<CreditWallet>();
        this.DeleteTableIfExists<CreditLedgerEntry>();
        this.DeleteTableIfExists<SponsorInvite>();
        this.DeleteTableIfExists<CreditPurchaseGrant>();
        this.DeleteTableIfExists<AzureUsageMetric>();
    }
}
