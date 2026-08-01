using NUnit.Framework;

namespace Nop.Plugin.Misc.SqlReports.Tests;

[TestFixture]
public class SqlReportMigrationTests
{
    [Test]
    public void ExecutionLogReportForeignKeyMigration_DropsExistingForeignKeysByMetadata()
    {
        var migrationText = File.ReadAllText(GetPluginFilePath("Data", "Migrations", "ExecutionLogReportForeignKeyMigration.cs"));

        Assert.That(migrationText, Does.Contain("sys.foreign_keys"));
        Assert.That(migrationText, Does.Contain("sys.foreign_key_columns"));
        Assert.That(migrationText, Does.Contain("OnDelete(Rule.SetNull)"));
        Assert.That(migrationText, Does.Not.Contain("Schema.Table(nameof(SqlReportExecutionLog)).Constraint(ForeignKeyName).Exists()"));
    }

    private static string GetPluginFilePath(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName, "Plugins", "Nop.Plugin.Misc.SqlReports" }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate SQL Reports plugin source file.", Path.Combine(relativeParts));
    }
}
