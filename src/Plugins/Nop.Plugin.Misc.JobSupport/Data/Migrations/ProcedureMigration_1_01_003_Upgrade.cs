using System.Reflection;
using FluentMigrator;
using Nop.Data;
using Nop.Data.Migrations;

namespace Nop.Plugin.Misc.JobSupport.Data.Migrations;

[NopMigration("2026-02-01 00:00:02", "Misc.JobSupport procedure upgrade", MigrationProcessType.Update)]
public class ProcedureMigration_1_01_003_Upgrade : ForwardOnlyMigration
{
    public override void Up()
    {
        if (DataSettingsManager.LoadSettings().DataProvider != DataProviderType.SqlServer)
            return;

        Execute.Sql(ReadEmbeddedScript("JobSupport_ProfileSearch.sql"));
        Execute.Sql(ReadEmbeddedScript("JobSupport_ProfileRelationships.sql"));
    }

    private static string ReadEmbeddedScript(string fileName)
    {
        var assembly = typeof(ProcedureMigration_1_01_003_Upgrade).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith($".Data.Sql.{fileName}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL resource {resourceName} was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
