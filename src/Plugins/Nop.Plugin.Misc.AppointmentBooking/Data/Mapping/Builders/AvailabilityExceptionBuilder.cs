using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Vendors;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Data.Mapping.Builders;

public class AvailabilityExceptionBuilder : NopEntityBuilder<AvailabilityException>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(AvailabilityException.ServiceId)).AsInt32().ForeignKey<BookableService>();
        table.WithColumn(nameof(AvailabilityException.VendorId)).AsInt32().ForeignKey<Vendor>(onDelete: Rule.None);
    }
}
