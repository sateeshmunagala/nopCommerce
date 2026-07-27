using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Plugin.Misc.AIInterview;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.AIInterview.Models;

public record JobRequirementsModel : BaseNopModel
{
    public int ProductId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.ProductRequirements.ResumeRequired")]
    public bool ResumeRequired { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.ProductRequirements.InterviewRequired")]
    public bool InterviewRequired { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.ProductRequirements.MinimumScore")]
    public decimal MinimumScore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.ProductRequirements.QuestionCount")]
    public int QuestionCount { get; set; } = 3;

    public bool IsJobProduct { get; set; }
}

public record AiServiceSettingsModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.AIInterview.UseMockResponses")]
    public bool UseMockResponses { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Provider")]
    public string Provider { get; set; }

    public IList<SelectListItem> AvailableProviders { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.ApiKey")]
    public string ApiKey { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Model")]
    public string Model { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Prompt")]
    public string Prompt { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.ResumeProfileExtractionSystemPrompt")]
    public string ResumeProfileExtractionSystemPrompt { get; set; } = AIInterviewDefaults.DefaultResumeProfileExtractionSystemPrompt;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanSystemPrompt")]
    public string QuestionPlanSystemPrompt { get; set; } = AIInterviewDefaults.DefaultQuestionPlanSystemPrompt;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.QuestionPlanBuilderInstructionBlock")]
    public string QuestionPlanBuilderInstructionBlock { get; set; } = AIInterviewDefaults.DefaultQuestionPlanBuilderInstructionBlock;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.RuntimeQuestionGenerationSystemPrompt")]
    public string RuntimeQuestionGenerationSystemPrompt { get; set; } = AIInterviewDefaults.DefaultRuntimeQuestionGenerationSystemPrompt;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.RuntimeScoringSystemPrompt")]
    public string RuntimeScoringSystemPrompt { get; set; } = AIInterviewDefaults.DefaultRuntimeScoringSystemPrompt;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.RuntimeScoringRetryAddendumPrompt")]
    public string RuntimeScoringRetryAddendumPrompt { get; set; } = AIInterviewDefaults.DefaultRuntimeScoringRetryAddendumPrompt;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.FinalScoringSystemPrompt")]
    public string FinalScoringSystemPrompt { get; set; } = AIInterviewDefaults.DefaultFinalScoringSystemPrompt;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummarySystemPrompt")]
    public string StrengthsSummarySystemPrompt { get; set; } = AIInterviewDefaults.DefaultStrengthsSummarySystemPrompt;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummaryRetryStrictJsonSystemPrompt")]
    public string StrengthsSummaryRetryStrictJsonSystemPrompt { get; set; } = AIInterviewDefaults.DefaultStrengthsSummaryRetryStrictJsonSystemPrompt;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.ServiceSettings")]
    public string ServiceSettings { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.CreditProductSkuMappingsJson")]
    public string CreditProductSkuMappingsJson { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.CreditPurchasePageUrl")]
    public string CreditPurchasePageUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.SupportPhoneNumber")]
    public string SupportPhoneNumber { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiEndpointUrl")]
    public string AzureOpenAiEndpointUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiApiKey")]
    public string AzureOpenAiApiKey { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiDeploymentOrModel")]
    public string AzureOpenAiDeploymentOrModel { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.StrengthsSummaryMaxCompletionTokens")]
    public int StrengthsSummaryMaxCompletionTokens { get; set; } = AIInterviewDefaults.DefaultStrengthsSummaryMaxCompletionTokens;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechKey")]
    public string AzureSpeechKey { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechRegion")]
    public string AzureSpeechRegion { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceEndpointUrl")]
    public string AzureDocumentIntelligenceEndpointUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceApiKey")]
    public string AzureDocumentIntelligenceApiKey { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceModelId")]
    public string AzureDocumentIntelligenceModelId { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureDocumentIntelligenceTimeoutSeconds")]
    public int AzureDocumentIntelligenceTimeoutSeconds { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.TrackAzureOpenAiUsage")]
    public bool TrackAzureOpenAiUsage { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.TrackAzureSpeechUsage")]
    public bool TrackAzureSpeechUsage { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.CalculateAzureCostPerInterview")]
    public bool CalculateAzureCostPerInterview { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiPromptTokenPricePerThousand")]
    public decimal AzureOpenAiPromptTokenPricePerThousand { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureOpenAiCompletionTokenPricePerThousand")]
    public decimal AzureOpenAiCompletionTokenPricePerThousand { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechRecognitionPricePerHour")]
    public decimal AzureSpeechRecognitionPricePerHour { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureSpeechSynthesisPricePerThousandCharacters")]
    public decimal AzureSpeechSynthesisPricePerThousandCharacters { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureUsageCurrencyCode")]
    public string AzureUsageCurrencyCode { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageContainerUrl")]
    public string AzureBlobStorageContainerUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.AzureBlobStorageSasToken")]
    public string AzureBlobStorageSasToken { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadMaxMb")]
    public int RecordingUploadMaxMb { get; set; } = AIInterviewDefaults.DefaultRecordingUploadMaxMb;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.RecordingVideoBitsPerSecond")]
    public int RecordingVideoBitsPerSecond { get; set; } = AIInterviewDefaults.DefaultRecordingVideoBitsPerSecond;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.RecordingAudioBitsPerSecond")]
    public int RecordingAudioBitsPerSecond { get; set; } = AIInterviewDefaults.DefaultRecordingAudioBitsPerSecond;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.RecordingSourceMode")]
    public string RecordingSourceMode { get; set; } = AIInterviewDefaults.DefaultRecordingSourceMode;

    public IList<SelectListItem> AvailableRecordingSourceModes { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.RecordingUploadTimeoutMs")]
    public int RecordingUploadTimeoutMs { get; set; } = AIInterviewDefaults.DefaultRecordingUploadTimeoutMs;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.AiService.FinalizationWaitTimeoutMs")]
    public int FinalizationWaitTimeoutMs { get; set; } = AIInterviewDefaults.DefaultFinalizationWaitTimeoutMs;
}

public record SponsorInviteAdminModel : BaseNopModel
{
    public SponsorInviteAdminModel()
    {
        Invites = new List<SponsorInviteRowModel>();
    }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.SponsorInvites.BulkEmails")]
    public string BulkEmails { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.SponsorInvites.ProductId")]
    public int ProductId { get; set; }

    public IList<SelectListItem> AvailableProducts { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.SponsorInvites.MaxAttempts")]
    public int MaxAttempts { get; set; } = 1;

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.SponsorInvites.ExpiryDateUtc")]
    public DateTime? ExpiryDateUtc { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.SponsorInvites.SponsorId")]
    public int? SponsorId { get; set; }

    public IList<SelectListItem> AvailableSponsors { get; set; } = new List<SelectListItem>();

    public string Message { get; set; }

    public IList<SponsorInviteRowModel> Invites { get; set; }
}

public record SponsorInviteRowModel : BaseNopModel
{
    public int Id { get; set; }
    public int SponsorId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string ProductAdminUrl { get; set; }
    public string VendorName { get; set; }
    public string VendorAdminUrl { get; set; }
    public string Email { get; set; }
    public string InviteCode { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime? ExpiryDateUtc { get; set; }
    public string ExpiryDate { get; set; }
    public bool IsActive { get; set; }
    public bool IsAccepted { get; set; }
    public bool IsExpired { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public string CreatedOn { get; set; }
    public string Status { get; set; }
    public string StatusText { get; set; }
}

public record CreditManagementModel : BaseNopModel
{
    public CreditManagementModel()
    {
        LedgerEntries = new List<CreditLedgerRowModel>();
        ActivitySearchModel = new ApplicantCreditActivitySearchModel();
    }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Credits.CustomerId")]
    public int CustomerId { get; set; }

    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public string CustomerAdminUrl { get; set; }

    public IList<SelectListItem> AvailableCustomers { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Credits.Amount")]
    public decimal Amount { get; set; }

    public decimal WalletBalance { get; set; }
    public string ScopeTitle { get; set; }
    public IList<CreditLedgerRowModel> LedgerEntries { get; set; }
    public ApplicantCreditActivitySearchModel ActivitySearchModel { get; set; }
}

public record CreditLedgerRowModel : BaseNopModel
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; }
    public string CustomerAdminUrl { get; set; }
    public decimal Amount { get; set; }
    public string TransactionType { get; set; }
    public string Remarks { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public string CreatedOn { get; set; }
}

public record ApplicantCreditActivitySearchModel : BaseSearchModel
{
    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Credits.Activity.SearchKeyword")]
    public string SearchKeyword { get; set; }
}

public record ApplicantCreditActivityListModel : BasePagedListModel<ApplicantCreditActivityRowModel>;

public record ApplicantCreditActivityRowModel : BaseNopModel
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public string CustomerAdminUrl { get; set; }
    public string ViewLedgerUrl { get; set; }
    public decimal WalletBalance { get; set; }
    public decimal TotalDeposited { get; set; }
    public decimal TotalWithdrawn { get; set; }
    public DateTime? LastCreditActivityUtc { get; set; }
    public string LastCreditActivity { get; set; }
}

public record ScoreboardFilterModel : BaseSearchModel
{
    public ScoreboardFilterModel()
    {
        Rows = new List<ScoreboardRowModel>();
    }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.Candidate")]
    public string Candidate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.Vendor")]
    public string Vendor { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.JobPosting")]
    public string JobPosting { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.Status")]
    public string Status { get; set; }

    public IList<SelectListItem> AvailableStatuses { get; set; } = new List<SelectListItem>();

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.MinScore")]
    public decimal? MinScore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.MaxScore")]
    public decimal? MaxScore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.StartDate")]
    public DateTime? StartDate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.Scoreboard.EndDate")]
    public DateTime? EndDate { get; set; }

    public IList<ScoreboardRowModel> Rows { get; set; }
}

public record ScoreboardListModel : BasePagedListModel<ScoreboardRowModel>;

public record ScoreboardRowModel : BaseNopModel
{
    public int SessionId { get; set; }
    public int ApplicationId { get; set; }
    public int ProductId { get; set; }
    public int VendorId { get; set; }
    public int CandidateCustomerId { get; set; }
    public string CandidateName { get; set; }
    public string CandidateEmail { get; set; }
    public string CandidateAdminUrl { get; set; }
    public string VendorName { get; set; }
    public string VendorAdminUrl { get; set; }
    public string JobTitle { get; set; }
    public string ProductAdminUrl { get; set; }
    public string Status { get; set; }
    public decimal Score { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
    public string CompletedOn { get; set; }
    public string ReportUrl { get; set; }
}

public record MockPracticeSessionSearchModel : BaseSearchModel
{
    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Customer")]
    public string CustomerKeyword { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Product")]
    public string ProductKeyword { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Difficulty")]
    public string Difficulty { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.Status")]
    public string Status { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.HasResume")]
    public bool? HasResume { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.QuestionCount")]
    public int? QuestionCount { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.MinScore")]
    public decimal? MinScore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.MaxScore")]
    public decimal? MaxScore { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.DateFrom")]
    public DateTime? DateFrom { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.MockPracticeSessions.DateTo")]
    public DateTime? DateTo { get; set; }

    public IList<SelectListItem> AvailableStatuses { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableDifficulties { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableHasResumeOptions { get; set; } = new List<SelectListItem>();
}

public record MockPracticeSessionListModel : BasePagedListModel<MockPracticeSessionRowModel>;

public record MockPracticeSessionRowModel : BaseNopModel
{
    public int SessionId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public string CustomerAdminUrl { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string Difficulty { get; set; }
    public string Status { get; set; }
    public bool HasResume { get; set; }
    public int QuestionCount { get; set; }
    public decimal Score { get; set; }
    public string SelectedInputs { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? StartedOnUtc { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
    public string CreatedOn { get; set; }
    public string StartedOn { get; set; }
    public string CompletedOn { get; set; }
    public string ReportUrl { get; set; }
}

public record FeedbackReportSearchModel : BaseSearchModel
{
    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.FeedbackReports.Candidate")]
    public string CandidateKeyword { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.FeedbackReports.Issue")]
    public string Issue { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.FeedbackReports.Helpfulness")]
    public string Helpfulness { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.FeedbackReports.SubmittedFrom")]
    public DateTime? SubmittedFrom { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.FeedbackReports.SubmittedTo")]
    public DateTime? SubmittedTo { get; set; }

    [NopResourceDisplayName("Plugins.Misc.AIInterview.Admin.FeedbackReports.HasAttachment")]
    public bool? HasAttachment { get; set; }

    public IList<SelectListItem> AvailableIssues { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableHelpfulnessOptions { get; set; } = new List<SelectListItem>();
    public IList<SelectListItem> AvailableHasAttachmentOptions { get; set; } = new List<SelectListItem>();
}

public record FeedbackReportListModel : BasePagedListModel<FeedbackReportRowModel>;

public record FeedbackReportRowModel : BaseNopModel
{
    public int SessionId { get; set; }
    public int CustomerId { get; set; }
    public string Submitted { get; set; }
    public DateTime? SubmittedOnUtc { get; set; }
    public string CandidateName { get; set; }
    public string CandidateEmail { get; set; }
    public string CandidateAdminUrl { get; set; }
    public string Issue { get; set; }
    public string Helpfulness { get; set; }
    public string CommentPreview { get; set; }
    public bool HasAttachment { get; set; }
    public string Attachment { get; set; }
    public string DetailsUrl { get; set; }
}

public record CandidateDetailsModel : BaseNopModel
{
    public int SessionId { get; set; }
    public int ApplicationId { get; set; }
    public int ProductId { get; set; }
    public int CandidateCustomerId { get; set; }
    public string CandidateName { get; set; }
    public string CandidateEmail { get; set; }
    public string CandidatePhone { get; set; }
    public string CandidateAdminUrl { get; set; }
    public string TargetRole { get; set; }
    public string ProductName { get; set; }
    public string ProductAdminUrl { get; set; }
    public string VendorName { get; set; }
    public string VendorAdminUrl { get; set; }
    public string Difficulty { get; set; }
    public string Status { get; set; }
    public string StatusBadgeClass { get; set; }
    public string LifecycleState { get; set; }
    public string LifecycleBadgeClass { get; set; }
    public string ComplianceStatus { get; set; }
    public string ComplianceBadgeClass { get; set; }
    public string SystemState { get; set; }
    public string SystemBadgeClass { get; set; }
    public decimal Score { get; set; }
    public decimal? AverageQuestionScore { get; set; }
    public decimal? AverageTechnicalScore { get; set; }
    public decimal? AverageCommunicationScore { get; set; }
    public decimal? AverageProfessionalismScore { get; set; }
    public decimal? AveragePositiveAttitudeScore { get; set; }
    public int QuestionCount { get; set; }
    public int AnsweredQuestionCount { get; set; }
    public bool HasRecording { get; set; }
    public string RecordingUrl { get; set; }
    public string ReportUrl { get; set; }
    public DateTime? AppliedOnUtc { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? StartedOnUtc { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
    public string AppliedOn { get; set; }
    public string CreatedOn { get; set; }
    public string StartedOn { get; set; }
    public string CompletedOn { get; set; }
    public string SummaryText { get; set; }
    public string FeedbackText { get; set; }
    public string CandidateFeedbackIssue { get; set; }
    public string CandidateFeedbackHelpfulness { get; set; }
    public string CandidateFeedbackComment { get; set; }
    public int CandidateFeedbackAttachmentDownloadId { get; set; }
    public string CandidateFeedbackAttachmentName { get; set; }
    public string CandidateFeedbackAttachmentUrl { get; set; }
    public DateTime? CandidateFeedbackSubmittedOnUtc { get; set; }
    public string CandidateFeedbackSubmittedOn { get; set; }
    public string ReportData { get; set; }
    public string QuestionScores { get; set; }
    public string SessionKey { get; set; }
    public string InternalSessionToken { get; set; }
    public string AzureMediaReference { get; set; }
    public string ApplicationTrackingReference { get; set; }
    public string StatusComment { get; set; }
    public IList<decimal> ParsedQuestionScores { get; set; } = new List<decimal>();
    public IList<CandidateDetailsTurnModel> Turns { get; set; } = new List<CandidateDetailsTurnModel>();
}

public record CandidateDetailsTurnModel : BaseNopModel
{
    public int TurnId { get; set; }
    public int SequenceNumber { get; set; }
    public string QuestionLabel { get; set; }
    public string QuestionText { get; set; }
    public string AnswerText { get; set; }
    public string Feedback { get; set; }
    public decimal? Score { get; set; }
    public decimal? TechnicalScore { get; set; }
    public decimal? CommunicationScore { get; set; }
    public decimal? ProfessionalismScore { get; set; }
    public decimal? PositiveAttitudeScore { get; set; }
    public DateTime AskedOnUtc { get; set; }
    public DateTime? AnsweredOnUtc { get; set; }
    public string AskedOn { get; set; }
    public string AnsweredOn { get; set; }
    public string RubricJson { get; set; }
    public string RawAiResponseJson { get; set; }
}
