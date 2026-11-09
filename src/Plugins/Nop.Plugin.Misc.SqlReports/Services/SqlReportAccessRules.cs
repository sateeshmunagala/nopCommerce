namespace Nop.Plugin.Misc.SqlReports.Services;

public static class SqlReportAccessRules
{
    public static bool CanRunReport(bool isAdmin,
        bool isActive,
        IEnumerable<int> allowedRoleIds,
        IEnumerable<int> customerRoleIds)
    {
        if (isAdmin)
            return true;

        if (!isActive)
            return false;

        var allowed = allowedRoleIds?.ToHashSet() ?? new HashSet<int>();
        if (!allowed.Any())
            return false;

        return (customerRoleIds ?? Enumerable.Empty<int>()).Any(allowed.Contains);
    }
}
