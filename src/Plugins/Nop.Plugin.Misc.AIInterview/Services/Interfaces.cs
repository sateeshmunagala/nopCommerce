using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Services;

public interface IApplicationService
{
    Task InsertJobApplicationAsync(JobApplication application);
    Task<JobApplication> GetJobApplicationByIdAsync(int applicationId);
    Task<IList<JobApplication>> GetJobApplicationsByCustomerIdAsync(int customerId);
    Task<IPagedList<JobApplication>> GetApplicationsAsync(string candidateNameOrEmail = null, string status = null, decimal? minScore = null, decimal? maxScore = null, DateTime? startDate = null, DateTime? endDate = null, int productId = 0, int vendorId = 0, int pageIndex = 0, int pageSize = int.MaxValue, bool sortByScore = false);
    Task UpdateJobApplicationAsync(JobApplication application);
}

public interface IInterviewSessionService
{
    Task InsertInterviewSessionAsync(InterviewSession session);
    Task<InterviewSession> GetInterviewSessionByIdAsync(int sessionId);
    Task<InterviewSession> GetLatestCompletedSessionByCustomerIdAsync(int customerId);
}

public interface ICreditService
{
    Task<CreditWallet> GetOrCreateWalletAsync(int customerId);
    Task AddCreditAsync(int customerId, decimal amount, string remarks);
}

public interface ISponsorInviteService
{
    Task InsertSponsorInviteAsync(SponsorInvite invite);
    Task<SponsorInvite> GetSponsorInviteByCodeAsync(string code);
    Task CreateInviteAsync(int sponsorId, string email, int productId, int maxAttempts, DateTime? expiryDateUtc);
}
