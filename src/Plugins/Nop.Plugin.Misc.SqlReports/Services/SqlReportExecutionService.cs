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
    private static readonly Regex CommentRegex = new(@"--|/\*", RegexOptions.Compiled);
    private static readonly HashSet<string> ForbiddenTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ALTER", "BACKUP", "CREATE", "DBCC", "DECLARE", "DELETE", "DENY", "DROP", "EXEC", "EXECUTE",
        "GRANT", "INSERT", "MERGE", "RESTORE", "REVOKE", "SET", "TRUNCATE", "UPDATE", "INTO",
        "USE", "WAITFOR", "BULK", "OPENROWSET", "OPENDATASOURCE", "OPENQUERY", "OUTPUT", "PRINT",
        "RAISERROR", "THROW", "BEGIN", "END", "WHILE", "CURSOR", "FETCH", "OPTION"
    };

    private readonly SqlReportsSettings _settings;

    public SqlReportExecutionService(SqlReportsSettings settings)
    {
        _settings = settings;
    }

    public virtual async Task<SqlReportExecutionResult> ExecuteAsync(string sql,
        IEnumerable<SqlReportParameter> knownParameters,
        IDictionary<string, string> values,
        int? maxRows = null)
    {
        EnsureSqlServer();
        ValidateSelectOnly(sql);

        var effectiveMaxRows = Math.Min(Math.Max(maxRows ?? _settings.MaxRowsPerQuery, 1), Math.Max(_settings.MaxRowsPerQuery, 1));
        var dataSettings = DataSettingsManager.LoadSettings();

        await using var connection = new SqlConnection(dataSettings.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        command.CommandTimeout = _settings.CommandTimeoutSeconds > 0 ? _settings.CommandTimeoutSeconds : dataSettings.SQLCommandTimeout ?? 30;

        foreach (var parameter in BuildSqlParameters(sql, knownParameters, values))
            command.Parameters.Add(parameter);

        var stopwatch = Stopwatch.StartNew();
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        var result = new SqlReportExecutionResult();

        for (var i = 0; i < reader.FieldCount; i++)
            result.Columns.Add(reader.GetName(i));

        while (await reader.ReadAsync())
        {
            if (result.Rows.Count >= effectiveMaxRows)
            {
                result.Truncated = true;
                break;
            }

            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in result.Columns.Select((name, index) => new { name, index }))
                row[column.name] = await reader.IsDBNullAsync(column.index) ? null : NormalizeCellValue(reader.GetValue(column.index));

            result.Rows.Add(row);
        }

        stopwatch.Stop();
        result.RowsReturned = result.Rows.Count;
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        return result;
    }

    public virtual byte[] ExportToXlsx(SqlReportExecutionResult result)
    {
        if (!_settings.AllowExport)
            throw new InvalidOperationException("SQL report export is disabled.");

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Report");

        for (var columnIndex = 0; columnIndex < result.Columns.Count; columnIndex++)
            worksheet.Cell(1, columnIndex + 1).Value = result.Columns[columnIndex];

        for (var rowIndex = 0; rowIndex < result.Rows.Count; rowIndex++)
        {
            var row = result.Rows[rowIndex];

            for (var columnIndex = 0; columnIndex < result.Columns.Count; columnIndex++)
            {
                var value = row.TryGetValue(result.Columns[columnIndex], out var cellValue) ? NormalizeCellValue(cellValue) : null;
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

    public virtual IList<SqlParameter> BuildSqlParameters(string sql,
        IEnumerable<SqlReportParameter> knownParameters,
        IDictionary<string, string> values)
    {
        var parametersByName = (knownParameters ?? Enumerable.Empty<SqlReportParameter>())
            .ToDictionary(parameter => NormalizeParameterName(parameter.ParameterName), StringComparer.OrdinalIgnoreCase);

        return ExtractParameterNames(sql)
            .Select(parameterName =>
            {
                parametersByName.TryGetValue(parameterName, out var parameterDefinition);
                var submittedValue = values != null && values.TryGetValue(parameterName, out var value) ? value : parameterDefinition?.DefaultValue;

                return CreateSqlParameter(parameterName, parameterDefinition, submittedValue);
            })
            .ToList();
    }

    public virtual void ValidateSelectOnly(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("SQL query is required.");

        if (CommentRegex.IsMatch(sql))
            throw new InvalidOperationException("SQL comments are not allowed.");

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

        if (tokens.Any(token => token.StartsWith("xp_", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("sp_", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Stored procedure access is not allowed.");
    }

    protected virtual SqlParameter CreateSqlParameter(string parameterName, SqlReportParameter parameterDefinition, string value)
    {
        var sqlParameter = new SqlParameter($"@{parameterName}", DBNull.Value);
        var dataType = parameterDefinition?.DataType ?? SqlReportDataType.Text;

        if (string.IsNullOrWhiteSpace(value))
        {
            if (parameterDefinition?.IsRequired == true)
                throw new InvalidOperationException($"Parameter @{parameterName} is required.");

            return sqlParameter;
        }

        sqlParameter.Value = dataType switch
        {
            SqlReportDataType.Number => decimal.Parse(value, CultureInfo.CurrentCulture),
            SqlReportDataType.NumberList => NormalizeNumberList(value),
            SqlReportDataType.TextList => NormalizeTextList(value),
            _ => value
        };

        return sqlParameter;
    }

    protected virtual object NormalizeCellValue(object value)
    {
        if (value is not string text)
            return value;

        var maxCellLength = _settings.MaxCellLength > 0 ? _settings.MaxCellLength : 4000;
        return text.Length <= maxCellLength ? text : text[..maxCellLength];
    }

    protected virtual string NormalizeTextList(string value)
    {
        return string.Join(",", SplitList(value)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim()));
    }

    protected virtual string NormalizeNumberList(string value)
    {
        return string.Join(",", SplitList(value).Select(item => decimal.Parse(item.Trim(), CultureInfo.CurrentCulture).ToString(CultureInfo.InvariantCulture)));
    }

    protected virtual IEnumerable<string> SplitList(string value)
    {
        return (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
