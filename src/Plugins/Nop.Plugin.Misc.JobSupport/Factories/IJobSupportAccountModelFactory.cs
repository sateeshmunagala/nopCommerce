using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Models.Account;

namespace Nop.Plugin.Misc.JobSupport.Factories;

public interface IJobSupportAccountModelFactory
{
    Task<ProfileEditModel> PrepareProfileEditAsync(Customer customer, ProfileEditModel model = null);
    Task SaveProfileAsync(Customer customer, ProfileEditModel model);
    Task<RelationshipListModel> PrepareRelationshipsAsync(Customer customer, RelationshipType relationshipType);
    Task<SubscriptionModel> PrepareSubscriptionAsync(Customer customer, int storeId);
    Task<AffiliationModel> PrepareAffiliationsAsync(Customer customer);
}
