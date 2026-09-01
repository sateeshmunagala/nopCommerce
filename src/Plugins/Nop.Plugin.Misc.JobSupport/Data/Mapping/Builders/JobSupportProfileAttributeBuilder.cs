using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Mapping.Builders;

public class JobSupportProfileAttributeDefinitionBuilder : NopEntityBuilder<JobSupportProfileAttributeDefinition>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.LegacyCustomerAttributeId)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.Name)).AsString(400).NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.IsRequired)).AsBoolean().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.DisplayOrder)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.CreatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.UpdatedOnUtc)).AsDateTime2().NotNullable();
    }
}

public class JobSupportProfileAttributeOptionBuilder : NopEntityBuilder<JobSupportProfileAttributeOption>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(JobSupportProfileAttributeOption.AttributeDefinitionId)).AsInt32().NotNullable().ForeignKey<JobSupportProfileAttributeDefinition>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfileAttributeOption.LegacyCustomerAttributeValueId)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeOption.Name)).AsString(400).NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeOption.DisplayOrder)).AsInt32().NotNullable();
    }
}

public class JobSupportProfileAttributeValueBuilder : NopEntityBuilder<JobSupportProfileAttributeValue>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(JobSupportProfileAttributeValue.ProfileId)).AsInt32().NotNullable().ForeignKey<JobSupportProfile>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfileAttributeValue.AttributeDefinitionId)).AsInt32().NotNullable().ForeignKey<JobSupportProfileAttributeDefinition>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfileAttributeValue.AttributeOptionId)).AsInt32().Nullable().ForeignKey<JobSupportProfileAttributeOption>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfileAttributeValue.Value)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(JobSupportProfileAttributeValue.LegacyCustomerAttributeId)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeValue.LegacyCustomerAttributeValueId)).AsInt32().Nullable()
            .WithColumn(nameof(JobSupportProfileAttributeValue.CreatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeValue.UpdatedOnUtc)).AsDateTime2().NotNullable();
    }
}
