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
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.SystemName)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.Name)).AsString(400).NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.HelpText)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.ControlType)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.IsRequired)).AsBoolean().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.ShowOnOnboarding)).AsBoolean().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.ShowOnProfileEdit)).AsBoolean().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.ShowOnPublicProfile)).AsBoolean().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.DisplayOrder)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeDefinition.IsActive)).AsBoolean().NotNullable()
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
            .WithColumn(nameof(JobSupportProfileAttributeOption.DisplayOrder)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeOption.IsActive)).AsBoolean().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeOption.LegacyOptionId)).AsInt32().Nullable()
            .WithColumn(nameof(JobSupportProfileAttributeOption.CreatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeOption.UpdatedOnUtc)).AsDateTime2().NotNullable();
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
            .WithColumn(nameof(JobSupportProfileAttributeValue.DisplayOrder)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeValue.CreatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportProfileAttributeValue.UpdatedOnUtc)).AsDateTime2().NotNullable();
    }
}
