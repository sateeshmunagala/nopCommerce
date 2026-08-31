using Nop.Plugin.Misc.JobSupport.Contracts;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial interface IJobSupportProfileQueryService
{
    Task<PagedProfileSearchResult> SearchProfilesAsync(ProfileSearchRequest request);
    Task<PagedProfileSearchResult> GetProfilesByRelationshipAsync(ProfileSearchRequest request);
}
