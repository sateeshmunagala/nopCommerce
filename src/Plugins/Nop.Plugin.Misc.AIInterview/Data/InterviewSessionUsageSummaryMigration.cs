using FluentMigrator;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/07/15 09:05:00", "Misc.AIInterview interview session azure usage summary", MigrationProcessType.Update)]
public class InterviewSessionUsageSummaryMigration : Migration
{
    private const string TableName = nameof(InterviewSession);

    public override void Up()
    {
        AddIntColumn(nameof(InterviewSession.TotalPromptTokens));
        AddIntColumn(nameof(InterviewSession.TotalCompletionTokens));
        AddDecimalColumn(nameof(InterviewSession.TotalOpenAiCostUsd));
        AddIntColumn(nameof(InterviewSession.TotalSpeechRecognitionCharacters));
        AddIntColumn(nameof(InterviewSession.TotalSpeechSynthesisCharacters));
        AddLongColumn(nameof(InterviewSession.TotalSpeechDurationMs));
        AddDecimalColumn(nameof(InterviewSession.TotalSpeechCostUsd));
        AddDecimalColumn(nameof(InterviewSession.TotalAzureCostUsd));
    }

    public override void Down()
    {
        DeleteColumnIfExists(nameof(InterviewSession.TotalAzureCostUsd));
        DeleteColumnIfExists(nameof(InterviewSession.TotalSpeechCostUsd));
        DeleteColumnIfExists(nameof(InterviewSession.TotalSpeechDurationMs));
        DeleteColumnIfExists(nameof(InterviewSession.TotalSpeechSynthesisCharacters));
        DeleteColumnIfExists(nameof(InterviewSession.TotalSpeechRecognitionCharacters));
        DeleteColumnIfExists(nameof(InterviewSession.TotalOpenAiCostUsd));
        DeleteColumnIfExists(nameof(InterviewSession.TotalCompletionTokens));
        DeleteColumnIfExists(nameof(InterviewSession.TotalPromptTokens));
    }

    protected virtual void AddIntColumn(string columnName)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(columnName)
                .AsInt32()
                .WithDefaultValue(0);
        }
    }

    protected virtual void AddLongColumn(string columnName)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(columnName)
                .AsInt64()
                .WithDefaultValue(0L);
        }
    }

    protected virtual void AddDecimalColumn(string columnName)
    {
        if (!Schema.Table(TableName).Column(columnName).Exists())
        {
            Alter.Table(TableName)
                .AddColumn(columnName)
                .AsDecimal(18, 4)
                .WithDefaultValue(0m);
        }
    }

    protected virtual void DeleteColumnIfExists(string columnName)
    {
        if (Schema.Table(TableName).Column(columnName).Exists())
            Delete.Column(columnName).FromTable(TableName);
    }
}
