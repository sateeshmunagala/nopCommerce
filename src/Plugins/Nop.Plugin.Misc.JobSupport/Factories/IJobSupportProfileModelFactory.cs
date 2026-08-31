using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Models.Public;

namespace Nop.Plugin.Misc.JobSupport.Factories;

public interface IJobSupportProfileModelFactory
{
    Task PrepareFilterAsync(ProfileFilterModel filter);
    Task<ProfileListModel> PrepareProfileListAsync(ProfileFilterModel filter,
        PagedProfileSearchResult result,
        Customer currentCustomer,
        bool isGuest);
    Task<ProfileDetailModel> PrepareProfileDetailAsync(Product profile,
        ProfileSearchResult result,
        Customer currentCustomer,
        bool isGuest,
        string returnUrl);
    Task<ProfileCardModel> PrepareProfileCardAsync(ProfileSearchResult result,
        Customer currentCustomer,
        bool isGuest);
}
