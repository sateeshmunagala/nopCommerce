using Nop.Plugin.Misc.SqlReports.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.SqlReports.Tests;

[TestFixture]
public class SqlReportAccessRulesTests
{
    [Test]
    public void CanRunReport_Allows_Admin_Even_WhenInactiveOrUnassigned()
    {
        var allowed = SqlReportAccessRules.CanRunReport(
            isAdmin: true,
            isActive: false,
            allowedRoleIds: Array.Empty<int>(),
            customerRoleIds: Array.Empty<int>());

        Assert.That(allowed, Is.True);
    }

    [Test]
    public void CanRunReport_Denies_Vendor_WhenReportInactive()
    {
        var allowed = SqlReportAccessRules.CanRunReport(
            isAdmin: false,
            isActive: false,
            allowedRoleIds: new[] { 3 },
            customerRoleIds: new[] { 3 });

        Assert.That(allowed, Is.False);
    }

    [Test]
    public void CanRunReport_Denies_Vendor_WhenNoAclRolesAssigned()
    {
        var allowed = SqlReportAccessRules.CanRunReport(
            isAdmin: false,
            isActive: true,
            allowedRoleIds: Array.Empty<int>(),
            customerRoleIds: new[] { 3 });

        Assert.That(allowed, Is.False);
    }

    [Test]
    public void CanRunReport_Allows_Vendor_WhenAnyRoleMatches()
    {
        var allowed = SqlReportAccessRules.CanRunReport(
            isAdmin: false,
            isActive: true,
            allowedRoleIds: new[] { 2, 5 },
            customerRoleIds: new[] { 5, 7 });

        Assert.That(allowed, Is.True);
    }

    [Test]
    public void CanRunReport_Denies_Vendor_WhenAclDoesNotMatch()
    {
        var allowed = SqlReportAccessRules.CanRunReport(
            isAdmin: false,
            isActive: true,
            allowedRoleIds: new[] { 2, 5 },
            customerRoleIds: new[] { 3, 7 });

        Assert.That(allowed, Is.False);
    }
}
