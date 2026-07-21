using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/07/21 01:00:00", "Misc.AIInterview credit ledger display metadata", MigrationProcessType.Update)]
public class CreditLedgerMetadataMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(CreditLedgerEntry)).Column(nameof(CreditLedgerEntry.LedgerSource)).Exists())
            Alter.Table(nameof(CreditLedgerEntry)).AddColumn(nameof(CreditLedgerEntry.LedgerSource)).AsString(100).Nullable();

        if (!Schema.Table(nameof(CreditLedgerEntry)).Column(nameof(CreditLedgerEntry.ProductId)).Exists())
            Alter.Table(nameof(CreditLedgerEntry)).AddColumn(nameof(CreditLedgerEntry.ProductId)).AsInt32().NotNullable().WithDefaultValue(0);

        if (!Schema.Table(nameof(CreditLedgerEntry)).Column(nameof(CreditLedgerEntry.OrderId)).Exists())
            Alter.Table(nameof(CreditLedgerEntry)).AddColumn(nameof(CreditLedgerEntry.OrderId)).AsInt32().NotNullable().WithDefaultValue(0);

        if (!Schema.Table(nameof(CreditLedgerEntry)).Column(nameof(CreditLedgerEntry.SponsorInviteId)).Exists())
            Alter.Table(nameof(CreditLedgerEntry)).AddColumn(nameof(CreditLedgerEntry.SponsorInviteId)).AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
    }
}
