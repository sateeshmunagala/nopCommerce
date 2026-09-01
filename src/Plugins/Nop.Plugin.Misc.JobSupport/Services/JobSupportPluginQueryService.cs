using LinqToDB.Data;
using Nop.Data;
using Nop.Data.DataProviders;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Data;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportPluginQueryService
{
    private const string PROFILE_SEARCH_PROCEDURE = "JobSupport_ProfileSearch";
    private const string RELATIONSHIP_PROCEDURE = "JobSupport_ProfileRelationships";

    private readonly INopDataProvider _dataProvider;
    private readonly ILogger _logger;
    private readonly JobSupportSettings _settings;

    public JobSupportPluginQueryService(INopDataProvider dataProvider, ILogger logger, JobSupportSettings settings)
    {
        _dataProvider = dataProvider;
        _logger = logger;
        _settings = settings;
    }

    public async Task<PagedProfileSearchResult> SearchProfilesAsync(ProfileSearchRequest request)
    {
        var guard = Validate(request);
        if (guard != ProfileQueryErrorCode.None)
            return Failed(request, guard);

        var procedureRequest = request;
        if (request.ProductIds?.Count > 0)
        {
            procedureRequest = CopyForIdentifierLookup(request);
        }

        var parameters = PluginProcedureParameterFactory.CreateProfileSearchParameters(procedureRequest,
            out var totalRecordsParameter);
        try
        {
            var cards = await _dataProvider.QueryProcAsync<ProfileCardResult>(PROFILE_SEARCH_PROCEDURE, parameters);
            int? identifierTotal = null;
            if (request.ProductIds?.Count > 0)
            {
                var identifiers = request.ProductIds.ToHashSet();
                var matchingCards = cards.Where(card => card.LegacyProductId.HasValue && identifiers.Contains(card.LegacyProductId.Value))
                    .ToList();
                identifierTotal = matchingCards.Count;
                cards = matchingCards.Skip(request.PageIndex * request.PageSize).Take(request.PageSize).ToList();
            }
            var total = identifierTotal.HasValue
                ? identifierTotal.Value
                : PluginProcedureParameterFactory.ReadTotalRecords(totalRecordsParameter).GetValueOrDefault();
            return Success(request, cards.Select(Map).ToList(), total);
        }
        catch (Exception exception)
        {
            await LogFailureAsync(PROFILE_SEARCH_PROCEDURE, parameters, exception);
            return Failed(request, ProfileQueryErrorCode.ProcedureExecutionFailed);
        }
    }

    public async Task<PagedProfileSearchResult> GetProfilesByRelationshipAsync(ProfileSearchRequest request)
    {
        var guard = Validate(request);
        if (guard != ProfileQueryErrorCode.None)
            return Failed(request, guard);
        if (!TryMapRelationship(request.RelationshipType, out var direction, out var type, out var status))
            return Failed(request, ProfileQueryErrorCode.NotSupported);

        var parameters = PluginProcedureParameterFactory.CreateRelationshipParameters(request,
            direction, type, status, out var totalRecordsParameter);
        try
        {
            var cards = await _dataProvider.QueryProcAsync<RelationshipQueryResult>(RELATIONSHIP_PROCEDURE, parameters);
            var total = PluginProcedureParameterFactory.ReadTotalRecords(totalRecordsParameter).GetValueOrDefault();
            return Success(request, cards.Select(Map).ToList(), total);
        }
        catch (Exception exception)
        {
            await LogFailureAsync(RELATIONSHIP_PROCEDURE, parameters, exception);
            return Failed(request, ProfileQueryErrorCode.ProcedureExecutionFailed);
        }
    }

    private ProfileQueryErrorCode Validate(ProfileSearchRequest request)
    {
        if (!_settings.Enabled)
            return ProfileQueryErrorCode.Disabled;
        if (_dataProvider is not MsSqlNopDataProvider)
            return ProfileQueryErrorCode.UnsupportedProvider;
        if (request == null || request.PageIndex < 0 || request.PageSize <= 0)
            return ProfileQueryErrorCode.InvalidRequest;
        return ProfileQueryErrorCode.None;
    }

    private static bool TryMapRelationship(RelationshipType? relationship,
        out int direction,
        out int? type,
        out int? status)
    {
        direction = 0;
        type = null;
        status = null;
        if (!relationship.HasValue)
            return false;

        (int Direction, int? Type, int? Status) mapping = relationship.Value switch
        {
            RelationshipType.ShortlistedByMe => (1, 1, (int)RelationshipStatus.Active),
            RelationshipType.ShortlistedMe => (2, 1, (int)RelationshipStatus.Active),
            RelationshipType.InterestSent => (1, 2, (int)RelationshipStatus.Pending),
            RelationshipType.InterestReceived => (2, 2, (int)RelationshipStatus.Pending),
            RelationshipType.AcceptedByMe => (2, 2, (int)RelationshipStatus.Accepted),
            RelationshipType.AcceptedMe => (1, 2, (int)RelationshipStatus.Accepted),
            RelationshipType.DeclinedByMe => (2, 2, (int)RelationshipStatus.Declined),
            RelationshipType.DeclinedMe => (1, 2, (int)RelationshipStatus.Declined),
            RelationshipType.BlockedByMe => (1, 3, (int)RelationshipStatus.Blocked),
            RelationshipType.BlockedMe => (2, 3, (int)RelationshipStatus.Blocked),
            RelationshipType.ViewedByMe => (1, 4, (int)RelationshipStatus.Active),
            RelationshipType.ViewedMe => (2, 4, (int)RelationshipStatus.Active),
            _ => (0, (int?)null, (int?)null)
        };
        (direction, type, status) = mapping;
        return direction != 0;
    }

    private static ProfileSearchResult Map(ProfileCardResult card) => new()
    {
        Id = card.LegacyProductId ?? card.ProfileId,
        VendorId = card.CustomerId,
        FirstName = card.DisplayName,
        CountryId = card.CountryId,
        StateProvinceId = card.StateProvinceId,
        City = card.City,
        AvatarPictureId = card.AvatarPictureId?.ToString(),
        CustomerProfileTypeId = card.ProfileType,
        PrimaryTechnology = card.PrimaryTechnology,
        SecondaryTechnology = card.SecondaryTechnology,
        CurrentAvailability = card.CurrentAvailability,
        ProfileType = card.ProfileType.ToString(),
        MotherTongue = card.MotherTongue,
        WorkExperience = card.RelevantExperience,
        Slug = card.Slug,
        LastLoginDateUtc = AsUtc(card.LastLoginDateUtc),
        LastActivityDateUtc = AsUtc(card.LastActivityDateUtc),
        ProfileShortListed = card.Requested,
        InterestSent = card.InterestStatus == (int)RelationshipStatus.Pending,
        PremiumCustomer = card.PremiumCustomer
    };

    private static PagedProfileSearchResult Success(ProfileSearchRequest request,
        IList<ProfileSearchResult> rows,
        int totalRecords) => new()
    {
        Items = rows,
        PageIndex = request.PageIndex,
        PageSize = request.PageSize,
        TotalRecords = totalRecords,
        OutputTotalRecords = totalRecords,
        ReturnedRowCount = rows.Count,
        Succeeded = true,
        ErrorCode = ProfileQueryErrorCode.None,
        Source = ProfileQuerySource.PluginProcedure
    };

    private static PagedProfileSearchResult Failed(ProfileSearchRequest request, ProfileQueryErrorCode errorCode) => new()
    {
        PageIndex = request?.PageIndex ?? 0,
        PageSize = request?.PageSize ?? 0,
        ErrorCode = errorCode,
        Source = ProfileQuerySource.None
    };

    private static DateTime? AsUtc(DateTime? value) => !value.HasValue || value.Value.Kind == DateTimeKind.Utc
        ? value
        : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private Task LogFailureAsync(string procedureName, IEnumerable<DataParameter> parameters, Exception exception) =>
        _logger.ErrorAsync(
            $"JobSupport plugin query failed for procedure {procedureName}; parameters: {string.Join(", ", parameters.Select(parameter => parameter.Name))}",
            exception);

    private static ProfileSearchRequest CopyForIdentifierLookup(ProfileSearchRequest request) => new()
    {
        CustomerId = request.CustomerId,
        StoreId = request.StoreId,
        ProfileTypeId = request.ProfileTypeId,
        PrimarySkillIds = request.PrimarySkillIds,
        SecondarySkillIds = request.SecondarySkillIds,
        Availability = request.Availability,
        Keywords = request.Keywords,
        ExcludeOwnProfile = request.ExcludeOwnProfile,
        SortOrder = request.SortOrder,
        PageIndex = 0,
        PageSize = int.MaxValue
    };
}
