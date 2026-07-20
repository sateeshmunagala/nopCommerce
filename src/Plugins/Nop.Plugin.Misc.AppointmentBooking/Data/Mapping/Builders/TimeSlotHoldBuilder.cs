using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Vendors;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Data.Mapping.Builders;

public class TimeSlotHoldBuilder : NopEntityBuilder<TimeSlotHold>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(TimeSlotHold.ServiceId)).AsInt32().ForeignKey<BookableService>();
        table.WithColumn(nameof(TimeSlotHold.ProductId)).AsInt32().ForeignKey<Product>(onDelete: Rule.None);
        table.WithColumn(nameof(TimeSlotHold.VendorId)).AsInt32().ForeignKey<Vendor>(onDelete: Rule.None);
        table.WithColumn(nameof(TimeSlotHold.CustomerId)).AsInt32().ForeignKey<Customer>(onDelete: Rule.None);
    }
}
