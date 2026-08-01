using FluentMigrator.Builders.Create.Table;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.SqlReports.Domain;

namespace Nop.Plugin.Misc.SqlReports.Data.Mapping.Builders;

public class SqlReportParameterMappingBuilder : NopEntityBuilder<SqlReportParameterMapping>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(SqlReportParameterMapping.SqlReportId)).AsInt32().ForeignKey<SqlReport>().NotNullable()
            .WithColumn(nameof(SqlReportParameterMapping.SqlReportParameterId)).AsInt32().ForeignKey<SqlReportParameter>().NotNullable();
    }
}
