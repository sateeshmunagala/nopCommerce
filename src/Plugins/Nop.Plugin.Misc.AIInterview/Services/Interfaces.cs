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
    Task UpdateJobApplicationAsync(JobApplication application);
}

public interface IInterviewSessionService
{
    Task SendInterviewCompletionNotificationAsync(InterviewSession session, int languageId);
    Task InsertInterviewSessionAsync(InterviewSession session);
    Task<InterviewSession> GetInterviewSessionByIdAsync(int sessionId);
    Task<InterviewSession> GetLatestCompletedSessionByCustomerIdAndProductIdAsync(int customerId, int productId);
    Task<decimal> GetHighestScoreByCustomerIdAndProductIdAsync(int customerId, int productId);
    Task<int> GetSponsorInviteAttemptCountAsync(int inviteId);
    Task<InterviewSession> GetSessionBySessionKeyAsync(string sessionKey);
    Task<InterviewSession> GetSessionByTokenAsync(string token);
    Task<InterviewSession> GetSessionByRecordingShareTokenAsync(string token);
    Task<IList<InterviewSession>> GetSessionsByCustomerIdAsync(int customerId);
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
}

public interface IInterviewRuntimeService
{
    Task<InterviewRuntimeModel> GetRuntimeModelAsync(string token);
    Task<InterviewRuntimeModel> BeginInterviewAsync(string token, Customer customer = null);
    Task<InterviewRuntimeModel> EnsureInterviewStartedAsync(InterviewSession session, Customer customer = null);
    Task<SubmitInterviewAnswerResponse> SubmitAnswerAsync(string token, string answer);
    Task<CompleteInterviewResponse> CompleteInterviewAsync(string token, string reason = null);
    Task<SpeechTokenResponseModel> GetSpeechTokenAsync(string token);
    Task<RecordingUploadResponseModel> UploadRecordingAsync(string token, IFormFile recording);
}

public interface IAIInterviewClient
{
    Task<AIInterviewClientResponse> GenerateQuestionAsync(AIInterviewClientRequest request);
    Task<AIInterviewClientResponse> ScoreAnswerAsync(AIInterviewClientRequest request);
}

public record AIInterviewClientRequest
{
    public string JobTitle { get; init; }
    public string Difficulty { get; init; }
    public string Prompt { get; init; }
    public string Question { get; init; }
    public string Answer { get; init; }
    public int QuestionNumber { get; init; }
    public IList<string> PreviousQuestions { get; init; } = new List<string>();
    public IList<decimal> PreviousScores { get; init; } = new List<decimal>();
    public IList<AIInterviewHistoryItem> PreviousTurns { get; init; } = new List<AIInterviewHistoryItem>();
}

public record AIInterviewHistoryItem
{
    public int SequenceNumber { get; init; }
    public string Question { get; init; }
    public string Answer { get; init; }
    public decimal? Score { get; init; }
    public string Feedback { get; init; }
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
    public string ErrorMessage { get; init; }
    public string RawJson { get; init; }
    public string RubricJson { get; init; }
}

public interface ICreditService
{
    Task<CreditWallet> GetOrCreateWalletAsync(int customerId);
    Task AddCreditAsync(int customerId, decimal amount, string remarks);
    Task<bool> AuthorizeAndChargeAsync(int customerId, decimal amount, string remarks);
}

public interface ICreditPurchaseService
{
    Task GrantCreditsForPaidOrderAsync(Nop.Core.Domain.Orders.Order order);
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

public interface IAIInterviewJobDisplayService
{
    Task<AIInterviewJobProductCardModel> PrepareJobProductCardModelAsync(ProductOverviewModel productOverviewModel);
    Task<AIInterviewJobSpecificationSnapshotModel> GetSpecificationSnapshotAsync(int productId, ProductSpecificationModel preparedSpecificationModel = null);
}
