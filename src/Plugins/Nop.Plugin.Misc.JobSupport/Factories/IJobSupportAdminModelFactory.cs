using Nop.Plugin.Misc.JobSupport.Models.Admin;

namespace Nop.Plugin.Misc.JobSupport.Factories;

public interface IJobSupportAdminModelFactory
{
    ProfileSearchModel PrepareProfileSearchModel();
    Task<ProfileListModel> PrepareProfileListModelAsync(ProfileSearchModel searchModel);
    ConfigurationModel PrepareConfigurationModel();
    void ApplyConfigurationModel(ConfigurationModel model);
}
