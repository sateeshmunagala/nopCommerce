using System.Reflection;
using FluentMigrator;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Nop.Plugin.Misc.SqlReports.Data.Migrations;
using NUnit.Framework;

namespace Nop.Plugin.Misc.SqlReports.Tests;

[TestFixture]
public class SqlReportMigrationIntegrationTests
{
    private const string DefaultMasterConnectionString = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True";

    [Test]
    public async Task ExecutionLogReportForeignKeyMigration_RecreatesVariantForeignKey_WithSetNullDelete()
    {
        var masterConnectionString = GetMasterConnectionString();
        var databaseName = $"NopSqlReportsFk_{Guid.NewGuid():N}";
        var databaseConnectionString = BuildDatabaseConnectionString(masterConnectionString, databaseName);

        await using var masterConnection = new SqlConnection(masterConnectionString);
        try
        {
            await masterConnection.OpenAsync();
        }
        catch (Exception exception)
        {
            Assert.Ignore($"SQL Server integration test skipped. Unable to connect to SQL Server test instance: {exception.Message}");
            return;
        }

        await ExecuteNonQueryAsync(masterConnection, $"CREATE DATABASE [{databaseName}]");

        try
        {
            await using var databaseConnection = new SqlConnection(databaseConnectionString);
            await databaseConnection.OpenAsync();
            await CreateVariantSchemaAsync(databaseConnection);
            await SeedVersionInfoAsync(databaseConnection);

            RunMigration(databaseConnectionString);

            var foreignKey = await ReadExecutionLogReportForeignKeyAsync(databaseConnection);
            Assert.That(foreignKey.Name, Is.EqualTo("FK_SqlReportExecutionLog_SqlReport"));
            Assert.That(foreignKey.DeleteAction, Is.EqualTo("SET_NULL"));

            await ExecuteNonQueryAsync(databaseConnection, "DELETE FROM dbo.SqlReport WHERE Id = 1;");
            var retainedLogReportId = await ExecuteScalarAsync<int?>(databaseConnection, "SELECT SqlReportId FROM dbo.SqlReportExecutionLog WHERE Id = 1;");

            Assert.That(retainedLogReportId, Is.Null);
        }
        finally
        {
            await ExecuteNonQueryAsync(masterConnection, $@"
ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [{databaseName}];");
        }
    }

    [Test]
    public async Task ExecutionLogReportForeignKeyMigration_SkipsCleanDatabaseWithoutSqlReportsTables()
    {
        var masterConnectionString = GetMasterConnectionString();
        var databaseName = $"NopSqlReportsFkClean_{Guid.NewGuid():N}";
        var databaseConnectionString = BuildDatabaseConnectionString(masterConnectionString, databaseName);

        await using var masterConnection = new SqlConnection(masterConnectionString);
        try
        {
            await masterConnection.OpenAsync();
        }
        catch (Exception exception)
        {
            Assert.Ignore($"SQL Server integration test skipped. Unable to connect to SQL Server test instance: {exception.Message}");
            return;
        }

        await ExecuteNonQueryAsync(masterConnection, $"CREATE DATABASE [{databaseName}]");

        try
        {
            await using var databaseConnection = new SqlConnection(databaseConnectionString);
            await databaseConnection.OpenAsync();
            await SeedVersionInfoAsync(databaseConnection);

            Assert.DoesNotThrow(() => RunMigration(databaseConnectionString));
        }
        finally
        {
            await ExecuteNonQueryAsync(masterConnection, $@"
ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [{databaseName}];");
        }
    }

    private static string GetMasterConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("SQLREPORTS_SQLSERVER_MASTER_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return DefaultMasterConnectionString;
    }

    private static string BuildDatabaseConnectionString(string masterConnectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName
        };

        return builder.ConnectionString;
    }

    private static async Task CreateVariantSchemaAsync(SqlConnection connection)
    {
        await ExecuteNonQueryAsync(connection, @"
CREATE TABLE dbo.SqlReport
(
    Id int NOT NULL CONSTRAINT PK_SqlReport PRIMARY KEY,
    Name nvarchar(400) NULL
);

CREATE TABLE dbo.SqlReportExecutionLog
(
    Id int NOT NULL CONSTRAINT PK_SqlReportExecutionLog PRIMARY KEY,
    SqlReportId int NULL,
    CustomerId int NOT NULL,
    DurationMs bigint NOT NULL,
    RowsReturned int NOT NULL,
    Success bit NOT NULL,
    Error nvarchar(1000) NULL,
    CreatedOnUtc datetime2 NOT NULL
);

ALTER TABLE dbo.SqlReportExecutionLog
    ADD CONSTRAINT FK_Custom_SqlReports_ExecutionLog_Report
    FOREIGN KEY (SqlReportId) REFERENCES dbo.SqlReport(Id);

INSERT INTO dbo.SqlReport (Id, Name) VALUES (1, N'Variant FK report');
INSERT INTO dbo.SqlReportExecutionLog (Id, SqlReportId, CustomerId, DurationMs, RowsReturned, Success, Error, CreatedOnUtc)
VALUES (1, 1, 10, 5, 1, 1, NULL, SYSUTCDATETIME());");
    }

    private static async Task SeedVersionInfoAsync(SqlConnection connection)
    {
        await ExecuteNonQueryAsync(connection, @"
CREATE TABLE dbo.VersionInfo
(
    Version bigint NOT NULL CONSTRAINT PK_VersionInfo PRIMARY KEY,
    AppliedOn datetime NULL,
    Description nvarchar(1024) NULL
);");

        await InsertAppliedMigrationAsync<SchemaMigration>(connection);
        await InsertAppliedMigrationAsync<AddLocales>(connection);
    }

    private static async Task InsertAppliedMigrationAsync<TMigration>(SqlConnection connection)
    {
        var attribute = typeof(TMigration).GetCustomAttribute<MigrationAttribute>();

        await ExecuteNonQueryAsync(connection,
            "INSERT INTO dbo.VersionInfo (Version, AppliedOn, Description) VALUES (@Version, SYSUTCDATETIME(), @Description);",
            new SqlParameter("@Version", attribute.Version),
            new SqlParameter("@Description", attribute.Description ?? typeof(TMigration).Name));
    }

    private static void RunMigration(string connectionString)
    {
        var migrationVersion = typeof(ExecutionLogReportForeignKeyMigration).GetCustomAttribute<MigrationAttribute>().Version;

        using var serviceProvider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(builder => builder
                .AddSqlServer()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(ExecutionLogReportForeignKeyMigration).Assembly).For.Migrations())
            .BuildServiceProvider(false);

        using var scope = serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp(migrationVersion);
    }

    private static async Task<(string Name, string DeleteAction)> ReadExecutionLogReportForeignKeyAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT fk.name, fk.delete_referential_action_desc
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
INNER JOIN sys.tables parent ON fk.parent_object_id = parent.object_id
INNER JOIN sys.columns parent_column ON fkc.parent_object_id = parent_column.object_id AND fkc.parent_column_id = parent_column.column_id
INNER JOIN sys.tables referenced ON fk.referenced_object_id = referenced.object_id
INNER JOIN sys.columns referenced_column ON fkc.referenced_object_id = referenced_column.object_id AND fkc.referenced_column_id = referenced_column.column_id
WHERE parent.name = N'SqlReportExecutionLog'
    AND parent_column.name = N'SqlReportId'
    AND referenced.name = N'SqlReport'
    AND referenced_column.name = N'Id';";

        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);

        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task ExecuteNonQueryAsync(SqlConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var parameter in parameters)
            command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ExecuteScalarAsync<T>(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var value = await command.ExecuteScalarAsync();
        if (value is DBNull)
            return default;

        return (T)value;
    }
}
