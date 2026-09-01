using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Customers;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Mapping.Builders;

public class JobSupportProfileViewBuilder : NopEntityBuilder<JobSupportProfileView>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(JobSupportProfileView.ViewerCustomerId)).AsInt32().NotNullable().ForeignKey<Customer>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfileView.ViewedCustomerId)).AsInt32().NotNullable().ForeignKey<Customer>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfileView.ViewerProfileId)).AsInt32().NotNullable().ForeignKey<JobSupportProfile>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfileView.ViewedProfileId)).AsInt32().NotNullable().ForeignKey<JobSupportProfile>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportProfileView.FirstViewedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportProfileView.LastViewedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportProfileView.ViewCount)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportProfileView.LegacyShoppingCartItemId)).AsInt32().Nullable();
    }
}
