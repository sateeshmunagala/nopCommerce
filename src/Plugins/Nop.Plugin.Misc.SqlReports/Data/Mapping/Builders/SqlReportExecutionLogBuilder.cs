using System.Data;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using Nop.Data.Extensions;
using Nop.Plugin.Misc.SqlReports.Domain;

namespace Nop.Plugin.Misc.SqlReports.Data.Mapping.Builders;

public class SqlReportExecutionLogBuilder : NopEntityBuilder<SqlReportExecutionLog>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(SqlReportExecutionLog.SqlReportId)).AsInt32().ForeignKey<SqlReport>(onDelete: Rule.SetNull).Nullable()
            .WithColumn(nameof(SqlReportExecutionLog.Error)).AsString(1000).Nullable();
    }
}
