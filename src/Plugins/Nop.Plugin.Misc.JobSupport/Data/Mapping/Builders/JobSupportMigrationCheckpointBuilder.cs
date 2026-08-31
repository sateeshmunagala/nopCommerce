using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Mapping.Builders;

public class JobSupportMigrationCheckpointBuilder : NopEntityBuilder<JobSupportMigrationCheckpoint>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(JobSupportMigrationCheckpoint.MigrationName)).AsString(200).NotNullable()
            .WithColumn(nameof(JobSupportMigrationCheckpoint.LastProcessedId)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportMigrationCheckpoint.ProcessedCount)).AsInt64().NotNullable()
            .WithColumn(nameof(JobSupportMigrationCheckpoint.SkippedCount)).AsInt64().NotNullable()
            .WithColumn(nameof(JobSupportMigrationCheckpoint.FailedCount)).AsInt64().NotNullable()
            .WithColumn(nameof(JobSupportMigrationCheckpoint.MismatchCount)).AsInt64().NotNullable()
            .WithColumn(nameof(JobSupportMigrationCheckpoint.Status)).AsString(50).NotNullable()
            .WithColumn(nameof(JobSupportMigrationCheckpoint.ErrorLog)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(JobSupportMigrationCheckpoint.LastExecutedOnUtc)).AsDateTime2().Nullable()
            .WithColumn(nameof(JobSupportMigrationCheckpoint.UpdatedOnUtc)).AsDateTime2().NotNullable();
    }
}
