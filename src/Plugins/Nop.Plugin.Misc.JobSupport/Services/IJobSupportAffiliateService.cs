using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.JobSupport.Contracts;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial interface IJobSupportAffiliateService
{
    Task EnsureAffiliateAsync(Customer customer, WorkflowExecutionMode mode);
}
