using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Customers;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Mapping.Builders;

public class JobSupportContactRevealBuilder : NopEntityBuilder<JobSupportContactReveal>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(JobSupportContactReveal.SubscriptionId)).AsInt32().Nullable().ForeignKey<JobSupportSubscription>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportContactReveal.ViewerCustomerId)).AsInt32().NotNullable().ForeignKey<Customer>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportContactReveal.TargetCustomerId)).AsInt32().NotNullable().ForeignKey<Customer>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportContactReveal.TargetProfileId)).AsInt32().NotNullable().ForeignKey<JobSupportProfile>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportContactReveal.CreditCost)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportContactReveal.RevealedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportContactReveal.LegacyGenericAttributeId)).AsInt32().Nullable();
    }
}
