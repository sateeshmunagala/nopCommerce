using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Data.Builders;

public class AzureUsageMetricBuilder : NopEntityBuilder<AzureUsageMetric>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(AzureUsageMetric.UsageKind)).AsString(200).Nullable()
            .WithColumn(nameof(AzureUsageMetric.Provider)).AsString(100).Nullable()
            .WithColumn(nameof(AzureUsageMetric.DeploymentOrModel)).AsString(400).Nullable()
            .WithColumn(nameof(AzureUsageMetric.ModelName)).AsString(400).Nullable()
            .WithColumn(nameof(AzureUsageMetric.OperationName)).AsString(200).Nullable()
            .WithColumn(nameof(AzureUsageMetric.CurrencyCode)).AsString(10).Nullable()
            .WithColumn(nameof(AzureUsageMetric.PricingSnapshotJson)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(AzureUsageMetric.RawUsageJson)).AsString(4000).Nullable()
            .WithColumn(nameof(AzureUsageMetric.MetadataJson)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(AzureUsageMetric.ClientEventId)).AsString(200).Nullable();
    }
}
