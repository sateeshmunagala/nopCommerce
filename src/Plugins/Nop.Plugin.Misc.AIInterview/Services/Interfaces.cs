using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Core.Domain.Catalog;
using Microsoft.AspNetCore.Http;

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
    Task<InterviewSession> GetSessionBySessionKeyAsync(string sessionKey);
    Task<InterviewSession> GetSessionByTokenAsync(string token);
    Task<IList<InterviewSession>> GetSessionsByCustomerIdAsync(int customerId);
    Task UpdateInterviewSessionAsync(InterviewSession session);
    Task<bool> CanAccessReportAsync(int customerId, int sessionId);
}

public interface ICreditService
{
    Task<CreditWallet> GetOrCreateWalletAsync(int customerId);
    Task AddCreditAsync(int customerId, decimal amount, string remarks);
    Task<bool> AuthorizeAndChargeAsync(int customerId, decimal amount, string remarks);
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
