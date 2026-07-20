using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Data.Mapping.Builders;

public class BookingAnswerBuilder : NopEntityBuilder<BookingAnswer>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(BookingAnswer.BookingId)).AsInt32().ForeignKey<Booking>();
        table.WithColumn(nameof(BookingAnswer.ServiceQuestionId)).AsInt32().ForeignKey<ServiceQuestion>(onDelete: Rule.None);
    }
}
