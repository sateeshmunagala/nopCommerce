using Nop.Core.Domain.Customers;
using Nop.Services.Security;

namespace Nop.Plugin.Misc.JobSupport.Infrastructure;

public class JobSupportPermissionConfigManager : IPermissionConfigManager
{
    public const string ACCESS_JOB_SUPPORT = "JobSupport.AccessJobSupport";
    public const string MANAGE_PROFILES = "JobSupport.ManageProfiles";
    public const string MANAGE_RELATIONSHIPS = "JobSupport.ManageRelationships";
    public const string MANAGE_SUBSCRIPTIONS = "JobSupport.ManageSubscriptions";
    public const string VIEW_DIAGNOSTICS = "JobSupport.ViewDiagnostics";

    public IList<PermissionConfig> AllConfigs => new List<PermissionConfig>
    {
        new("JobSupport access", ACCESS_JOB_SUPPORT, nameof(StandardPermission.Configuration), NopCustomerDefaults.AdministratorsRoleName),
        new("JobSupport profiles", MANAGE_PROFILES, nameof(StandardPermission.Configuration), NopCustomerDefaults.AdministratorsRoleName),
        new("JobSupport relationships", MANAGE_RELATIONSHIPS, nameof(StandardPermission.Configuration), NopCustomerDefaults.AdministratorsRoleName),
        new("JobSupport subscriptions", MANAGE_SUBSCRIPTIONS, nameof(StandardPermission.Configuration), NopCustomerDefaults.AdministratorsRoleName),
        new("JobSupport diagnostics", VIEW_DIAGNOSTICS, nameof(StandardPermission.Configuration), NopCustomerDefaults.AdministratorsRoleName)
    };
}
