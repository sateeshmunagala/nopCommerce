using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.JobSupport.Domain;

namespace Nop.Plugin.Misc.JobSupport.Data.Mapping.Builders;

public class JobSupportSubscriptionBuilder : NopEntityBuilder<JobSupportSubscription>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(JobSupportSubscription.CustomerId)).AsInt32().NotNullable().ForeignKey<Customer>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportSubscription.OrderId)).AsInt32().NotNullable().ForeignKey<Order>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportSubscription.OrderItemId)).AsInt32().NotNullable().ForeignKey<OrderItem>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportSubscription.ProductId)).AsInt32().NotNullable().ForeignKey<Product>(onDelete: Rule.None)
            .WithColumn(nameof(JobSupportSubscription.Status)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportSubscription.StartOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportSubscription.EndOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportSubscription.AllottedCredits)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportSubscription.CarriedCredits)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportSubscription.UsedCredits)).AsInt32().NotNullable()
            .WithColumn(nameof(JobSupportSubscription.CreatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportSubscription.UpdatedOnUtc)).AsDateTime2().NotNullable()
            .WithColumn(nameof(JobSupportSubscription.LegacyRewardPointsHistoryId)).AsInt32().Nullable()
            .WithColumn(nameof(JobSupportSubscription.MigrationSource)).AsString(int.MaxValue).Nullable();
    }
}
