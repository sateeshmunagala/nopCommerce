using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Core.Domain.Catalog;
using Microsoft.AspNetCore.Http;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.AIInterview.Services;

public interface IApplicationService
{
    Task SendApplicationSubmittedNotificationAsync(JobApplication application, int languageId);
    Task SendApplicationStatusUpdateNotificationAsync(JobApplication application, int languageId);
    Task InsertJobApplicationAsync(JobApplication application);
    Task<JobApplication> GetJobApplicationByIdAsync(int applicationId);
    Task<IList<JobApplication>> GetJobApplicationsByCustomerIdAsync(int customerId);
    Task<IList<JobApplication>> GetJobApplicationsByCustomerIdAndJobTitleAsync(int customerId, string jobTitle);
    Task<IPagedList<JobApplication>> GetApplicationsAsync(string candidateNameOrEmail = null, string status = null, decimal? minScore = null, decimal? maxScore = null, DateTime? startDate = null, DateTime? endDate = null, int productId = 0, int vendorId = 0, int pageIndex = 0, int pageSize = int.MaxValue, bool sortByScore = false);
    Task<int> GetApplicationCountAsync(int productId = 0, int vendorId = 0, string status = null);
    Task UpdateJobApplicationAsync(JobApplication application);
}

public interface IInterviewSessionService
{
    Task SendInterviewCompletionNotificationAsync(InterviewSession session, int languageId);
    Task SendRuntimeFeedbackSubmittedAdminNotificationAsync(InterviewSession session, int languageId);
    Task InsertInterviewSessionAsync(InterviewSession session);
    Task<InterviewSession> GetInterviewSessionByIdAsync(int sessionId);
    Task<InterviewSession> GetLatestCompletedSessionByCustomerIdAndProductIdAsync(int customerId, int productId);
    Task<decimal> GetHighestScoreByCustomerIdAndProductIdAsync(int customerId, int productId);
    Task<int> GetSponsorInviteAttemptCountAsync(int inviteId);
    Task<InterviewSession> GetSessionBySessionKeyAsync(string sessionKey);
    Task<InterviewSession> GetSessionByTokenAsync(string token);
    Task<InterviewSession> GetSessionByRecordingShareTokenAsync(string token);
    Task<IList<InterviewSession>> GetSessionsByCustomerIdAsync(int customerId);
    Task<IList<InterviewSession>> GetPreviousResumeSourceSessionsAsync(int customerId);
    Task<IList<InterviewSession>> GetCompletionWorkSessionsAsync(DateTime staleProcessingBeforeUtc, int maxCount = 20);
    Task<string> EnsureRecordingShareTokenAsync(InterviewSession session);
    Task UpdateInterviewSessionAsync(InterviewSession session);
    Task<bool> CanAccessReportAsync(int customerId, int sessionId);
}

public interface IInterviewTurnService
{
    Task<InterviewTurn> InsertInterviewTurnAsync(InterviewTurn turn);
    Task<IList<InterviewTurn>> GetTurnsBySessionIdAsync(int interviewSessionId);
    Task<InterviewTurn> GetLatestTurnBySessionIdAsync(int interviewSessionId);
    Task UpdateInterviewTurnAsync(InterviewTurn turn);
    Task DeleteInterviewTurnsAsync(IList<InterviewTurn> turns);
}

public interface IInterviewRuntimeService
{
    Task<InterviewRuntimeModel> GetRuntimeModelAsync(string token);
    Task<InterviewRuntimeModel> BeginInterviewAsync(string token, Customer customer = null);
    Task<InterviewRuntimeModel> EnsureInterviewStartedAsync(InterviewSession session, Customer customer = null);
    Task<PrepareInterviewResponseModel> PrepareInterviewAsync(string token, Customer customer = null);
    Task<SubmitInterviewAnswerResponse> SubmitAnswerAsync(SubmitInterviewAnswerRequest request);
    Task<CompleteInterviewResponse> CompleteInterviewAsync(string token, string reason = null);
    Task<CompleteInterviewResponse> ProcessCompletionWorkAsync(int sessionId);
    Task<SpeechTokenResponseModel> GetSpeechTokenAsync(string token);
    Task TrackSpeechSynthesisUsageAsync(SpeechSynthesisUsageRequest request);
    Task<RecordingUploadResponseModel> UploadRecordingAsync(string token, IFormFile recording);
}

public interface IResumeFileService
{
    ResumeFileValidationResult ValidateResumeFile(IFormFile file);
    Task<ResumeFileStoreResult> StoreResumeAsync(IFormFile file);
}

public interface IResumeTextExtractionService
{
    Task<ResumeTextExtractionResult> ExtractTextAsync(Nop.Core.Domain.Media.Download download);
}

public interface IResumeProfileService
{
    Task<ResumeProfileGenerationResult> EnsureResumeProfileAsync(JobApplication application, Product product = null, bool forceRegenerate = false);
    Task<ResumeProfileGenerationResult> EnsureResumeProfileAsync(InterviewSession session, Product product = null, bool forceRegenerate = false);
    AIResumeProfileResponse ParseProfile(string resumeProfileJson);
}

public interface IAIInterviewClient
{
    Task<AIInterviewClientResponse> GenerateQuestionAsync(AIInterviewClientRequest request);
    Task<AIResumeProfileResponse> AnalyzeResumeAsync(AIResumeProfileRequest request);
    Task<AIInterviewQuestionPlanResponse> GenerateQuestionPlanAsync(AIInterviewQuestionPlanRequest request);
    Task<AIInterviewClientResponse> ScoreAnswerAsync(AIInterviewClientRequest request);
    Task<AIInterviewFinalScoringResponse> ScoreInterviewAtCompletionAsync(AIInterviewFinalScoringRequest request);
    Task<AIInterviewStrengthsSummaryResponse> GenerateStrengthsSummaryAsync(AIInterviewStrengthsSummaryRequest request);
}

public interface IAzureOpenAiChatCompletionAdapter
{
    Task<AzureOpenAiChatCompletionResult> CompleteChatAsync(AzureOpenAiChatCompletionRequest request);
}

public sealed record AzureOpenAiChatCompletionRequest
{
    public string Mode { get; init; }
    public string OperationName { get; init; }
    public string SystemPrompt { get; init; }
    public string UserPrompt { get; init; }
    public int MaxCompletionTokens { get; init; }
}

public sealed record AzureOpenAiChatCompletionResult
{
    public bool Success { get; init; }
    public string Content { get; init; }
    public string FailureKind { get; init; }
    public string Reason { get; init; }
    public int? StatusCode { get; init; }
    public string ReasonPhrase { get; init; }
    public string ErrorCode { get; init; }
    public string ErrorMessage { get; init; }
    public string ResponseBody { get; init; }
    public string Endpoint { get; init; }
    public string EndpointHost { get; init; }
    public string DeploymentOrModel { get; init; }
    public string ModelName { get; init; }
    public string ResponseId { get; init; }
    public string FinishReason { get; init; }
    public bool IsLengthTruncated { get; init; }
    public string RequestShape { get; init; }
    public bool FallbackUsed { get; init; }
    public AzureOpenAiUsageInfo UsageInfo { get; init; }
}

public sealed record AzureOpenAiUsageInfo
{
    public string DeploymentOrModel { get; init; }
    public string ModelName { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
    public string RawUsageJson { get; init; }
    public string MetadataJson { get; init; }
}

public sealed record AzureOpenAiUsageRecordRequest
{
    public int InterviewSessionId { get; init; }
    public int? InterviewTurnId { get; init; }
    public string UsageKind { get; init; }
    public string OperationName { get; init; }
    public AzureOpenAiUsageInfo UsageInfo { get; init; }
    public string MetadataJson { get; init; }
}

public sealed record AzureSpeechUsageRecordRequest
{
    public int InterviewSessionId { get; init; }
    public int? InterviewTurnId { get; init; }
    public string UsageKind { get; init; }
    public string OperationName { get; init; }
    public int SpeechRecognitionCharacters { get; init; }
    public int SpeechSynthesisCharacters { get; init; }
    public long SpeechDurationMs { get; init; }
    public string ClientEventId { get; init; }
    public string MetadataJson { get; init; }
}

public interface IAzureUsageService
{
    Task RecordOpenAiUsageAsync(AzureOpenAiUsageRecordRequest request);
    Task RecordSpeechUsageAsync(AzureSpeechUsageRecordRequest request);
    Task RecalculateSessionSummaryAsync(int interviewSessionId);
}

public record AIInterviewClientRequest
{
    public string JobTitle { get; init; }
    public string JobContext { get; init; }
    public string Difficulty { get; init; }
    public string Prompt { get; init; }
    public string Question { get; init; }
    public string Answer { get; init; }
    public int QuestionNumber { get; init; }
    public string ResumeProfileJson { get; init; }
    public string CurrentTurnRubricJson { get; init; }
    public IList<string> PreviousQuestions { get; init; } = new List<string>();
    public IList<decimal> PreviousScores { get; init; } = new List<decimal>();
    public IList<AIInterviewHistoryItem> PreviousTurns { get; init; } = new List<AIInterviewHistoryItem>();
}

public sealed record ResumeFileValidationResult
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; }
    public string ErrorMessage { get; init; }
}

public sealed record ResumeFileStoreResult
{
    public bool Success { get; init; }
    public int DownloadId { get; init; }
    public string ErrorCode { get; init; }
    public string ErrorMessage { get; init; }
}

public sealed record ResumeTextExtractionResult
{
    public bool Success { get; init; }
    public string Text { get; init; }
    public string ErrorCode { get; init; }
    public string ErrorMessage { get; init; }
    public string ExceptionType { get; init; }
    public string DiagnosticMessage { get; init; }
}

public sealed record ResumeProfileGenerationResult
{
    public bool Success { get; init; }
    public string ProfileJson { get; init; }
    public AIResumeProfileResponse Profile { get; init; }
    public string ErrorCode { get; init; }
    public string ErrorMessage { get; init; }
}

public record AIResumeProfileRequest
{
    public string JobTitle { get; init; }
    public string JobContext { get; init; }
    public string ResumeText { get; init; }
}

public record AIResumeProfileResponse
{
    public bool Success { get; init; } = true;
    public IList<string> Skills { get; init; } = new List<string>();
    public IList<string> PrimarySkills { get; init; } = new List<string>();
    public IList<string> Tools { get; init; } = new List<string>();
    public IList<AIResumeProjectProfile> Projects { get; init; } = new List<AIResumeProjectProfile>();
    public string ExperienceSummary { get; init; }
    public IList<string> SenioritySignals { get; init; } = new List<string>();
    public IList<string> MissingOrUnclearAreas { get; init; } = new List<string>();
    public string ErrorMessage { get; init; }
    public string RawJson { get; init; }
    public AzureOpenAiUsageInfo UsageInfo { get; init; }
}

public record AIResumeProjectProfile
{
    public string Name { get; init; }
    public string Domain { get; init; }
    public IList<string> Technologies { get; init; } = new List<string>();
    public IList<string> Responsibilities { get; init; } = new List<string>();
    public string Impact { get; init; }
}

public record AIInterviewQuestionPlanRequest
{
    public string JobTitle { get; init; }
    public string JobContext { get; init; }
    public string Difficulty { get; init; }
    public int QuestionCount { get; init; }
    public int TotalQuestionCount { get; init; }
    public string Prompt { get; init; }
    public string ResumeProfileJson { get; init; }
    public IList<string> ExistingQuestions { get; init; } = new List<string>();
    public IList<string> ExistingCategories { get; init; } = new List<string>();
}

public record AIInterviewQuestionPlanResponse
{
    public bool Success { get; init; } = true;
    public IList<AIInterviewQuestionPlanItem> Questions { get; init; } = new List<AIInterviewQuestionPlanItem>();
    public string ErrorMessage { get; init; }
    public string RawJson { get; init; }
    public AzureOpenAiUsageInfo UsageInfo { get; init; }
}

public record AIInterviewQuestionPlanItem
{
    public int SequenceNumber { get; init; }
    public string Category { get; init; }
    public string Question { get; init; }
    public string ResumeEvidence { get; init; }
    public IList<string> ExpectedSignals { get; init; } = new List<string>();
    public AIInterviewQuestionRubric Rubric { get; init; } = new();
}

public record AIInterviewQuestionRubric
{
    public string Technical { get; init; }
    public string Communication { get; init; }
    public string Professionalism { get; init; }
    public string PositiveAttitude { get; init; }
}

public record AIInterviewHistoryItem
{
    public int SequenceNumber { get; init; }
    public string Question { get; init; }
    public string Answer { get; init; }
    public decimal? Score { get; init; }
    public string Feedback { get; init; }
}

public record AIInterviewFinalScoringRequest
{
    public string JobTitle { get; init; }
    public string JobContext { get; init; }
    public string Difficulty { get; init; }
    public string Prompt { get; init; }
    public string ResumeProfileJson { get; init; }
    public IList<AIInterviewFinalScoringTurnRequest> Turns { get; init; } = new List<AIInterviewFinalScoringTurnRequest>();
}

public record AIInterviewFinalScoringTurnRequest
{
    public int SequenceNumber { get; init; }
    public string Question { get; init; }
    public string Answer { get; init; }
    public string CurrentTurnRubricJson { get; init; }
}

public record AIInterviewFinalScoringResponse
{
    public bool Success { get; init; } = true;
    public IList<AIInterviewFinalScoringTurnResult> Turns { get; init; } = new List<AIInterviewFinalScoringTurnResult>();
    public decimal? Score { get; init; }
    public string Completion { get; init; }
    public string ErrorMessage { get; init; }
    public string RawJson { get; init; }
    public AzureOpenAiUsageInfo UsageInfo { get; init; }
}

public record AIInterviewStrengthsSummaryRequest
{
    public string JobTitle { get; init; }
    public string JobContext { get; init; }
    public string Difficulty { get; init; }
    public string ResumeProfileJson { get; init; }
    public IList<AIInterviewStrengthsSummaryTurnRequest> Turns { get; init; } = new List<AIInterviewStrengthsSummaryTurnRequest>();
}

public record AIInterviewStrengthsSummaryTurnRequest
{
    public int SequenceNumber { get; init; }
    public string Question { get; init; }
    public string Answer { get; init; }
    public decimal? Score { get; init; }
    public string Feedback { get; init; }
}

public record AIInterviewStrengthsSummaryResponse
{
    public bool Success { get; init; } = true;
    public string StrengthsText { get; init; }
    public string Confidence { get; init; }
    public IList<int> EvidenceTurnNumbers { get; init; } = new List<int>();
    public string ErrorMessage { get; init; }
    public string RawJson { get; init; }
    public AzureOpenAiUsageInfo UsageInfo { get; init; }
}

public record AIInterviewFinalScoringTurnResult
{
    public int SequenceNumber { get; init; }
    public decimal? Score { get; init; }
    public decimal? TechnicalScore { get; init; }
    public decimal? CommunicationScore { get; init; }
    public decimal? ProfessionalismScore { get; init; }
    public decimal? PositiveAttitudeScore { get; init; }
    public string Feedback { get; init; }
    public string AnswerQuality { get; init; }
    public string NonSubstantiveReason { get; init; }
    public string RubricJson { get; init; }
}

public record AIInterviewClientResponse
{
    public bool Success { get; init; } = true;
    public string Question { get; init; }
    public string NextQuestion { get; init; }
    public decimal? Score { get; init; }
    public decimal? TechnicalScore { get; init; }
    public decimal? CommunicationScore { get; init; }
    public decimal? ProfessionalismScore { get; init; }
    public decimal? PositiveAttitudeScore { get; init; }
    public string Feedback { get; init; }
    public bool Complete { get; init; }
    public string Completion { get; init; }
    public string AnswerQuality { get; init; }
    public string NonSubstantiveReason { get; init; }
    public string ErrorMessage { get; init; }
    public string RawJson { get; init; }
    public string RubricJson { get; init; }
    public AzureOpenAiUsageInfo UsageInfo { get; init; }
    public IList<AzureOpenAiUsageInfo> AdditionalUsageInfos { get; init; } = new List<AzureOpenAiUsageInfo>();
}

public interface ICreditService
{
    Task<CreditWallet> GetOrCreateWalletAsync(int customerId);
    Task AddCreditAsync(int customerId, decimal amount, string remarks);
    Task AddCreditAsync(int customerId, decimal amount, string remarks, string ledgerSource, int productId = 0, int orderId = 0);
    Task<bool> AuthorizeAndChargeAsync(int customerId, decimal amount, string remarks);
    Task<bool> AuthorizeAndChargeAsync(int customerId, decimal amount, string remarks, string ledgerSource, int productId = 0, int sponsorInviteId = 0);
}

public static class CreditLedgerSources
{
    public const string Order = "Order";
    public const string AdminTopUp = "Admin top-up";
    public const string InterviewUsage = "Interview usage";
    public const string SponsorInterviewUsage = "Sponsor interview usage";
    public const string Adjustment = "Adjustment";
}

public interface ICreditActivityService
{
    Task<CreditActivityModel> BuildCreditActivityModelAsync(Customer customer, int page, int pageSize);
}

public interface ICreditPurchaseService
{
    Task GrantCreditsForPaidOrderAsync(Nop.Core.Domain.Orders.Order order);
}

public static class CreditDepositSources
{
    public const string ViaOrder = "Via order";
    public const string ViaAdminTopUp = "Via admin top-up";
}

public sealed record CreditDepositNotificationRequest
{
    public int CustomerId { get; init; }
    public decimal CreditsDeposited { get; init; }
    public string DepositSource { get; init; }
    public int? OrderId { get; init; }
    public string Remarks { get; init; }
}

public interface ICreditDepositNotificationService
{
    Task SendCreditDepositedNotificationAsync(CreditDepositNotificationRequest request);
}

public interface ISponsorInviteService
{
    Task InsertSponsorInviteAsync(SponsorInvite invite);
    Task<SponsorInvite> GetSponsorInviteByCodeAsync(string code);
    Task CreateInviteAsync(int sponsorId, string email, int productId, int maxAttempts, DateTime? expiryDateUtc);
    Task<IList<SponsorInvite>> GetSponsorInvitesAsync(int sponsorId);
    Task DeactivateInviteAsync(int inviteId, int sponsorId);
    Task<bool> ValidateInviteAsync(string code, string email);
}

public interface IJobInterviewExperienceService
{
    Task EnsureInterviewDifficultyAttributeAsync(Product product);
    Task<string> ResolveInterviewDifficultyAsync(Product product, IFormCollection form);
}

public interface IJobRequirementService
{
    Task<bool> IsJobProductAsync(Product product);
    Task<JobRequirementsModel> GetRequirementsAsync(Product product);
    Task<JobRequirementsModel> GetRequirementsAsync(int productId);
    Task SaveRequirementsAsync(Product product, bool resumeRequired, bool interviewRequired, decimal minimumScore = 0, int questionCount = 3);
    Task SaveRequirementsAsync(int productId, bool resumeRequired, bool interviewRequired, decimal minimumScore = 0, int questionCount = 3);
}

public interface IJobProductAccessService
{
    Task<bool> CanViewJobProductAsync(int productId, bool allowAdminPreview = false);
    Task<bool> CanViewJobProductAsync(Product product, bool allowAdminPreview = false);
    Task<bool> CanAcceptJobApplicationsAsync(Product product);
    Task<bool> CanAppearInListingsAsync(Product product, bool allowAdminPreview = false);
}

public interface IAIInterviewJobDisplayService
{
    Task<AIInterviewJobProductCardModel> PrepareJobProductCardModelAsync(ProductOverviewModel productOverviewModel);
    Task<AIInterviewJobSpecificationSnapshotModel> GetSpecificationSnapshotAsync(int productId, ProductSpecificationModel preparedSpecificationModel = null);
    bool IsCompactSpecificationAttributeName(string name);
}
