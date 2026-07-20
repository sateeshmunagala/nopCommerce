using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Vendors;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Data.Mapping.Builders;

public class BookingBuilder : NopEntityBuilder<Booking>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(Booking.ServiceId)).AsInt32().ForeignKey<BookableService>();
        table.WithColumn(nameof(Booking.ProductId)).AsInt32().ForeignKey<Product>(onDelete: Rule.None);
        table.WithColumn(nameof(Booking.VendorId)).AsInt32().ForeignKey<Vendor>(onDelete: Rule.None);
        table.WithColumn(nameof(Booking.CustomerId)).AsInt32().ForeignKey<Customer>(onDelete: Rule.None);
        table.WithColumn(nameof(Booking.OrderId)).AsInt32().Nullable().ForeignKey<Order>(onDelete: Rule.None);
        table.WithColumn(nameof(Booking.OrderItemId)).AsInt32().Nullable().ForeignKey<OrderItem>(onDelete: Rule.None);
    }
}
