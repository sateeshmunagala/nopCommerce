using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Models.Admin;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial interface IJobSupportCutoverService
{
    Task RecordComparisonAsync(string queryName,
        PagedProfileSearchResult legacy,
        PagedProfileSearchResult plugin,
        long legacyDurationMilliseconds,
        long pluginDurationMilliseconds);

    Task<CutoverStatusModel> GetStatusAsync();
}
