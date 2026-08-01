using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using Nop.Plugin.Misc.SqlReports.Domain;

namespace Nop.Plugin.Misc.SqlReports.Data.Mapping.Builders;

public class SqlReportBuilder : NopEntityBuilder<SqlReport>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(SqlReport.Name)).AsString(400).NotNullable()
            .WithColumn(nameof(SqlReport.SystemName)).AsString(400).Nullable()
            .WithColumn(nameof(SqlReport.Description)).AsString(int.MaxValue).Nullable()
            .WithColumn(nameof(SqlReport.SqlQuery)).AsString(int.MaxValue).NotNullable();
    }
}
