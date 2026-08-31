using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.JobSupport.Contracts;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial interface IJobSupportProfileService
{
    Task EnsureProfileForCustomerAsync(Customer customer, JobSupportSettings settings);
    Task ActivateProfileAsync(Customer customer, JobSupportSettings settings);
    Task<RelationshipActionResult> UpdateAvailabilityAsync(int customerId, string availability, WorkflowExecutionMode mode);
    Task SynchronizeAvatarAsync(GenericAttribute attribute, WorkflowExecutionMode mode);
}
