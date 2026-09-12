using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Security;
using Nop.Services.Security;

namespace Nop.Plugin.Widgets.AISearch.Services;

public class AISearchPermissionConfigManager : IPermissionConfigManager
{
    public const string ADMIN_ACCESS_AISEARCH = "Widgets.AISearch.Admin.Access";

    public IList<PermissionConfig> AllConfigs => new List<PermissionConfig>
    {
        new("Admin area. Manage AI product search", ADMIN_ACCESS_AISEARCH,
            nameof(StandardPermission.Configuration), NopCustomerDefaults.AdministratorsRoleName)
    };
}
