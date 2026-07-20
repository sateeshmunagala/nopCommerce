using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Vendors;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Data.Mapping.Builders;

public class AvailabilityRuleBuilder : NopEntityBuilder<AvailabilityRule>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(AvailabilityRule.ServiceId)).AsInt32().ForeignKey<BookableService>();
        table.WithColumn(nameof(AvailabilityRule.VendorId)).AsInt32().ForeignKey<Vendor>(onDelete: Rule.None);
    }
}
