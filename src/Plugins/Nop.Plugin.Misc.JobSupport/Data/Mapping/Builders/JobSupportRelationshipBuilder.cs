using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Customers;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Mapping.Builders;

public class JobSupportRelationshipBuilder : NopEntityBuilder<JobSupportRelationship>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(JobSupportRelationship.SourceCustomerId)).AsInt32().NotNullable().ForeignKey<Customer>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportRelationship.TargetCustomerId)).AsInt32().NotNullable().ForeignKey<Customer>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportRelationship.SourceProfileId)).AsInt32().NotNullable().ForeignKey<JobSupportProfile>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportRelationship.TargetProfileId)).AsInt32().NotNullable().ForeignKey<JobSupportProfile>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportRelationship.RelationshipTypeId)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportRelationship.StatusId)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportRelationship.CreatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportRelationship.UpdatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportRelationship.RespondedOnUtc)).AsDateTime2().Nullable()
            .WithColumn(nameof(JobSupportRelationship.LegacyShoppingCartItemId)).AsInt32().Nullable()
            .WithColumn(nameof(JobSupportRelationship.MetadataJson)).AsString(int.MaxValue).Nullable();
    }
}
