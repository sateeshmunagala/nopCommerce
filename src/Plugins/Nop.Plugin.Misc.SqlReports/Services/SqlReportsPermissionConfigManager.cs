using Nop.Core.Domain.Customers;
using Nop.Services.Security;

namespace Nop.Plugin.Misc.SqlReports.Services;

public class SqlReportsPermissionConfigManager : IPermissionConfigManager
{
    public IList<PermissionConfig> AllConfigs => new List<PermissionConfig>
    {
        new("Admin area. SQL Reports. Manage reports and parameters", SqlReportsDefaults.Permissions.ManageReports, nameof(StandardPermission.ContentManagement), NopCustomerDefaults.AdministratorsRoleName),
        new("Admin area. SQL Reports. Run reports", SqlReportsDefaults.Permissions.RunReports, nameof(StandardPermission.ContentManagement), NopCustomerDefaults.AdministratorsRoleName)
    };
}
