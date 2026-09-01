using System.Text.Json;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Data;
using Nop.Data.DataProviders;
using Nop.Plugin.Misc.JobSupport.Domain;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;
using Nop.Plugin.Misc.JobSupport.Models.Admin;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportCutoverService : IJobSupportCutoverService
{
    private const string CHECKPOINT_NAME = "ReadCutoverComparison";
    private const string PROCEDURE_VERSION = "1.01.001";

    private readonly IRepository<JobSupportMigrationCheckpoint> _checkpointRepository;
    private readonly INopDataProvider _dataProvider;
    private readonly JobSupportSettings _settings;

    public JobSupportCutoverService(IRepository<JobSupportMigrationCheckpoint> checkpointRepository,
        INopDataProvider dataProvider,
        JobSupportSettings settings)
    {
        _checkpointRepository = checkpointRepository;
        _dataProvider = dataProvider;
        _settings = settings;
    }

    public async Task<CutoverStatusModel> GetStatusAsync()
    {
        var model = new CutoverStatusModel
        {
            ReadMode = _settings.DataReadMode,
            WriteMode = DataAccessMode.Plugin,
            ProviderStatus = _dataProvider is MsSqlNopDataProvider ? "SqlServer" : "UnsupportedProvider"
        };
        AddModeOptions(model);

        var checkpoint = await _checkpointRepository.Table
            .FirstOrDefaultAsync(item => item.MigrationName == CHECKPOINT_NAME);
        if (checkpoint != null)
        {
            model.MismatchCount = checkpoint.MismatchCount;
            model.LastComparisonOnUtc = checkpoint.LastExecutedOnUtc;
            if (!string.IsNullOrWhiteSpace(checkpoint.ErrorLog))
            {
                try
                {
                    var diagnostic = JsonSerializer.Deserialize<SanitizedComparison>(checkpoint.ErrorLog);
                    model.LegacyDurationMilliseconds = diagnostic?.LegacyDurationMilliseconds;
                    model.PluginDurationMilliseconds = diagnostic?.PluginDurationMilliseconds;
                }
                catch (JsonException)
                {
                    // Older diagnostic text is intentionally ignored.
                }
            }
        }

        if (_dataProvider is not MsSqlNopDataProvider)
            return model;

        var legacyProfileName = ProcedureNameOnly(_settings.LegacyProfileSearchProcedureName);
        var legacyRelationshipName = ProcedureNameOnly(_settings.LegacyShortlistProcedureName);
        var rows = await _dataProvider.QueryAsync<ProcedurePresence>(
            "SELECT [name] AS [Name] FROM sys.procedures WHERE [name] IN (@LegacyProfile, @LegacyRelationship, @PluginProfile, @PluginRelationship)",
            new DataParameter("LegacyProfile", legacyProfileName, DataType.NVarChar),
            new DataParameter("LegacyRelationship", legacyRelationshipName, DataType.NVarChar),
            new DataParameter("PluginProfile", "JobSupport_ProfileSearch", DataType.NVarChar),
            new DataParameter("PluginRelationship", "JobSupport_ProfileRelationships", DataType.NVarChar));
        var names = rows.Select(row => row.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        model.LegacyProfileSearchPresent = !string.IsNullOrWhiteSpace(legacyProfileName) && names.Contains(legacyProfileName);
        model.LegacyRelationshipProcedurePresent = !string.IsNullOrWhiteSpace(legacyRelationshipName) && names.Contains(legacyRelationshipName);
        model.PluginProfileSearchPresent = names.Contains("JobSupport_ProfileSearch");
        model.PluginRelationshipProcedurePresent = names.Contains("JobSupport_ProfileRelationships");
        model.PluginProcedureVersion = model.PluginProfileSearchPresent && model.PluginRelationshipProcedurePresent
            ? PROCEDURE_VERSION
            : "Not installed";
        model.RollbackReady = model.LegacyProfileSearchPresent && model.LegacyRelationshipProcedurePresent;
        return model;
    }

    private static void AddModeOptions(CutoverStatusModel model)
    {
        model.ReadModes = Options(new[] { DataAccessMode.Plugin, DataAccessMode.Legacy }, model.ReadMode);
        model.WriteModes = Options(new[] { DataAccessMode.Plugin }, DataAccessMode.Plugin);
    }

    private static IList<SelectListItem> Options(IEnumerable<DataAccessMode> values, DataAccessMode selected) =>
        values.Select(value => new SelectListItem(value.ToString(), ((int)value).ToString(), value == selected)).ToList();

    private static string ProcedureNameOnly(string value)
    {
        var name = (value ?? string.Empty).Trim().Trim('[', ']');
        var separator = name.LastIndexOf('.');
        return (separator < 0 ? name : name[(separator + 1)..]).Trim('[', ']');
    }

    private sealed class ProcedurePresence
    {
        public string Name { get; set; }
    }

    private sealed class SanitizedComparison
    {
        public string Query { get; set; }
        public bool LegacySucceeded { get; set; }
        public bool PluginSucceeded { get; set; }
        public string LegacyError { get; set; }
        public string PluginError { get; set; }
        public int LegacyTotal { get; set; }
        public int PluginTotal { get; set; }
        public int[] LegacyProfileIds { get; set; } = Array.Empty<int>();
        public int[] PluginProfileIds { get; set; } = Array.Empty<int>();
        public long LegacyDurationMilliseconds { get; set; }
        public long PluginDurationMilliseconds { get; set; }
    }
}
