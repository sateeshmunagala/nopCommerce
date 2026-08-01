using Microsoft.Data.SqlClient;
using Nop.Plugin.Misc.SqlReports;
using Nop.Plugin.Misc.SqlReports.Domain;
using Nop.Plugin.Misc.SqlReports.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.SqlReports.Tests;

[TestFixture]
public class SqlReportExecutionServiceTests
{
    [TestCase("select 1")]
    [TestCase(" SELECT TOP 10 Id, Name FROM Customer")]
    [TestCase("with totals as (select 1 as Id) select Id from totals")]
    [TestCase("select 'delete from Customer' as TextValue")]
    [TestCase("select [Name] from Customer where Id = @CustomerId")]
    [TestCase("select case when Total > 0 then 'Paid' else 'Free' end as PaymentStatus from [Order]")]
    public void ValidateSelectOnly_Allows_ReadOnlySingleStatements(string sql)
    {
        Assert.DoesNotThrow(() => CreateService().ValidateSelectOnly(sql));
    }

    [TestCase("")]
    [TestCase("select 1; select 2")]
    [TestCase("update Customer set Active = 0")]
    [TestCase("delete from Customer")]
    [TestCase("select * into #result from Customer")]
    [TestCase("select * from Customer option (recompile)")]
    [TestCase("select * from openrowset('SQLNCLI', 'Server=.;Trusted_Connection=yes;', 'select 1')")]
    [TestCase("select 1 -- trailing comment")]
    [TestCase("select 1 /* block comment */")]
    [TestCase("exec sp_who")]
    [TestCase("select * from Customer; drop table Customer")]
    [TestCase("use OtherDatabase select 1")]
    [TestCase("waitfor delay '00:00:01'; select 1")]
    public void ValidateSelectOnly_Denies_UnsafeOrMultiStatementSql(string sql)
    {
        Assert.Throws<InvalidOperationException>(() => CreateService().ValidateSelectOnly(sql));
    }

    [Test]
    public void ExtractParameterNames_Ignores_StringLiterals_And_Deduplicates()
    {
        var names = CreateService().ExtractParameterNames("select '@Ignored' as Literal, @CustomerId as Id, @customerId as Duplicate, @FromDate as FromDate");

        Assert.That(names, Is.EqualTo(new[] { "CustomerId", "FromDate" }));
    }

    [Test]
    public void BuildSqlParameters_Binds_V1_ParameterTypes()
    {
        var parameters = CreateService().BuildSqlParameters(
            "select * from Orders where Name = @CustomerName and Total >= @MinTotal and Status in (select value from string_split(@Statuses, ',')) and VendorId in (select value from string_split(@VendorIds, ','))",
            new[]
            {
                new SqlReportParameter { ParameterName = "CustomerName", DataType = SqlReportDataType.Text },
                new SqlReportParameter { ParameterName = "MinTotal", DataType = SqlReportDataType.Number },
                new SqlReportParameter { ParameterName = "Statuses", DataType = SqlReportDataType.TextList },
                new SqlReportParameter { ParameterName = "VendorIds", DataType = SqlReportDataType.NumberList }
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CustomerName"] = "Acme",
                ["MinTotal"] = "12.50",
                ["Statuses"] = "Paid, Pending",
                ["VendorIds"] = "1, 2,3.5"
            });

        AssertParameter(parameters, "@CustomerName", "Acme");
        AssertParameter(parameters, "@MinTotal", 12.50m);
        AssertParameter(parameters, "@Statuses", "Paid,Pending");
        AssertParameter(parameters, "@VendorIds", "1,2,3.5");
    }

    [Test]
    public void BuildSqlParameters_Uses_DefaultValue_ForMissingOptionalValue()
    {
        var parameters = CreateService().BuildSqlParameters(
            "select * from Orders where Status = @Status",
            new[] { new SqlReportParameter { ParameterName = "Status", DataType = SqlReportDataType.Text, DefaultValue = "Paid" } },
            new Dictionary<string, string>());

        AssertParameter(parameters, "@Status", "Paid");
    }

    [Test]
    public void BuildSqlParameters_Throws_ForMissingRequiredValue()
    {
        Assert.Throws<InvalidOperationException>(() => CreateService().BuildSqlParameters(
            "select * from Orders where Id = @OrderId",
            new[] { new SqlReportParameter { ParameterName = "OrderId", DataType = SqlReportDataType.Number, IsRequired = true } },
            new Dictionary<string, string>()));
    }

    [Test]
    public void BuildSqlParameters_Throws_ForInvalidNumberList()
    {
        Assert.Throws<FormatException>(() => CreateService().BuildSqlParameters(
            "select * from Orders where Id in (select value from string_split(@OrderIds, ','))",
            new[] { new SqlReportParameter { ParameterName = "OrderIds", DataType = SqlReportDataType.NumberList } },
            new Dictionary<string, string> { ["OrderIds"] = "1,two,3" }));
    }

    [Test]
    public void ExportToXlsx_Throws_WhenExportDisabled()
    {
        var service = CreateService(allowExport: false);

        Assert.Throws<InvalidOperationException>(() => service.ExportToXlsx(CreateResult()));
    }

    [Test]
    public void ExportToXlsx_Returns_XlsxPackage_WhenEnabled()
    {
        var bytes = CreateService().ExportToXlsx(CreateResult());

        Assert.That(bytes, Is.Not.Empty);
        Assert.That(bytes[0], Is.EqualTo((byte)'P'));
        Assert.That(bytes[1], Is.EqualTo((byte)'K'));
    }

    private static SqlReportExecutionService CreateService(bool allowExport = true)
    {
        return new SqlReportExecutionService(new SqlReportsSettings
        {
            MaxRowsPerQuery = 10,
            CommandTimeoutSeconds = 5,
            MaxCellLength = 20,
            EnableInstantQuery = true,
            AllowExport = allowExport
        });
    }

    private static SqlReportExecutionResult CreateResult()
    {
        return new SqlReportExecutionResult
        {
            Columns = { "Name", "Total" },
            Rows =
            {
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Name"] = "First customer with a long display name",
                    ["Total"] = 12.5m
                }
            },
            RowsReturned = 1
        };
    }

    private static void AssertParameter(IEnumerable<SqlParameter> parameters, string name, object value)
    {
        var parameter = parameters.Single(item => item.ParameterName == name);

        Assert.That(parameter.Value, Is.EqualTo(value));
    }
}
