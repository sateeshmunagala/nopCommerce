using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.JobSupport.Contracts;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial interface IJobSupportSubscriptionService
{
    Task ApplyPaidOrderAsync(Order order, JobSupportSettings settings);
    Task<SubscriptionSummary> GetSubscriptionAsync(int customerId, int storeId);
    Task<ContactRevealDecision> RevealContactAsync(int customerId, int targetProfileId, int storeId);
}
