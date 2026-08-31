using Nop.Plugin.Misc.JobSupport.Contracts;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial interface IJobSupportLegacyParityService
{
    ProfileComparisonResult Compare(PagedProfileSearchResult expected,
        PagedProfileSearchResult actual,
        bool ignoreFormatting = false);
}
