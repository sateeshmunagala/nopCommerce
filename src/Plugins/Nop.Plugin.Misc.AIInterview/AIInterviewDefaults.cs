namespace Nop.Plugin.Misc.AIInterview;

/// <summary>
/// Represents plugin constants
/// </summary>
public static class AIInterviewDefaults
{
    /// <summary>
    /// Gets the plugin system name
    /// </summary>
    public static string SystemName => "Misc.AIInterview";

    /// <summary>
    /// Gets the configuration route name
    /// </summary>
    public static string ConfigurationRouteName => "Plugin.Misc.AIInterview.Configure";

    /// <summary>
    /// Gets the public index route name
    /// </summary>
    public static string IndexRouteName => "Plugin.Misc.AIInterview.Index";

    /// <summary>
    /// Gets the apply route name
    /// </summary>
    public static string ApplyRouteName => "Plugin.Misc.AIInterview.Apply";

    /// <summary>
    /// Gets the my applications route name
    /// </summary>
    public static string MyApplicationsRouteName => "Plugin.Misc.AIInterview.MyApplications";

    /// <summary>
    /// Gets the employer applications route name
    /// </summary>
    public static string EmployerApplicationsRouteName => "Plugin.Misc.AIInterview.EmployerApplications";

    /// <summary>
    /// Gets the report route name
    /// </summary>
    public static string ReportRouteName => "Plugin.Misc.AIInterview.Report";

    /// <summary>
    /// Gets the interview route name
    /// </summary>
    public static string InterviewRouteName => "Plugin.Misc.AIInterview.Interview";

    /// <summary>
    /// Mock routes
    /// </summary>
    public static string MockStartRouteName => "Plugin.Misc.AIInterview.Mock.Start";
    public static string MockRuntimeRouteName => "Plugin.Misc.AIInterview.Mock.Runtime";
    public static string MockHistoryRouteName => "Plugin.Misc.AIInterview.Mock.History";
    public static string MockReportRouteName => "Plugin.Misc.AIInterview.Mock.Report";
    public static string MockEmployerManageRouteName => "Plugin.Misc.AIInterview.Mock.EmployerManage";

    /// <summary>
    /// Admin Mock routes
    /// </summary>
    public static string AdminMockConfigureRouteName => "Plugin.Misc.AIInterview.Admin.Mock.Configure";
    public static string AdminMockReportRouteName => "Plugin.Misc.AIInterview.Admin.Mock.Report";

    /// <summary>
    /// Gets the prefix for locale resources
    /// </summary>
    public static string LocalizationPrefix => "Plugins.Misc.AIInterview";
}
