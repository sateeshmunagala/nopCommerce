using System.Diagnostics;
using LinqToDB.Data;
using Nop.Data;
using Nop.Data.DataProviders;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Data;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportProfileQueryService : IJobSupportProfileQueryService
{
    private readonly INopDataProvider _dataProvider;
    private readonly IJobSupportCutoverService _cutoverService;
    private readonly ILogger _logger;
    private readonly JobSupportPluginQueryService _pluginQueryService;
    private readonly JobSupportSettings _settings;

    public JobSupportProfileQueryService(INopDataProvider dataProvider,
        IJobSupportCutoverService cutoverService,
        JobSupportPluginQueryService pluginQueryService,
        JobSupportSettings settings,
        ILogger logger)
    {
        _dataProvider = dataProvider;
        _cutoverService = cutoverService;
        _pluginQueryService = pluginQueryService;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PagedProfileSearchResult> SearchProfilesAsync(ProfileSearchRequest request)
    {
        if (!_settings.Enabled)
            return Failed(request, ProfileQueryErrorCode.Disabled);

        return _settings.DataReadMode switch
        {
            DataAccessMode.Legacy => await SearchLegacyProfilesAsync(request),
            DataAccessMode.Compare => await CompareAsync(request, relationships: false),
            DataAccessMode.Plugin => await _pluginQueryService.SearchProfilesAsync(request),
            _ => Failed(request, ProfileQueryErrorCode.Disabled)
        };
    }

    public async Task<PagedProfileSearchResult> GetProfilesByRelationshipAsync(ProfileSearchRequest request)
    {
        if (!_settings.Enabled)
            return Failed(request, ProfileQueryErrorCode.Disabled);

        return _settings.DataReadMode switch
        {
            DataAccessMode.Legacy => await SearchLegacyRelationshipsAsync(request),
            DataAccessMode.Compare => await CompareAsync(request, relationships: true),
            DataAccessMode.Plugin => await _pluginQueryService.GetProfilesByRelationshipAsync(request),
            _ => Failed(request, ProfileQueryErrorCode.Disabled)
        };
    }

    private async Task<PagedProfileSearchResult> CompareAsync(ProfileSearchRequest request, bool relationships)
    {
        var stopwatch = Stopwatch.StartNew();
        var legacy = relationships
            ? await SearchLegacyRelationshipsAsync(request)
            : await SearchLegacyProfilesAsync(request);
        stopwatch.Stop();
        var legacyDuration = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        var plugin = relationships
            ? await _pluginQueryService.GetProfilesByRelationshipAsync(request)
            : await _pluginQueryService.SearchProfilesAsync(request);
        stopwatch.Stop();
        await _cutoverService.RecordComparisonAsync(relationships ? "Relationships" : "ProfileSearch",
            legacy, plugin, legacyDuration, stopwatch.ElapsedMilliseconds);

        return _settings.CompareReturnMode == DataAccessMode.Plugin ? plugin : legacy;
    }

    private async Task<PagedProfileSearchResult> SearchLegacyProfilesAsync(ProfileSearchRequest request)
    {
        var procedureName = _settings.LegacyProfileSearchProcedureName;
        var guard = Validate(request, procedureName);
        if (guard != ProfileQueryErrorCode.None)
            return Failed(request, guard);

        var parameters = LegacyProcedureParameterFactory.CreateProfileSearchParameters(request);

        try
        {
            var rows = await _dataProvider.QueryProcAsync<ProfileSearchResult>(procedureName, parameters);
            NormalizeUtcDates(rows);

            return Success(request, rows, rows.Count, null, ProfileQuerySource.LegacyProcedure);
        }
        catch (Exception exception)
        {
            await LogProcedureFailureAsync(procedureName, parameters, exception);
            return Failed(request, ProfileQueryErrorCode.ProcedureExecutionFailed);
        }
    }

    private async Task<PagedProfileSearchResult> SearchLegacyRelationshipsAsync(ProfileSearchRequest request)
    {
        var procedureName = _settings.LegacyShortlistProcedureName;
        var guard = Validate(request, procedureName);
        if (guard != ProfileQueryErrorCode.None)
            return Failed(request, guard);

        if (!request.RelationshipType.HasValue ||
            !LegacyRelationshipMapper.TryMapToLegacyCartType(request.RelationshipType.Value, out var shoppingCartTypeId))
        {
            return Failed(request, ProfileQueryErrorCode.NotSupported);
        }

        var parameters = LegacyProcedureParameterFactory.CreateShortListParameters(request,
            shoppingCartTypeId,
            out var totalRecordsParameter);

        try
        {
            var rows = await _dataProvider.QueryProcAsync<ProfileSearchResult>(procedureName, parameters);
            NormalizeUtcDates(rows);

            var outputTotalRecords = ReadNullableInt32(totalRecordsParameter);
            return Success(request,
                rows,
                outputTotalRecords.GetValueOrDefault(),
                outputTotalRecords,
                ProfileQuerySource.LegacyProcedure);
        }
        catch (Exception exception)
        {
            await LogProcedureFailureAsync(procedureName, parameters, exception);
            return Failed(request, ProfileQueryErrorCode.ProcedureExecutionFailed);
        }
    }

    private ProfileQueryErrorCode Validate(ProfileSearchRequest request, string procedureName)
    {
        if (!_settings.Enabled || !_settings.UseLegacyStoredProcedures)
            return ProfileQueryErrorCode.Disabled;
        if (_dataProvider is not MsSqlNopDataProvider)
            return ProfileQueryErrorCode.UnsupportedProvider;
        if (string.IsNullOrWhiteSpace(procedureName))
            return ProfileQueryErrorCode.MissingProcedureName;
        if (request == null || request.PageSize <= 0 || request.PageIndex < 0)
            return ProfileQueryErrorCode.InvalidRequest;

        return ProfileQueryErrorCode.None;
    }

    private static PagedProfileSearchResult Success(ProfileSearchRequest request,
        IList<ProfileSearchResult> rows,
        int totalRecords,
        int? outputTotalRecords,
        ProfileQuerySource source)
    {
        var warnings = rows
            .Where(row => row.Id <= 0)
            .Select((_, index) => $"Returned profile row {index + 1} does not contain a profile identifier.")
            .ToList();
        if (!outputTotalRecords.HasValue)
            warnings.Add("The output total record count was not returned.");
        else if (outputTotalRecords.Value < rows.Count)
            warnings.Add("The output total record count is lower than the returned row count.");

        return new PagedProfileSearchResult
        {
            Items = rows,
            PageIndex = request.PageIndex,
            PageSize = request.PageSize,
            TotalRecords = totalRecords,
            OutputTotalRecords = outputTotalRecords,
            ReturnedRowCount = rows.Count,
            Succeeded = true,
            ErrorCode = ProfileQueryErrorCode.None,
            Source = source,
            MappingWarnings = warnings
        };
    }

    private static PagedProfileSearchResult Failed(ProfileSearchRequest request, ProfileQueryErrorCode errorCode)
    {
        return new PagedProfileSearchResult
        {
            PageIndex = request?.PageIndex ?? 0,
            PageSize = request?.PageSize ?? 0,
            Succeeded = false,
            ErrorCode = errorCode,
            Source = ProfileQuerySource.None
        };
    }

    private static int? ReadNullableInt32(DataParameter parameter)
    {
        if (parameter.Value == null || parameter.Value == DBNull.Value)
            return null;

        return Convert.ToInt32(parameter.Value);
    }

    private static void NormalizeUtcDates(IEnumerable<ProfileSearchResult> rows)
    {
        foreach (var row in rows)
        {
            row.LastLoginDateUtc = AsUtc(row.LastLoginDateUtc);
            row.LastActivityDateUtc = AsUtc(row.LastActivityDateUtc);
        }
    }

    private static DateTime? AsUtc(DateTime? value)
    {
        if (!value.HasValue || value.Value.Kind == DateTimeKind.Utc)
            return value;

        return DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    private Task LogProcedureFailureAsync(string procedureName,
        IEnumerable<DataParameter> parameters,
        Exception exception)
    {
        var parameterNames = string.Join(", ", parameters.Select(parameter => parameter.Name));
        return _logger.ErrorAsync(
            $"JobSupport legacy query failed for procedure {procedureName}; parameters: {parameterNames}",
            exception);
    }
}
