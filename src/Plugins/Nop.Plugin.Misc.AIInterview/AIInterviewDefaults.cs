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

    public static string AdminFeedbackReportsRouteName => "Plugin.Misc.AIInterview.Admin.FeedbackReports";

    public static string AdminFeedbackReportsListRouteName => "Plugin.Misc.AIInterview.Admin.FeedbackReports.List";

    public static string AdminMenuSystemName => "AIInterview";
    public static string AdminConfigureMenuSystemName => "AIInterview.Configure";
    public static string AdminAiServiceMenuSystemName => "AIInterview.AiService";
    public static string AdminSponsorInvitesMenuSystemName => "AIInterview.SponsorInvites";
    public static string AdminVendorCreditsMenuSystemName => "AIInterview.VendorCredits";
    public static string AdminApplicantCreditsMenuSystemName => "AIInterview.ApplicantCredits";
    public static string AdminScoreboardMenuSystemName => "AIInterview.Scoreboard";
    public static string AdminMockPracticeSessionsMenuSystemName => "AIInterview.MockPracticeSessions";
    public static string AdminFeedbackReportsMenuSystemName => "AIInterview.FeedbackReports";

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

    public static string HomepageTopPerformersWidgetZone => Nop.Web.Framework.Infrastructure.PublicWidgetZones.HomepageBeforeBestSellers;

    public const int HomepageTopPerformersMaxCount = 10;

    public const int HomepageTopPerformersFreshnessDays = 30;

    public static string DefaultAvatarImageUrl => "~/Plugins/Misc.AIInterview/Content/images/default-avatar.svg";

    public static string HomepageTopPerformersTitleResourceKey => $"{LocalizationPrefix}.Homepage.TopPerformers.Title";

    public static string HomepageTopPerformersScoreResourceKey => $"{LocalizationPrefix}.Homepage.TopPerformers.Score";

    public static string HomepageTopPerformersFallbackSkillResourceKey => $"{LocalizationPrefix}.Homepage.TopPerformers.SkillFallback";

    public static string HomepageTopPerformersAvatarAltResourceKey => $"{LocalizationPrefix}.Homepage.TopPerformers.AvatarAlt";

    public static string HomepageTopPerformersPreviousResourceKey => $"{LocalizationPrefix}.Homepage.TopPerformers.Previous";

    public static string HomepageTopPerformersNextResourceKey => $"{LocalizationPrefix}.Homepage.TopPerformers.Next";

    public static string HomepageTopPerformersEmptyResourceKey => $"{LocalizationPrefix}.Homepage.TopPerformers.Empty";

    public static string HomepageTopPerformersUnknownCandidateResourceKey => $"{LocalizationPrefix}.Homepage.TopPerformers.UnknownCandidate";

    /// <summary>
    /// Gets the employer applications route name
    /// </summary>
    public static string EmployerApplicationsRouteName => "Plugin.Misc.AIInterview.EmployerApplications";
    public static string EmployerDownloadResumeRouteName => "Plugin.Misc.AIInterview.EmployerDownloadResume";
    public static string EmployerDashboardRouteName => "Plugin.Misc.AIInterview.EmployerDashboard";
    public static string InstituteDashboardRouteName => "Plugin.Misc.AIInterview.InstituteDashboard";
    public static string InstituteCandidatesRouteName => "Plugin.Misc.AIInterview.InstituteCandidates";
    public static string InstituteCreditsRouteName => "Plugin.Misc.AIInterview.InstituteCredits";
    public static string InstituteVendorIdAttributeKey => "AIInterview.InstituteVendorId";
    public static string InstituteRegistrationCookieName => "ai_inst";
    public static string InstituteCreditAllotRouteName => "Plugin.Misc.AIInterview.InstituteCreditAllot";

    public static string VendorScoreboardRouteName => "Plugin.Misc.AIInterview.VendorScoreboard";

    public static string VendorJobCreationRouteName => "Plugin.Misc.AIInterview.VendorJobCreation";
    public static string VendorJobEditRouteName => "Plugin.Misc.AIInterview.VendorJobEdit";
    public static string VendorJobPublishToggleRouteName => "Plugin.Misc.AIInterview.VendorJobPublishToggle";

    public static string ApplyInlineRouteName => "Plugin.Misc.AIInterview.ApplyInline";

    public static string JobProductTemplateName => "AI Interview Job Details";

    public static string JobProductTemplateViewPath => "~/Plugins/Misc.AIInterview/Views/ProductTemplate.JobDetails.cshtml";

    public static string MockPracticeProductTemplateName => "AI Interview Practice";

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
    public static string DefaultAzureDocumentIntelligenceModelId => "prebuilt-read";
    public const int DefaultAzureDocumentIntelligenceTimeoutSeconds = 60;
    public static string DefaultSupportPhoneNumber => "+91 72073 33883";
    public const int DefaultStrengthsSummaryMaxCompletionTokens = 1500;
    public const int MinStrengthsSummaryMaxCompletionTokens = 500;
    public const int MaxStrengthsSummaryMaxCompletionTokens = 3000;
    public const int DefaultRecordingUploadMaxMb = 100;
    public const int MinRecordingUploadMaxMb = 80;
    public const int MaxRecordingUploadMaxMb = 250;
    public const int DefaultRecordingVideoBitsPerSecond = 650000;
    public const int MinRecordingVideoBitsPerSecond = 350000;
    public const int MaxRecordingVideoBitsPerSecond = 1200000;
    public const string CompletionRecoveryTaskName = "AI Interview completion recovery";
    public const string CompletionRecoveryTaskType = "Nop.Plugin.Misc.AIInterview.Services.InterviewCompletionRecoveryTask";
    public const string LegacyCompletionRecoveryTaskType = "Nop.Plugin.Misc.AIInterview.Services.InterviewCompletionRecoveryTask, Nop.Plugin.Misc.AIInterview";
    public const int CompletionRecoveryTaskPeriodSeconds = 30;
    public const int CompletionMaxAttempts = 3;
    public const int CompletionFirstRetryDelayMinutes = 1;
    public const int CompletionSecondRetryDelayMinutes = 5;
    public const int CompletionFallbackRetryDelayMinutes = 15;
    public const int DefaultRecordingAudioBitsPerSecond = 64000;
    public const int MinRecordingAudioBitsPerSecond = 32000;
    public const int MaxRecordingAudioBitsPerSecond = 128000;
    public const string DefaultRecordingSourceMode = "ScreenPreferred";
    public const int DefaultRecordingUploadTimeoutMs = 15000;
    public const int MinRecordingUploadTimeoutMs = 5000;
    public const int MaxRecordingUploadTimeoutMs = 60000;
    public const int DefaultFinalizationWaitTimeoutMs = 10000;
    public const int MinFinalizationWaitTimeoutMs = 5000;
    public const int MaxFinalizationWaitTimeoutMs = 45000;
    public static string DefaultResumeProfileExtractionSystemPrompt => "Return JSON only. Extract only facts supported by the resume text. Do not invent companies, projects, dates, skills, tools, metrics, or responsibilities. If project names are unclear, use a short descriptive label based on the resume text. If no projects are present, return an empty projects array. Keep each string concise.";
    public static string DefaultQuestionPlanSystemPrompt => "Return JSON only. Return exactly the requested number of questions. Ask one clear question per item. Do not include answers. Do not ask duplicate questions. Do not invent resume facts. Use resumeEvidence only for facts present in the resume profile. Project-scenario questions must be tied to a real project or responsibility from the resume profile when available. Skill questions must prioritize resume profile primary skills and job-required skills. Keep questions concise enough to read aloud in an interview runtime.";
    public static string DefaultQuestionPlanBuilderInstructionBlock => """
Sequence 1 is reserved by the runtime for the candidate introduction and project-experience question.
Generate exactly the requested remaining questions for this call; do not duplicate the introduction/project-experience question.
Use remaining sequence numbers only. If sequence 1 already exists, begin generated questions at sequence 2 and continue from there.
Remaining questions should build on resume and job context, cover role-relevant technical depth, feel natural and conversational, and ask one clear question at a time.
Allowed categories: skill, project_scenario, job_fit, behavioral
""";
    public static string DefaultRuntimeQuestionGenerationSystemPrompt => "Return JSON only. Question mode contract: question, complete:false, optional rubricJson. No markdown. No prose outside JSON.";
    public static string DefaultRuntimeScoringSystemPrompt => "Return JSON only. Scoring mode contract: technicalScore, communicationScore, professionalismScore, positiveAttitudeScore, score, feedback, complete, optional nextQuestion, completion, optional answerQuality, optional nonSubstantiveReason, rubricJson. No markdown. No prose outside JSON. All numeric scores must be integers or decimals from 0 to 100. score must be present and must be the average of the four category scores. feedback must be present. technicalScore, communicationScore, professionalismScore, and positiveAttitudeScore must all be present. rubricJson should be a JSON object that repeats the category scores and score. Distinguish answerQuality as non_substantive, weak, or substantive. Reserve score 0 and answerQuality non_substantive only for empty, copied, refusal, AI-persona, or unrelated answers. If the answer attempts the question but is generic, vague, or lacks evidence, classify it as weak and assign low but non-zero scores with concrete feedback.";
    public static string DefaultRuntimeScoringRetryAddendumPrompt => "Guardrail: if the answer attempts the question but is weak or generic, do not classify it as non_substantive and do not score it as 0. Use answerQuality weak with low but non-zero scores for attempted answers. Reserve answerQuality non_substantive and score 0 for empty, copied, refusal, AI-persona, or unrelated answers only.";
    public static string DefaultFinalScoringSystemPrompt => "Return JSON only. Final scoring mode contract: turns array with sequenceNumber, technicalScore, communicationScore, professionalismScore, positiveAttitudeScore, score, feedback, optional answerQuality, optional nonSubstantiveReason, optional rubricJson; plus overallScore and completion. Score every supplied answered turn exactly once. Do not add, remove, or renumber turns. All scores must be numeric 0-100. score must be the average of the four category scores. Reserve score 0 only for empty, copied, refusal, AI-persona, or unrelated answers.";
    public static string DefaultStrengthsSummarySystemPrompt => "Return JSON only. Strengths summary mode contract: strengthsText string, optional confidence string, optional evidenceTurnNumbers integer array. strengthsText must be 200 to 300 characters, plain text, no markdown, no bullets, and grounded only in the submitted answered turns. Write one concise evidence-based strengths paragraph. Reflect the actual submitted answers and scored feedback. Avoid generic boilerplate.";
    public static string DefaultStrengthsSummaryRetryStrictJsonSystemPrompt => "Return JSON only. Strengths summary mode contract: strengthsText string, optional confidence string, optional evidenceTurnNumbers integer array. strengthsText must be 200 to 300 characters, plain text, no markdown, no bullets, and grounded only in the submitted answered turns. Write one concise evidence-based strengths paragraph. Reflect the actual submitted answers and scored feedback. Avoid generic boilerplate. Strict JSON-first retry: start the response with { and output only one complete JSON object. No markdown fences, preface, trailing prose, or partial JSON.";

    public static string InterviewDifficultyAttributeName => "Interview Difficulty";

    public static IReadOnlyList<string> InterviewDifficultyValues => new[] { "Easy", "Medium", "Hard" };

    public const string DefaultInterviewDifficulty = "Medium";

    /// <summary>
    /// Gets the report route name
    /// </summary>
    public static string ReportRouteName => "Plugin.Misc.AIInterview.Report";

    public static string ReportPanelRouteName => "Plugin.Misc.AIInterview.Report.Panel";

    public static string ReportShareRouteName => "Plugin.Misc.AIInterview.Report.Share";

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
    public static string MockPrepareRouteName => "Plugin.Misc.AIInterview.Mock.Prepare";
    public static string MockSubmitAnswerRouteName => "Plugin.Misc.AIInterview.Mock.SubmitAnswer";
    public static string MockStopRouteName => "Plugin.Misc.AIInterview.Mock.Stop";
    public static string MockFeedbackRouteName => "Plugin.Misc.AIInterview.Mock.Feedback";
    public static string MockRefreshTokenRouteName => "Plugin.Misc.AIInterview.Mock.RefreshToken";
    public static string MockSpeechTokenRouteName => "Plugin.Misc.AIInterview.Mock.SpeechToken";
    public static string MockSpeechUsageRouteName => "Plugin.Misc.AIInterview.Mock.SpeechUsage";
    public static string MockRecordingUploadRouteName => "Plugin.Misc.AIInterview.Mock.RecordingUpload";
    public static string MockAcknowledgeGuidelinesRouteName => "Plugin.Misc.AIInterview.Mock.AcknowledgeGuidelines";
    public static string MockRuntimeClientEventRouteName => "Plugin.Misc.AIInterview.Mock.RuntimeClientEvent";
    public static string MockHistoryRouteName => "Plugin.Misc.AIInterview.Mock.History";
    public static string MockReportRouteName => "Plugin.Misc.AIInterview.Mock.Report";
    public static string MockEmployerManageRouteName => "Plugin.Misc.AIInterview.Mock.EmployerManage";

    public const int RuntimeTokenLifetimeMinutes = 120;

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
    public static string MyActivityCreditsTabKey => "credits";
    public static string EmployerDashboardOverviewTabKey => "overview";
    public static string EmployerDashboardJobsTabKey => "jobs";
    public static string EmployerDashboardApplicationsTabKey => "applications";
    public static string EmployerDashboardInvitesTabKey => "invites";
    public static string InstituteDashboardTabKey => "dashboard";
    public static string InstituteCreditsTabKey => "credits";
    public static string InstituteCandidatesTabKey => "candidates";

    public const int MyActivityNavigationTab = 160;
    public const int MyApplicationsNavigationTab = 160;
    public const int MockHistoryNavigationTab = 165;
    public const int EmployerDashboardNavigationTab = 170;
    public const int InstituteDashboardNavigationTab = 50;
    public const int InstituteCandidatesNavigationTab = 51;
    public const int InstituteCreditsNavigationTab = 52;
    public const int VendorScoreboardNavigationTab = EmployerDashboardNavigationTab;
    public const int VendorJobCreationNavigationTab = EmployerDashboardNavigationTab;
    public const int EmployerApplicationsNavigationTab = EmployerDashboardNavigationTab;
    public const int SponsorInvitesNavigationTab = EmployerDashboardNavigationTab;
}
