using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/08/10 09:00:00", "Misc.AIInterview runtime start credit markers", MigrationProcessType.Update)]
public class InterviewStartCreditMigration : Migration
{
    private const string SessionTable = nameof(InterviewSession);
    private const string LedgerTable = nameof(CreditLedgerEntry);
    private const string LedgerIndex = "IX_AIInterview_CreditLedger_InterviewSession_Source_Type";

    public override void Up()
    {
        AddDateTimeColumn(SessionTable, nameof(InterviewSession.CreditChargedOnUtc));
        AddIntColumn(SessionTable, nameof(InterviewSession.CreditChargeCustomerId));
        AddDecimalColumn(SessionTable, nameof(InterviewSession.CreditChargeAmount));
        AddStringColumn(SessionTable, nameof(InterviewSession.CreditChargeLedgerSource), 100);
        AddDateTimeColumn(SessionTable, nameof(InterviewSession.CreditRefundedOnUtc));
        AddStringColumn(SessionTable, nameof(InterviewSession.CreditRefundReasonCode), 100);

        if (!Schema.Table(LedgerTable).Column(nameof(CreditLedgerEntry.InterviewSessionId)).Exists())
            Alter.Table(LedgerTable).AddColumn(nameof(CreditLedgerEntry.InterviewSessionId)).AsInt32().Nullable();

        if (!Schema.Table(LedgerTable).Index(LedgerIndex).Exists())
        {
            Create.Index(LedgerIndex)
                .OnTable(LedgerTable)
                .OnColumn(nameof(CreditLedgerEntry.InterviewSessionId)).Ascending()
                .OnColumn(nameof(CreditLedgerEntry.LedgerSource)).Ascending()
                .OnColumn(nameof(CreditLedgerEntry.TransactionType)).Ascending();
        }
    }

    public override void Down()
    {
        if (Schema.Table(LedgerTable).Index(LedgerIndex).Exists())
            Delete.Index(LedgerIndex).OnTable(LedgerTable);
        DeleteColumnIfExists(LedgerTable, nameof(CreditLedgerEntry.InterviewSessionId));
        DeleteColumnIfExists(SessionTable, nameof(InterviewSession.CreditRefundReasonCode));
        DeleteColumnIfExists(SessionTable, nameof(InterviewSession.CreditRefundedOnUtc));
        DeleteColumnIfExists(SessionTable, nameof(InterviewSession.CreditChargeLedgerSource));
        DeleteColumnIfExists(SessionTable, nameof(InterviewSession.CreditChargeAmount));
        DeleteColumnIfExists(SessionTable, nameof(InterviewSession.CreditChargeCustomerId));
        DeleteColumnIfExists(SessionTable, nameof(InterviewSession.CreditChargedOnUtc));
    }

    private void AddDateTimeColumn(string table, string column)
    {
        if (!Schema.Table(table).Column(column).Exists())
            Alter.Table(table).AddColumn(column).AsDateTime2().Nullable();
    }

    private void AddIntColumn(string table, string column)
    {
        if (!Schema.Table(table).Column(column).Exists())
            Alter.Table(table).AddColumn(column).AsInt32().NotNullable().WithDefaultValue(0);
    }

    private void AddDecimalColumn(string table, string column)
    {
        if (!Schema.Table(table).Column(column).Exists())
            Alter.Table(table).AddColumn(column).AsDecimal(18, 4).NotNullable().WithDefaultValue(0);
    }

    private void AddStringColumn(string table, string column, int length)
    {
        if (!Schema.Table(table).Column(column).Exists())
            Alter.Table(table).AddColumn(column).AsString(length).Nullable();
    }

    private void DeleteColumnIfExists(string table, string column)
    {
        if (Schema.Table(table).Column(column).Exists())
            Delete.Column(column).FromTable(table);
    }
}
