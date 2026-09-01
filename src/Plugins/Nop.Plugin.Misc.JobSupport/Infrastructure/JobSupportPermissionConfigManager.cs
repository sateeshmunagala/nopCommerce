using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Security;
using Nop.Services.Security;

namespace Nop.Plugin.Misc.JobSupport.Infrastructure;

public class JobSupportPermissionConfigManager : IPermissionProvider
{
    public const string ACCESS_JOB_SUPPORT = "JobSupport.AccessJobSupport";
    public const string MANAGE_PROFILES = "JobSupport.ManageProfiles";
    public const string MANAGE_RELATIONSHIPS = "JobSupport.ManageRelationships";
    public const string MANAGE_SUBSCRIPTIONS = "JobSupport.ManageSubscriptions";
    public const string VIEW_DIAGNOSTICS = "JobSupport.ViewDiagnostics";

    public static readonly PermissionRecord AccessJobSupport = Create("Access JobSupport", ACCESS_JOB_SUPPORT);
    public static readonly PermissionRecord ManageProfiles = Create("Manage JobSupport profiles", MANAGE_PROFILES);
    public static readonly PermissionRecord ManageRelationships = Create("Manage JobSupport relationships", MANAGE_RELATIONSHIPS);
    public static readonly PermissionRecord ManageSubscriptions = Create("Manage JobSupport subscriptions", MANAGE_SUBSCRIPTIONS);
    public static readonly PermissionRecord ViewDiagnostics = Create("View JobSupport diagnostics", VIEW_DIAGNOSTICS);

    public IEnumerable<PermissionRecord> GetPermissions() =>
        new[] { AccessJobSupport, ManageProfiles, ManageRelationships, ManageSubscriptions, ViewDiagnostics };

    public HashSet<(string systemRoleName, PermissionRecord[] permissions)> GetDefaultPermissions() =>
        new()
        {
            (NopCustomerDefaults.AdministratorsRoleName, GetPermissions().ToArray())
        };

    private static PermissionRecord Create(string name, string systemName) =>
        new() { Name = name, SystemName = systemName, Category = "JobSupport" };
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class CheckPermissionAttribute : TypeFilterAttribute
{
    public CheckPermissionAttribute(string permissionSystemName) : base(typeof(CheckPermissionFilter))
    {
        Arguments = new object[] { permissionSystemName };
    }

    private sealed class CheckPermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly string _permissionSystemName;
        private readonly IPermissionService _permissionService;

        public CheckPermissionFilter(string permissionSystemName, IPermissionService permissionService)
        {
            _permissionSystemName = permissionSystemName;
            _permissionService = permissionService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (!await _permissionService.AuthorizeAsync(_permissionSystemName))
                context.Result = new ForbidResult();
        }
    }
}
