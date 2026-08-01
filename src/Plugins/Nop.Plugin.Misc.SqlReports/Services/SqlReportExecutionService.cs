using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using Nop.Data;
using Nop.Data.Configuration;
using Nop.Plugin.Misc.SqlReports.Domain;

namespace Nop.Plugin.Misc.SqlReports.Services;

public class SqlReportExecutionService
{
    private static readonly Regex ParameterNameRegex = new(@"(?<!@)@([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"\b[A-Za-z_][A-Za-z0-9_]*\b|;", RegexOptions.Compiled);
    private static readonly HashSet<string> ForbiddenTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ALTER", "BACKUP", "CREATE", "DBCC", "DECLARE", "DELETE", "DENY", "DROP", "EXEC", "EXECUTE",
        "GRANT", "INSERT", "MERGE", "RESTORE", "REVOKE", "SET", "TRUNCATE", "UPDATE", "INTO"
    };

    public virtual async Task<SqlReportExecutionResult> ExecuteAsync(string sql,
        IEnumerable<SqlReportParameter> knownParameters,
        IDictionary<string, string> values,
        int maxRows = 200)
    {
        EnsureSqlServer();
        ValidateSelectOnly(sql);

        var parametersByName = (knownParameters ?? Enumerable.Empty<SqlReportParameter>())
            .ToDictionary(parameter => NormalizeParameterName(parameter.ParameterName), StringComparer.OrdinalIgnoreCase);
        var referencedParameterNames = ExtractParameterNames(sql);

        var dataSettings = DataSettingsManager.LoadSettings();

        await using var connection = new SqlConnection(dataSettings.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        if (dataSettings.SQLCommandTimeout.HasValue)
            command.CommandTimeout = dataSettings.SQLCommandTimeout.Value;

        foreach (var parameterName in referencedParameterNames)
        {
            parametersByName.TryGetValue(parameterName, out var parameterDefinition);
            var submittedValue = values != null && values.TryGetValue(parameterName, out var value) ? value : parameterDefinition?.DefaultValue;
            command.Parameters.Add(CreateSqlParameter(parameterName, parameterDefinition, submittedValue));
        }

        var stopwatch = Stopwatch.StartNew();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        var result = new SqlReportExecutionResult();

        for (var i = 0; i < reader.FieldCount; i++)
            result.Columns.Add(reader.GetName(i));

        while (await reader.ReadAsync())
        {
            if (result.Rows.Count >= maxRows)
            {
                result.Truncated = true;
                break;
            }

            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in result.Columns.Select((name, index) => new { name, index }))
                row[column.name] = await reader.IsDBNullAsync(column.index) ? null : reader.GetValue(column.index);

            result.Rows.Add(row);
        }

        stopwatch.Stop();
        result.RowsReturned = result.Rows.Count;
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        return result;
    }

    public virtual byte[] ExportToXlsx(SqlReportExecutionResult result)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Report");

        for (var columnIndex = 0; columnIndex < result.Columns.Count; columnIndex++)
            worksheet.Cell(1, columnIndex + 1).Value = result.Columns[columnIndex];

        for (var rowIndex = 0; rowIndex < result.Rows.Count; rowIndex++)
        {
            var row = result.Rows[rowIndex];

            for (var columnIndex = 0; columnIndex < result.Columns.Count; columnIndex++)
            {
                var value = row.TryGetValue(result.Columns[columnIndex], out var cellValue) ? cellValue : null;
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Value = XLCellValue.FromObject(value);
            }
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    public virtual IList<string> ExtractParameterNames(string sql)
    {
        return ParameterNameRegex.Matches(RemoveCommentsAndStrings(sql ?? string.Empty))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public virtual void ValidateSelectOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("SQL query is required.");

        var cleaned = RemoveCommentsAndStrings(sql).Trim();
        cleaned = Regex.Replace(cleaned, @";+\s*$", string.Empty).Trim();

        if (cleaned.Contains(';'))
            throw new InvalidOperationException("Only a single SELECT statement is allowed.");

        var tokens = TokenRegex.Matches(cleaned)
            .Select(match => match.Value)
            .Where(token => token != ";")
            .ToList();

        if (!tokens.Any() || !string.Equals(tokens[0], "SELECT", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(tokens[0], "WITH", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only SELECT statements are allowed.");

        if (tokens.Any(token => ForbiddenTokens.Contains(token)))
            throw new InvalidOperationException("Only read-only SELECT statements are allowed.");
    }

    protected virtual SqlParameter CreateSqlParameter(string parameterName, SqlReportParameter parameterDefinition, string value)
    {
        var sqlParameter = new SqlParameter($"@{parameterName}", DBNull.Value);
        var dataType = parameterDefinition?.DataType ?? SqlReportDataType.String;

        if (string.IsNullOrWhiteSpace(value))
        {
            if (parameterDefinition?.IsRequired == true)
                throw new InvalidOperationException($"Parameter @{parameterName} is required.");

            return sqlParameter;
        }

        sqlParameter.Value = dataType switch
        {
            SqlReportDataType.Int32 => int.Parse(value, CultureInfo.CurrentCulture),
            SqlReportDataType.Decimal => decimal.Parse(value, CultureInfo.CurrentCulture),
            SqlReportDataType.Boolean => bool.Parse(value),
            SqlReportDataType.DateTime => DateTime.Parse(value, CultureInfo.CurrentCulture),
            _ => value
        };

        return sqlParameter;
    }

    protected virtual string NormalizeParameterName(string parameterName)
    {
        return (parameterName ?? string.Empty).Trim().TrimStart('@');
    }

    protected virtual void EnsureSqlServer()
    {
        var dataSettings = DataSettingsManager.LoadSettings();
        if (dataSettings.DataProvider != DataProviderType.SqlServer)
            throw new InvalidOperationException("SQL Reports V1 supports Microsoft SQL Server only.");
    }

    protected virtual string RemoveCommentsAndStrings(string sql)
    {
        var builder = new StringBuilder(sql.Length);

        for (var index = 0; index < sql.Length; index++)
        {
            if (index + 1 < sql.Length && sql[index] == '-' && sql[index + 1] == '-')
            {
                while (index < sql.Length && sql[index] != '\n')
                    index++;

                builder.Append(' ');
                continue;
            }

            if (index + 1 < sql.Length && sql[index] == '/' && sql[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < sql.Length && (sql[index] != '*' || sql[index + 1] != '/'))
                    index++;

                index++;
                builder.Append(' ');
                continue;
            }

            if (sql[index] == '\'')
            {
                index++;
                while (index < sql.Length)
                {
                    if (sql[index] == '\'' && index + 1 < sql.Length && sql[index + 1] == '\'')
                    {
                        index += 2;
                        continue;
                    }

                    if (sql[index] == '\'')
                        break;

                    index++;
                }

                builder.Append("''");
                continue;
            }

            builder.Append(sql[index]);
        }

        return builder.ToString();
    }
}
