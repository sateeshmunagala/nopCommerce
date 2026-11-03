using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data;

[NopMigration("2026/07/15 09:00:00", "Misc.AIInterview azure usage metrics", MigrationProcessType.Update)]
public class AzureUsageMetricMigration : Migration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<AzureUsageMetric>();
        CreateIndexes();
    }

    public override void Down()
    {
        DeleteIndexes();
        this.DeleteTableIfExists<AzureUsageMetric>();
    }

    protected virtual void CreateIndexes()
    {
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

    protected virtual void DeleteIndexes()
    {
        var indexes = new[]
        {
            "IX_AIInterview_AzureUsageMetric_ClientEventId",
            "IX_AIInterview_AzureUsageMetric_CreatedOnUtc",
            "IX_AIInterview_AzureUsageMetric_UsageKind",
            "IX_AIInterview_AzureUsageMetric_InterviewTurnId",
            "IX_AIInterview_AzureUsageMetric_InterviewSessionId"
        };

        foreach (var indexName in indexes)
        {
            if (Schema.Table(nameof(AzureUsageMetric)).Index(indexName).Exists())
                Delete.Index(indexName).OnTable(nameof(AzureUsageMetric));
        }
    }
}
