using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial interface IJobSupportNotificationService
{
    Task<bool> QueueProfileAvailableNotificationAsync(Product profile, Customer recipient);
}
