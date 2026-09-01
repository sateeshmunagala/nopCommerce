using Nop.Plugin.Misc.JobSupport.Models.Admin;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial interface IJobSupportCutoverService
{
    Task<CutoverStatusModel> GetStatusAsync();
}
