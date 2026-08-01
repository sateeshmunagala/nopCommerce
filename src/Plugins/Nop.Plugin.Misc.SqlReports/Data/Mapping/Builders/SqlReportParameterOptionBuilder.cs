using FluentMigrator.Builders.Create.Table;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.SqlReports.Domain;

namespace Nop.Plugin.Misc.SqlReports.Data.Mapping.Builders;

public class SqlReportParameterOptionBuilder : NopEntityBuilder<SqlReportParameterOption>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(SqlReportParameterOption.SqlReportParameterId)).AsInt32().ForeignKey<SqlReportParameter>().NotNullable()
            .WithColumn(nameof(SqlReportParameterOption.Value)).AsString(400).NotNullable()
            .WithColumn(nameof(SqlReportParameterOption.Text)).AsString(400).NotNullable();
    }
}
