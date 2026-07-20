using FluentMigrator.Builders.Create.Table;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Data.Mapping.Builders;

public class BookingParticipantBuilder : NopEntityBuilder<BookingParticipant>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(BookingParticipant.BookingId)).AsInt32().ForeignKey<Booking>();
    }
}
