using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Vendors;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.AppointmentBooking.Domains;

namespace Nop.Plugin.Misc.AppointmentBooking.Data.Mapping.Builders;

public class ServiceProductMappingBuilder : NopEntityBuilder<ServiceProductMapping>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table.WithColumn(nameof(ServiceProductMapping.ServiceId)).AsInt32().ForeignKey<BookableService>();
        table.WithColumn(nameof(ServiceProductMapping.ProductId)).AsInt32().ForeignKey<Product>(onDelete: Rule.None);
        table.WithColumn(nameof(ServiceProductMapping.VendorId)).AsInt32().ForeignKey<Vendor>(onDelete: Rule.None);
    }
}
