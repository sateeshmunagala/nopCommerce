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

    public static string AdminAiServiceRouteName => "Plugin.Misc.AIInterview.Admin.AiService";

    public static string AdminSponsorInvitesRouteName => "Plugin.Misc.AIInterview.Admin.SponsorInvites";

    public static string AdminVendorCreditsRouteName => "Plugin.Misc.AIInterview.Admin.VendorCredits";

    public static string AdminApplicantCreditsRouteName => "Plugin.Misc.AIInterview.Admin.ApplicantCredits";

    public static string AdminApplicantCreditsListRouteName => "Plugin.Misc.AIInterview.Admin.ApplicantCredits.List";

    public static string AdminScoreboardRouteName => "Plugin.Misc.AIInterview.Admin.Scoreboard";

    public static string AdminScoreboardExportRouteName => "Plugin.Misc.AIInterview.Admin.Scoreboard.Export";

    public static string AdminMockPracticeSessionsRouteName => "Plugin.Misc.AIInterview.Admin.MockPracticeSessions";

    public static string AdminMockPracticeSessionsListRouteName => "Plugin.Misc.AIInterview.Admin.MockPracticeSessions.List";

    public static string AdminMenuSystemName => "AIInterview";
    public static string AdminConfigureMenuSystemName => "AIInterview.Configure";
    public static string AdminAiServiceMenuSystemName => "AIInterview.AiService";
    public static string AdminSponsorInvitesMenuSystemName => "AIInterview.SponsorInvites";
    public static string AdminVendorCreditsMenuSystemName => "AIInterview.VendorCredits";
    public static string AdminApplicantCreditsMenuSystemName => "AIInterview.ApplicantCredits";
    public static string AdminScoreboardMenuSystemName => "AIInterview.Scoreboard";
    public static string AdminMockPracticeSessionsMenuSystemName => "AIInterview.MockPracticeSessions";

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

    public static string MyActivityRouteName => "Plugin.Misc.AIInterview.MyActivity";

    /// <summary>
    /// Gets the employer applications route name
    /// </summary>
    public static string EmployerApplicationsRouteName => "Plugin.Misc.AIInterview.EmployerApplications";
    public static string EmployerDownloadResumeRouteName => "Plugin.Misc.AIInterview.EmployerDownloadResume";
    public static string EmployerDashboardRouteName => "Plugin.Misc.AIInterview.EmployerDashboard";

    public static string VendorScoreboardRouteName => "Plugin.Misc.AIInterview.VendorScoreboard";

    public static string VendorJobCreationRouteName => "Plugin.Misc.AIInterview.VendorJobCreation";
    public static string VendorJobEditRouteName => "Plugin.Misc.AIInterview.VendorJobEdit";
    public static string VendorJobPublishToggleRouteName => "Plugin.Misc.AIInterview.VendorJobPublishToggle";

    public static string ApplyInlineRouteName => "Plugin.Misc.AIInterview.ApplyInline";

    public static string JobProductTemplateName => "AI Interview Job Details";

    public static string JobProductTemplateViewPath => "~/Plugins/Misc.AIInterview/Views/ProductTemplate.JobDetails.cshtml";

    public static string MockPracticeProductTemplateName => "AI Interview Mock Practice";

    public static string MockPracticeProductTemplateViewPath => "~/Plugins/Misc.AIInterview/Views/ProductTemplate.MockPractice.cshtml";

    public static string InterviewTypeJob => "Job";

    public static string InterviewTypeMockPractice => "MockPractice";

    public static string PricingCategoryTemplateName => "Pricing Category";

    public static string PricingCategoryTemplateViewPath => "CategoryTemplate.Pricing";

    public static string JobResumeRequiredAttributeName => "AIInterview.Job.ResumeRequired";
    public static string JobInterviewRequiredAttributeName => "AIInterview.Job.InterviewRequired";
    public static string JobMinimumScoreAttributeName => "AIInterview.Job.MinimumScore";
    public static string JobQuestionCountAttributeName => "AIInterview.Job.QuestionCount";
    public static string JobSalaryMinCtcPaAttributeName => "AIInterview.Job.SalaryMinCtcPa";
    public static string JobSalaryMaxCtcPaAttributeName => "AIInterview.Job.SalaryMaxCtcPa";
    public static string JobSalaryCurrencyCodeAttributeName => "AIInterview.Job.SalaryCurrencyCode";
    public static string JobSalaryPeriodAttributeName => "AIInterview.Job.SalaryPeriod";

    public static string DefaultCreditProductSkuMappingsJson => "{\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10,\"AI-CREDIT-20\":20}";
    public static string DefaultCreditPurchasePageUrl => "/pricing";

    public static string InterviewDifficultyAttributeName => "Interview Difficulty";

    public static IReadOnlyList<string> InterviewDifficultyValues => new[] { "Easy", "Medium", "Hard" };

    public const string DefaultInterviewDifficulty = "Medium";

    /// <summary>
    /// Gets the report route name
    /// </summary>
    public static string ReportRouteName => "Plugin.Misc.AIInterview.Report";

    public static string ReportPanelRouteName => "Plugin.Misc.AIInterview.Report.Panel";

    public static string RecordingRouteName => "Plugin.Misc.AIInterview.Recording";

    public static string RecordingShareRouteName => "Plugin.Misc.AIInterview.Recording.Share";

    /// <summary>
    /// Gets the interview route name
    /// </summary>
    public static string InterviewRouteName => "Plugin.Misc.AIInterview.Interview";

    /// <summary>
    /// Mock routes
    /// </summary>
    public static string MockStartRouteName => "Plugin.Misc.AIInterview.Mock.Start";
    public static string MockRuntimeRouteName => "Plugin.Misc.AIInterview.Mock.Runtime";
    public static string MockBeginRouteName => "Plugin.Misc.AIInterview.Mock.Begin";
    public static string MockSubmitAnswerRouteName => "Plugin.Misc.AIInterview.Mock.SubmitAnswer";
    public static string MockStopRouteName => "Plugin.Misc.AIInterview.Mock.Stop";
    public static string MockRefreshTokenRouteName => "Plugin.Misc.AIInterview.Mock.RefreshToken";
    public static string MockSpeechTokenRouteName => "Plugin.Misc.AIInterview.Mock.SpeechToken";
    public static string MockSpeechUsageRouteName => "Plugin.Misc.AIInterview.Mock.SpeechUsage";
    public static string MockRecordingUploadRouteName => "Plugin.Misc.AIInterview.Mock.RecordingUpload";
    public static string MockAcknowledgeGuidelinesRouteName => "Plugin.Misc.AIInterview.Mock.AcknowledgeGuidelines";
    public static string MockHistoryRouteName => "Plugin.Misc.AIInterview.Mock.History";
    public static string MockReportRouteName => "Plugin.Misc.AIInterview.Mock.Report";
    public static string MockEmployerManageRouteName => "Plugin.Misc.AIInterview.Mock.EmployerManage";

    /// <summary>
    /// Admin Mock routes
    /// </summary>
    public static string AdminMockConfigureRouteName => "Plugin.Misc.AIInterview.Admin.Mock.Configure";

    /// <summary>
    /// Gets the prefix for locale resources
    /// </summary>
    public static string LocalizationPrefix => "Plugins.Misc.AIInterview";

    public static string MyActivityAppliedJobsTabKey => "applied-jobs";
    public static string MyActivitySavedJobsTabKey => "saved-jobs";
    public static string MyActivityMockInterviewsTabKey => "mock-interviews";
    public static string EmployerDashboardOverviewTabKey => "overview";
    public static string EmployerDashboardJobsTabKey => "jobs";
    public static string EmployerDashboardApplicationsTabKey => "applications";
    public static string EmployerDashboardInvitesTabKey => "invites";

    public const int MyActivityNavigationTab = 160;
    public const int MyApplicationsNavigationTab = 160;
    public const int MockHistoryNavigationTab = 165;
    public const int EmployerDashboardNavigationTab = 170;
    public const int VendorScoreboardNavigationTab = EmployerDashboardNavigationTab;
    public const int VendorJobCreationNavigationTab = EmployerDashboardNavigationTab;
    public const int EmployerApplicationsNavigationTab = EmployerDashboardNavigationTab;
    public const int SponsorInvitesNavigationTab = EmployerDashboardNavigationTab;
}
