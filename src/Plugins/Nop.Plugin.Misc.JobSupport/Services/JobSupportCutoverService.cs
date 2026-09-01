using System.Text.Json;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Data;
using Nop.Data.DataProviders;
using Nop.Plugin.Misc.JobSupport.Contracts;
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

    public async Task RecordComparisonAsync(string queryName,
        PagedProfileSearchResult legacy,
        PagedProfileSearchResult plugin,
        long legacyDurationMilliseconds,
        long pluginDurationMilliseconds)
    {
        var legacyIds = legacy.Items.Select(item => item.Id).ToArray();
        var pluginIds = plugin.Items.Select(item => item.Id).ToArray();
        var mismatch = legacy.Succeeded != plugin.Succeeded ||
            legacy.ErrorCode != plugin.ErrorCode ||
            legacy.TotalRecords != plugin.TotalRecords ||
            !legacyIds.SequenceEqual(pluginIds);
        var diagnostic = new SanitizedComparison
        {
            Query = queryName,
            LegacySucceeded = legacy.Succeeded,
            PluginSucceeded = plugin.Succeeded,
            LegacyError = legacy.ErrorCode.ToString(),
            PluginError = plugin.ErrorCode.ToString(),
            LegacyTotal = legacy.TotalRecords,
            PluginTotal = plugin.TotalRecords,
            LegacyProfileIds = legacyIds,
            PluginProfileIds = pluginIds,
            LegacyDurationMilliseconds = legacyDurationMilliseconds,
            PluginDurationMilliseconds = pluginDurationMilliseconds
        };

        var checkpoint = await _checkpointRepository.Table
            .FirstOrDefaultAsync(item => item.MigrationName == CHECKPOINT_NAME);
        var now = DateTime.UtcNow;
        if (checkpoint == null)
        {
            checkpoint = new JobSupportMigrationCheckpoint
            {
                MigrationName = CHECKPOINT_NAME,
                Status = mismatch ? "Mismatch" : "Matched",
                MismatchCount = mismatch ? 1 : 0,
                ErrorLog = JsonSerializer.Serialize(diagnostic),
                LastExecutedOnUtc = now,
                UpdatedOnUtc = now
            };
            await _checkpointRepository.InsertAsync(checkpoint, false);
            return;
        }

        checkpoint.Status = mismatch ? "Mismatch" : "Matched";
        if (mismatch)
            checkpoint.MismatchCount++;
        checkpoint.ErrorLog = JsonSerializer.Serialize(diagnostic);
        checkpoint.LastExecutedOnUtc = now;
        checkpoint.UpdatedOnUtc = now;
        await _checkpointRepository.UpdateAsync(checkpoint, false);
    }

    public async Task<CutoverStatusModel> GetStatusAsync()
    {
        var model = new CutoverStatusModel
        {
            ReadMode = _settings.DataReadMode,
            WriteMode = _settings.DataWriteMode,
            CompareReturnMode = NormalizeCompareReturnMode(_settings.CompareReturnMode),
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
        model.ReadModes = Options(new[] { DataAccessMode.Legacy, DataAccessMode.Compare, DataAccessMode.Plugin }, model.ReadMode);
        model.WriteModes = Options(new[] { DataAccessMode.Legacy, DataAccessMode.Dual, DataAccessMode.Plugin }, model.WriteMode);
        model.CompareReturnModes = Options(new[] { DataAccessMode.Legacy, DataAccessMode.Plugin }, model.CompareReturnMode);
    }

    private static IList<SelectListItem> Options(IEnumerable<DataAccessMode> values, DataAccessMode selected) =>
        values.Select(value => new SelectListItem(value.ToString(), ((int)value).ToString(), value == selected)).ToList();

    private static DataAccessMode NormalizeCompareReturnMode(DataAccessMode mode) =>
        mode == DataAccessMode.Plugin ? DataAccessMode.Plugin : DataAccessMode.Legacy;

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
