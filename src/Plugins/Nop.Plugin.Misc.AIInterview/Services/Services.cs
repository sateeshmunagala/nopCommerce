using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Data.Extensions;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;

namespace Nop.Plugin.Misc.AIInterview.Services;

public class ApplicationService : IApplicationService
{
    private readonly IRepository<JobApplication> _applicationRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<InterviewSession> _sessionRepository;
    private readonly IRepository<Product> _productRepository;

    public ApplicationService(IRepository<JobApplication> applicationRepository,
        IRepository<Customer> customerRepository,
        IRepository<InterviewSession> sessionRepository,
        IRepository<Product> productRepository)
    {
        _applicationRepository = applicationRepository;
        _customerRepository = customerRepository;
        _sessionRepository = sessionRepository;
        _productRepository = productRepository;
    }

    public async Task InsertJobApplicationAsync(JobApplication application)
    {
        await _applicationRepository.InsertAsync(application);
    }

    public async Task<JobApplication> GetJobApplicationByIdAsync(int applicationId)
    {
        return await _applicationRepository.GetByIdAsync(applicationId);
    }

    public async Task<IList<JobApplication>> GetJobApplicationsByCustomerIdAsync(int customerId)
    {
        return await _applicationRepository.GetAllAsync(query => query.Where(a => a.CustomerId == customerId));
    }

    public async Task<IPagedList<JobApplication>> GetApplicationsAsync(string candidateNameOrEmail = null, string status = null, decimal? minScore = null, decimal? maxScore = null, DateTime? startDate = null, DateTime? endDate = null, int productId = 0, int vendorId = 0, int pageIndex = 0, int pageSize = int.MaxValue, bool sortByScore = false)
    {
        var query = _applicationRepository.Table;

        if (productId > 0)
            query = query.Where(a => a.ProductId == productId);

        if (vendorId > 0)
        {
            var productIds = _productRepository.Table.Where(p => p.VendorId == vendorId).Select(p => p.Id);
            query = query.Where(a => productIds.Contains(a.ProductId));
        }

        if (!string.IsNullOrEmpty(status))
            query = query.Where(a => a.Status == status);

        if (startDate.HasValue)
            query = query.Where(a => a.CreatedOnUtc >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.CreatedOnUtc <= endDate.Value);

        if (!string.IsNullOrEmpty(candidateNameOrEmail))
        {
            var customerIds = _customerRepository.Table
                .Where(c => c.Email.Contains(candidateNameOrEmail) || (c.FirstName + " " + c.LastName).Contains(candidateNameOrEmail))
                .Select(c => c.Id);
            query = query.Where(a => customerIds.Contains(a.CustomerId));
        }

        if (minScore.HasValue || maxScore.HasValue || sortByScore)
        {
            var sessionQuery = _sessionRepository.Table
                .GroupBy(s => s.JobApplicationId)
                .Select(g => new { JobApplicationId = g.Key, MaxScore = g.Max(s => s.Score) });

            if (minScore.HasValue)
                sessionQuery = sessionQuery.Where(s => s.MaxScore >= minScore.Value);

            if (maxScore.HasValue)
                sessionQuery = sessionQuery.Where(s => s.MaxScore <= maxScore.Value);

            query = from a in query
                    join s in sessionQuery on a.Id equals s.JobApplicationId into joinedSessions
                    from s in joinedSessions.DefaultIfEmpty()
                    orderby sortByScore ? (s != null ? s.MaxScore : 0) : 0 descending
                    select a;
        }
        else
        {
            query = query.OrderByDescending(a => a.CreatedOnUtc);
        }

        return await query.ToPagedListAsync(pageIndex, pageSize);
    }

    public async Task UpdateJobApplicationAsync(JobApplication application)
    {
        await _applicationRepository.UpdateAsync(application);
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

    public async Task<InterviewSession> GetLatestCompletedSessionByCustomerIdAsync(int customerId)
    {
        return (await _sessionRepository.GetAllAsync(query => query
            .Where(s => s.CustomerId == customerId && s.CompletedOnUtc.HasValue)
            .OrderByDescending(s => s.CompletedOnUtc)))
            .FirstOrDefault();
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
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly ILocalizationService _localizationService;

    public SponsorInviteService(IRepository<SponsorInvite> inviteRepository,
        IProductService productService,
        ICustomerService customerService,
        ILocalizationService localizationService)
    {
        _inviteRepository = inviteRepository;
        _productService = productService;
        _customerService = customerService;
        _localizationService = localizationService;
    }

    public async Task InsertSponsorInviteAsync(SponsorInvite invite)
    {
        await _inviteRepository.InsertAsync(invite);
    }

    public async Task<SponsorInvite> GetSponsorInviteByCodeAsync(string code)
    {
        return (await _inviteRepository.GetAllAsync(query => query.Where(i => i.InviteCode == code))).FirstOrDefault();
    }

    public async Task CreateInviteAsync(int sponsorId, string email, int productId, int maxAttempts, DateTime? expiryDateUtc)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.EmailRequired"));

        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.ProductNotFound"));

        var sponsor = await _customerService.GetCustomerByIdAsync(sponsorId);
        if (product.VendorId == 0 || product.VendorId != sponsor.VendorId)
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidOwnership"));

        if (maxAttempts <= 0)
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidAttempts"));

        if (expiryDateUtc.HasValue && expiryDateUtc.Value <= DateTime.UtcNow)
            throw new NopException(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Admin.Invite.InvalidExpiry"));

        var invite = new SponsorInvite
        {
            SponsorId = sponsorId,
            ProductId = productId,
            Email = email,
            MaxAttempts = maxAttempts,
            ExpiryDateUtc = expiryDateUtc,
            InviteCode = Guid.NewGuid().ToString("N"),
            IsAccepted = false,
            CreatedOnUtc = DateTime.UtcNow
        };

        await InsertSponsorInviteAsync(invite);
    }
}
