using Nop.Data;
using Nop.Plugin.Misc.AIInterview.Domain;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class ApplicationService : IApplicationService
{
    private readonly IRepository<JobApplication> _applicationRepository;

    public ApplicationService(IRepository<JobApplication> applicationRepository)
    {
        _applicationRepository = applicationRepository;
    }

    public async Task InsertJobApplicationAsync(JobApplication application)
    {
        await _applicationRepository.InsertAsync(application);
    }

    public async Task<JobApplication> GetJobApplicationByIdAsync(int applicationId)
    {
        return await _applicationRepository.GetByIdAsync(applicationId);
    }
}

public class InterviewSessionService : IInterviewSessionService
{
    private readonly IRepository<InterviewSession> _sessionRepository;

    public InterviewSessionService(IRepository<InterviewSession> sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }

    public async Task InsertInterviewSessionAsync(InterviewSession session)
    {
        await _sessionRepository.InsertAsync(session);
    }

    public async Task<InterviewSession> GetInterviewSessionByIdAsync(int sessionId)
    {
        return await _sessionRepository.GetByIdAsync(sessionId);
    }
}

public class CreditService : ICreditService
{
    private readonly IRepository<CreditWallet> _walletRepository;
    private readonly IRepository<CreditLedgerEntry> _ledgerRepository;

    public CreditService(IRepository<CreditWallet> walletRepository, IRepository<CreditLedgerEntry> ledgerRepository)
    {
        _walletRepository = walletRepository;
        _ledgerRepository = ledgerRepository;
    }

    public async Task<CreditWallet> GetOrCreateWalletAsync(int customerId)
    {
        var wallet = (await _walletRepository.GetAllAsync(query => query.Where(w => w.CustomerId == customerId))).FirstOrDefault();
        if (wallet == null)
        {
            wallet = new CreditWallet { CustomerId = customerId, Balance = 0 };
            await _walletRepository.InsertAsync(wallet);
        }
        return wallet;
    }

    public async Task AddCreditAsync(int customerId, decimal amount, string remarks)
    {
        var wallet = await GetOrCreateWalletAsync(customerId);
        wallet.Balance += amount;
        await _walletRepository.UpdateAsync(wallet);

        await _ledgerRepository.InsertAsync(new CreditLedgerEntry
        {
            CreditWalletId = wallet.Id,
            Amount = amount,
            TransactionType = "Deposit",
            Remarks = remarks,
            CreatedOnUtc = DateTime.UtcNow
        });
    }
}

public class SponsorInviteService : ISponsorInviteService
{
    private readonly IRepository<SponsorInvite> _inviteRepository;

    public SponsorInviteService(IRepository<SponsorInvite> inviteRepository)
    {
        _inviteRepository = inviteRepository;
    }

    public async Task InsertSponsorInviteAsync(SponsorInvite invite)
    {
        await _inviteRepository.InsertAsync(invite);
    }

    public async Task<SponsorInvite> GetSponsorInviteByCodeAsync(string code)
    {
        return (await _inviteRepository.GetAllAsync(query => query.Where(i => i.InviteCode == code))).FirstOrDefault();
    }
}
