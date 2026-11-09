using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.SqlReports.Domain;

namespace Nop.Plugin.Misc.SqlReports.Data.Mapping.Builders;

public class SqlReportParameterBuilder : NopEntityBuilder<SqlReportParameter>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(SqlReportParameter.Name)).AsString(400).NotNullable()
            .WithColumn(nameof(SqlReportParameter.ParameterName)).AsString(128).NotNullable()
            .WithColumn(nameof(SqlReportParameter.DataType)).AsString(50).NotNullable()
            .WithColumn(nameof(SqlReportParameter.DefaultValue)).AsString(4000).Nullable()
            .WithColumn(nameof(SqlReportParameter.Prompt)).AsString(400).Nullable();
    }
}
