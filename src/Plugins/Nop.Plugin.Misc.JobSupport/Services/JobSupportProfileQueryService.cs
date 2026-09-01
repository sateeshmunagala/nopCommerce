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
    private readonly ILogger _logger;
    private readonly JobSupportPluginQueryService _pluginQueryService;
    private readonly JobSupportSettings _settings;

    public JobSupportProfileQueryService(INopDataProvider dataProvider,
        JobSupportPluginQueryService pluginQueryService,
        JobSupportSettings settings,
        ILogger logger)
    {
        _dataProvider = dataProvider;
        _pluginQueryService = pluginQueryService;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PagedProfileSearchResult> SearchProfilesAsync(ProfileSearchRequest request)
    {
        if (!_settings.Enabled)
            return Failed(request, ProfileQueryErrorCode.Disabled);

        if (_settings.DataReadMode == DataAccessMode.Legacy && _settings.AllowLegacyReadRollback)
            return await SearchLegacyProfilesAsync(request);

        return await _pluginQueryService.SearchProfilesAsync(request);
    }

    public async Task<PagedProfileSearchResult> GetProfilesByRelationshipAsync(ProfileSearchRequest request)
    {
        if (!_settings.Enabled)
            return Failed(request, ProfileQueryErrorCode.Disabled);

        if (_settings.DataReadMode == DataAccessMode.Legacy && _settings.AllowLegacyReadRollback)
            return await SearchLegacyRelationshipsAsync(request);

        return await _pluginQueryService.GetProfilesByRelationshipAsync(request);
    }

    // Compatibility retained for read rollback; remove in JobSupport 2.0.0.
    private async Task<PagedProfileSearchResult> SearchLegacyProfilesAsync(ProfileSearchRequest request)
    {
        var procedureName = _settings.LegacyProfileSearchProcedureName;
        var guard = ValidateLegacyRead(request, procedureName);
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

    // Compatibility retained for read rollback; remove in JobSupport 2.0.0.
    private async Task<PagedProfileSearchResult> SearchLegacyRelationshipsAsync(ProfileSearchRequest request)
    {
        var procedureName = _settings.LegacyShortlistProcedureName;
        var guard = ValidateLegacyRead(request, procedureName);
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

    // Compatibility retained for read rollback; remove in JobSupport 2.0.0.
    private ProfileQueryErrorCode ValidateLegacyRead(ProfileSearchRequest request, string procedureName)
    {
        if (!_settings.AllowLegacyReadRollback || !_settings.UseLegacyStoredProcedures)
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
        if (outputTotalRecords.HasValue && outputTotalRecords.Value < rows.Count)
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

    private static PagedProfileSearchResult Failed(ProfileSearchRequest request, ProfileQueryErrorCode errorCode) => new()
    {
        PageIndex = request?.PageIndex ?? 0,
        PageSize = request?.PageSize ?? 0,
        Succeeded = false,
        ErrorCode = errorCode,
        Source = ProfileQuerySource.None
    };

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
            $"JobSupport legacy rollback query failed for procedure {procedureName}; parameters: {parameterNames}",
            exception);
    }
}
