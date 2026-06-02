using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Services;

public interface IApplicationService
{
    Task InsertJobApplicationAsync(JobApplication application);
    Task<JobApplication> GetJobApplicationByIdAsync(int applicationId);
    Task<IList<JobApplication>> GetJobApplicationsByCustomerIdAsync(int customerId);
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
}
