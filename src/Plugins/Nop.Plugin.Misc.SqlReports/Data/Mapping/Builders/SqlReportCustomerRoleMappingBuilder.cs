using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Customers;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.SqlReports.Domain;

namespace Nop.Plugin.Misc.SqlReports.Data.Mapping.Builders;

public class SqlReportCustomerRoleMappingBuilder : NopEntityBuilder<SqlReportCustomerRoleMapping>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(SqlReportCustomerRoleMapping.SqlReportId)).AsInt32().ForeignKey<SqlReport>().NotNullable()
            .WithColumn(nameof(SqlReportCustomerRoleMapping.CustomerRoleId)).AsInt32().ForeignKey<CustomerRole>().NotNullable();
    }
}
