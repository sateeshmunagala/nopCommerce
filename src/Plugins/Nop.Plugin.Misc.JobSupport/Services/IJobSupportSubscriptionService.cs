using Nop.Core.Domain.Orders;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial interface IJobSupportSubscriptionService
{
    Task ApplyPaidOrderAsync(Order order, JobSupportSettings settings);
}
